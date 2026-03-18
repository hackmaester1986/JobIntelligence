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

public class GreenhouseCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<GreenhouseCollector> logger) : IJobCollector
{
    public string SourceName => "greenhouse";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.GreenhouseBoardToken))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Greenhouse board token");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Greenhouse");

        // Greenhouse returns all jobs in a single request when content=true; pagination is not supported with content.
        List<GreenhouseJob> fetched;
        try
        {
            var url = $"v1/boards/{company.GreenhouseBoardToken}/jobs?content=true";
            var response = await client.GetFromJsonAsync<GreenhouseResponse>(url, ct);
            fetched = response?.Jobs ?? new List<GreenhouseJob>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => j.Id.ToString()).ToHashSet();

        // Batch-load all existing postings for this company+source in one query
        var existingMap = await db.JobPostings
            .Where(p => p.SourceId == source.Id && p.CompanyId == company.Id)
            .ToDictionaryAsync(p => p.ExternalId, ct);

        // Load removed postings' hashes for repost detection (hash -> most recent removed posting)
        var removedHashMap = await db.JobPostings
            .Where(p => p.SourceId == source.Id && p.CompanyId == company.Id && !p.IsActive && p.DescriptionHash != null)
            .Select(p => new { p.Id, p.DescriptionHash, p.RepostCount })
            .ToListAsync(ct);
        var removedByHash = removedHashMap
            .GroupBy(p => p.DescriptionHash!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

        // Mark removed postings
        foreach (var existing in existingMap.Values.Where(e => e.IsActive && !fetchedIds.Contains(e.ExternalId)))
        {
            existing.IsActive = false;
            existing.RemovedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            removedCount++;
        }

        // Upsert fetched postings
        foreach (var job in fetched)
        {
            var externalId = job.Id.ToString();
            var posting = MapToPosting(job, source.Id, company.Id);

            if (!existingMap.TryGetValue(externalId, out var existing))
            {
                if (posting.DescriptionHash != null && removedByHash.TryGetValue(posting.DescriptionHash, out var prev))
                {
                    posting.PreviousPostingId = prev.Id;
                    posting.RepostCount = (short)(prev.RepostCount + 1);
                }
                db.JobPostings.Add(posting);
                newCount++;
            }
            else
            {
                var changed = DetectChanges(existing, posting);
                if (changed.Count > 0)
                {
                    var snapshot = new JobSnapshot
                    {
                        JobPostingId = existing.Id,
                        SnapshotAt = DateTime.UtcNow,
                        ChangedFields = JsonDocument.Parse(JsonSerializer.Serialize(changed)),
                        RawData = posting.RawData
                    };
                    db.JobSnapshots.Add(snapshot);

                    ApplyChanges(existing, posting);
                    updatedCount++;
                }

                existing.LastSeenAt = DateTime.UtcNow;
                existing.IsActive = true;
                existing.RemovedAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Greenhouse [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(GreenhouseJob job, int sourceId, long companyId)
    {
        var location = job.Location?.Name ?? string.Empty;
        var loc = LocationParser.Parse(location);
        var salary = Parse(job.Content);

        return new JobPosting
        {
            ExternalId = job.Id.ToString(),
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Title ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Title),
            Department = job.Departments?.FirstOrDefault()?.Name,
            LocationRaw = Truncate(location, 500),
            LocationCity = loc.City,
            LocationState = loc.State,
            LocationCountry = loc.Country,
            IsRemote = loc.IsRemote,
            IsHybrid = loc.IsHybrid,
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            DescriptionHtml = job.Content,
            Description = StripHtml(job.Content),
            DescriptionHash = Compute(StripHtml(job.Content)),
            ApplyUrl = job.AbsoluteUrl,
            ApplyUrlDomain = ExtractDomain(job.AbsoluteUrl),
            PostedAt = job.UpdatedAt.HasValue ? DateTime.SpecifyKind(job.UpdatedAt.Value, DateTimeKind.Utc) : (DateTime?)null,
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
        existing.ApplyUrl = incoming.ApplyUrl;
        existing.ApplyUrlDomain = incoming.ApplyUrlDomain;
        existing.DescriptionHtml = incoming.DescriptionHtml;
        existing.Description = incoming.Description;
        existing.DescriptionHash = incoming.DescriptionHash;
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

    // Greenhouse API models
    private record GreenhouseResponse([property: JsonPropertyName("jobs")] List<GreenhouseJob> Jobs);
    private record GreenhouseJob(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("absolute_url")] string? AbsoluteUrl,
        [property: JsonPropertyName("location")] GreenhouseLocation? Location,
        [property: JsonPropertyName("departments")] List<GreenhouseDepartment>? Departments,
        [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt
    );
    private record GreenhouseLocation([property: JsonPropertyName("name")] string? Name);
    private record GreenhouseDepartment([property: JsonPropertyName("name")] string? Name);
}
