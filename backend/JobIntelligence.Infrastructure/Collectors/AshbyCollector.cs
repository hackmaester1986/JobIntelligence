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

public class AshbyCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<AshbyCollector> logger) : IJobCollector
{
    public string SourceName => "ashby";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.AshbyBoardSlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Ashby board slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Ashby");

        List<AshbyJob> fetched;
        try
        {
            var response = await client.GetFromJsonAsync<AshbyResponse>(
                $"posting-api/job-board/{company.AshbyBoardSlug}", ct);
            fetched = response?.Jobs ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Ashby board not found for {Company} (slug: {Slug}) — clearing slug",
                company.CanonicalName, company.AshbyBoardSlug);
            company.AshbyBoardSlug = null;
            await db.SaveChangesAsync(ct);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "Board not found (404) — slug cleared");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Ashby jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => j.Id).ToHashSet();

        var existingMap = await db.JobPostings
            .Where(p => p.SourceId == source.Id && p.CompanyId == company.Id)
            .ToDictionaryAsync(p => p.ExternalId, ct);

        // Load removed postings' hashes for repost detection
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

            if (!existingMap.TryGetValue(job.Id, out var existing))
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
        logger.LogInformation("Ashby [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(AshbyJob job, int sourceId, long companyId)
    {
        var locationText = job.Location ?? string.Empty;
        var loc = LocationParser.Parse(locationText);
        var salary = Parse(job.DescriptionHtml);

        var isRemote = job.IsRemote == true || job.WorkplaceType == "Remote" || loc.IsRemote;
        var isHybrid = job.WorkplaceType == "Hybrid" || loc.IsHybrid;

        var employmentType = job.EmploymentType?.ToLowerInvariant() switch
        {
            "fulltime" => "full-time",
            "parttime" => "part-time",
            "contract" => "contract",
            "intern" => "internship",
            _ => null
        };

        return new JobPosting
        {
            ExternalId = job.Id,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Title ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Title),
            Department = job.Department,
            Team = job.Team,
            LocationRaw = Truncate(locationText, 500),
            LocationCity = loc.City,
            LocationState = loc.State,
            LocationCountry = loc.Country,
            IsRemote = isRemote,
            IsHybrid = isHybrid,
            IsUsPosting = UsLocationClassifier.Classify(Truncate(locationText, 500)),
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            DescriptionHtml = job.DescriptionHtml,
            Description = StripHtml(job.DescriptionHtml),
            IsRemoteInDescription = LocationParser.HasRemoteInDescription(StripHtml(job.DescriptionHtml)),
            DescriptionHash = Compute(StripHtml(job.DescriptionHtml)),
            ApplyUrl = job.ApplyUrl,
            ApplyUrlDomain = ExtractDomain(job.ApplyUrl),
            PostedAt = job.PublishedAt.HasValue
                ? DateTime.SpecifyKind(job.PublishedAt.Value.UtcDateTime, DateTimeKind.Utc)
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
        existing.Team = incoming.Team;
        existing.LocationRaw = incoming.LocationRaw;
        existing.LocationCity = incoming.LocationCity;
        existing.LocationState = incoming.LocationState;
        existing.LocationCountry = incoming.LocationCountry;
        existing.IsRemote = incoming.IsRemote;
        existing.IsHybrid = incoming.IsHybrid;
        existing.IsUsPosting = incoming.IsUsPosting;
        existing.Department = incoming.Department;
        existing.EmploymentType = incoming.EmploymentType;
        existing.DescriptionHtml = incoming.DescriptionHtml;
        existing.Description = incoming.Description;
        existing.IsRemoteInDescription = incoming.IsRemoteInDescription;
        existing.DescriptionHash = incoming.DescriptionHash;
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

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Trim();
    }

    private record AshbyResponse([property: JsonPropertyName("jobs")] List<AshbyJob> Jobs);

    private record AshbyJob(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("department")] string? Department,
        [property: JsonPropertyName("team")] string? Team,
        [property: JsonPropertyName("employmentType")] string? EmploymentType,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("isRemote")] bool? IsRemote,
        [property: JsonPropertyName("workplaceType")] string? WorkplaceType,
        [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("applyUrl")] string? ApplyUrl,
        [property: JsonPropertyName("descriptionHtml")] string? DescriptionHtml
    );
}
