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

public class WorkableCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<WorkableCollector> logger) : IJobCollector
{
    public string SourceName => "workable";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.WorkableSlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Workable slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Workable");

        var fetched = new List<WorkableJob>();
        try
        {
            string? cursor = null;
            do
            {
                var url = $"api/v3/accounts/{company.WorkableSlug}/jobs";
                if (cursor != null) url += $"?after={Uri.EscapeDataString(cursor)}";

                var response = await client.GetFromJsonAsync<WorkableResponse>(url, ct);
                if (response?.Results == null) break;

                fetched.AddRange(response.Results);
                cursor = response.Paging?.Next;
            } while (cursor != null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Workable jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => j.Shortcode).ToHashSet();

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

            if (!existingMap.TryGetValue(job.Shortcode ?? string.Empty, out var existing))
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
        logger.LogInformation("Workable [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(WorkableJob job, int sourceId, long companyId)
    {
        var locationText = job.Location?.LocationStr ?? string.Empty;
        var loc = LocationParser.Parse(locationText);
        var salary = Parse(null); // Workable doesn't expose description in list API

        var isRemote = job.Remote == true || loc.IsRemote;
        var isHybrid = loc.IsHybrid;

        var employmentType = job.EmploymentType?.ToLowerInvariant() switch
        {
            "full_time" => "full-time",
            "part_time" => "part-time",
            "contract" => "contract",
            "intern" => "internship",
            _ => null
        };

        return new JobPosting
        {
            ExternalId = job.Shortcode ?? string.Empty,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Title ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Title),
            Department = job.Department?.Name,
            LocationRaw = Truncate(locationText, 500),
            LocationCity = job.Location?.City ?? loc.City,
            LocationState = loc.State,
            LocationCountry = job.Location?.Country ?? loc.Country,
            IsRemote = isRemote,
            IsHybrid = isHybrid,
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            ApplyUrl = job.ApplicationUrl,
            ApplyUrlDomain = ExtractDomain(job.ApplicationUrl),
            PostedAt = job.CreatedAt.HasValue
                ? DateTime.SpecifyKind(job.CreatedAt.Value, DateTimeKind.Utc)
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

    private record WorkableResponse(
        [property: JsonPropertyName("results")] List<WorkableJob>? Results,
        [property: JsonPropertyName("paging")] WorkablePaging? Paging);

    private record WorkablePaging(
        [property: JsonPropertyName("next")] string? Next);

    private record WorkableJob(
        [property: JsonPropertyName("shortcode")] string? Shortcode,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("department")] WorkableDepartment? Department,
        [property: JsonPropertyName("location")] WorkableLocation? Location,
        [property: JsonPropertyName("employment_type")] string? EmploymentType,
        [property: JsonPropertyName("remote")] bool? Remote,
        [property: JsonPropertyName("application_url")] string? ApplicationUrl,
        [property: JsonPropertyName("created_at")] DateTime? CreatedAt);

    private record WorkableDepartment(
        [property: JsonPropertyName("name")] string? Name);

    private record WorkableLocation(
        [property: JsonPropertyName("location_str")] string? LocationStr,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country);
}
