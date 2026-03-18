using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Parsing;
using static JobIntelligence.Infrastructure.Parsing.DescriptionHashHelper;
using static JobIntelligence.Infrastructure.Parsing.SalaryParser;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Collectors;

public class SmartRecruitersCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<SmartRecruitersCollector> logger) : IJobCollector
{
    public string SourceName => "smartrecruiters";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.SmartRecruitersSlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No SmartRecruiters slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("SmartRecruiters");

        var fetched = new List<SmartRecruitersJob>();
        try
        {
            const int limit = 100;
            int offset = 0;
            int totalFound;
            do
            {
                var response = await client.GetFromJsonAsync<SmartRecruitersResponse>(
                    $"v1/companies/{company.SmartRecruitersSlug}/postings?limit={limit}&offset={offset}", ct);
                if (response?.Content == null) break;

                fetched.AddRange(response.Content);
                totalFound = response.TotalFound;
                offset += response.Content.Count;
            } while (offset < totalFound);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch SmartRecruiters jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => j.Id).ToHashSet();

        var existingMap = await db.JobPostings
            .Where(p => p.SourceId == source.Id && p.CompanyId == company.Id)
            .ToDictionaryAsync(p => p.ExternalId, ct);

        var removedHashMap = await db.JobPostings
            .Where(p => p.SourceId == source.Id && p.CompanyId == company.Id && !p.IsActive && p.DescriptionHash != null)
            .Select(p => new { p.Id, p.DescriptionHash, p.RepostCount })
            .ToListAsync(ct);
        var removedByHash = removedHashMap
            .GroupBy(p => p.DescriptionHash!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

        foreach (var existing in existingMap.Values.Where(e => e.IsActive && !fetchedIds.Contains(e.ExternalId)))
        {
            existing.IsActive = false;
            existing.RemovedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            removedCount++;
        }

        foreach (var job in fetched)
        {
            var mapped = MapToPosting(job, source.Id, company.Id);

            if (!existingMap.TryGetValue(job.Id ?? string.Empty, out var existing))
            {
                if (mapped.DescriptionHash != null && removedByHash.TryGetValue(mapped.DescriptionHash, out var prev))
                {
                    mapped.PreviousPostingId = prev.Id;
                    mapped.RepostCount = (short)(prev.RepostCount + 1);
                }
                db.JobPostings.Add(mapped);
                newCount++;
            }
            else
            {
                var changed = DetectChanges(existing, mapped);
                if (changed.Count > 0)
                {
                    db.JobSnapshots.Add(new JobSnapshot
                    {
                        JobPostingId = existing.Id,
                        SnapshotAt = DateTime.UtcNow,
                        ChangedFields = JsonDocument.Parse(JsonSerializer.Serialize(changed)),
                        RawData = mapped.RawData
                    });
                    ApplyChanges(existing, mapped);
                    updatedCount++;
                }

                existing.LastSeenAt = DateTime.UtcNow;
                existing.IsActive = true;
                existing.RemovedAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SmartRecruiters [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(SmartRecruitersJob job, int sourceId, long companyId)
    {
        var city = job.Location?.City ?? string.Empty;
        var country = job.Location?.Country ?? string.Empty;
        var locationText = string.Join(", ", new[] { city, country }.Where(s => !string.IsNullOrEmpty(s)));
        var loc = LocationParser.Parse(locationText);
        var salary = Parse(null);

        var isRemote = job.Location?.Remote == true || loc.IsRemote;
        var isHybrid = loc.IsHybrid;

        var employmentType = job.TypeOfEmployment?.Label?.ToLowerInvariant() switch
        {
            var t when t != null && t.Contains("full") => "full-time",
            var t when t != null && t.Contains("part") => "part-time",
            var t when t != null && t.Contains("contract") => "contract",
            var t when t != null && t.Contains("intern") => "internship",
            _ => null
        };

        return new JobPosting
        {
            ExternalId = job.Id ?? string.Empty,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Name ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Name),
            Department = job.Department?.Label,
            LocationRaw = Truncate(locationText, 500),
            LocationCity = loc.City,
            LocationState = loc.State,
            LocationCountry = loc.Country,
            IsRemote = isRemote,
            IsHybrid = isHybrid,
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            ApplyUrl = job.Ref,
            ApplyUrlDomain = ExtractDomain(job.Ref),
            PostedAt = job.ReleasedDate.HasValue
                ? DateTime.SpecifyKind(job.ReleasedDate.Value, DateTimeKind.Utc)
                : null,
            IsActive = true,
            RawData = JsonDocument.Parse(JsonSerializer.Serialize(job))
        };
    }

    private static Dictionary<string, object[]> DetectChanges(JobPosting existing, JobPosting incoming)
    {
        var changes = new Dictionary<string, object[]>();
        if (existing.Title != incoming.Title) changes["title"] = [existing.Title, incoming.Title];
        if (existing.LocationRaw != incoming.LocationRaw) changes["location_raw"] = [existing.LocationRaw ?? "", incoming.LocationRaw ?? ""];
        if (existing.Department != incoming.Department) changes["department"] = [existing.Department ?? "", incoming.Department ?? ""];
        if (existing.ApplyUrl != incoming.ApplyUrl) changes["apply_url"] = [existing.ApplyUrl ?? "", incoming.ApplyUrl ?? ""];
        return changes;
    }

    private static void ApplyChanges(JobPosting existing, JobPosting incoming)
    {
        existing.Title = incoming.Title;
        existing.SeniorityLevel = incoming.SeniorityLevel;
        existing.LocationRaw = incoming.LocationRaw;
        existing.LocationCity = incoming.LocationCity;
        existing.LocationState = incoming.LocationState;
        existing.LocationCountry = incoming.LocationCountry;
        existing.IsRemote = incoming.IsRemote;
        existing.IsHybrid = incoming.IsHybrid;
        existing.Department = incoming.Department;
        existing.EmploymentType = incoming.EmploymentType;
        existing.ApplyUrl = incoming.ApplyUrl;
        existing.ApplyUrlDomain = incoming.ApplyUrlDomain;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? ExtractDomain(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        return null;
    }

    private record SmartRecruitersResponse(
        [property: JsonPropertyName("content")] List<SmartRecruitersJob>? Content,
        [property: JsonPropertyName("totalFound")] int TotalFound);

    private record SmartRecruitersJob(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("department")] SmartRecruitersDepartment? Department,
        [property: JsonPropertyName("location")] SmartRecruitersLocation? Location,
        [property: JsonPropertyName("typeOfEmployment")] SmartRecruitersLabel? TypeOfEmployment,
        [property: JsonPropertyName("ref")] string? Ref,
        [property: JsonPropertyName("releasedDate")] DateTime? ReleasedDate);

    private record SmartRecruitersDepartment(
        [property: JsonPropertyName("label")] string? Label);

    private record SmartRecruitersLabel(
        [property: JsonPropertyName("label")] string? Label);

    private record SmartRecruitersLocation(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("remote")] bool? Remote);
}
