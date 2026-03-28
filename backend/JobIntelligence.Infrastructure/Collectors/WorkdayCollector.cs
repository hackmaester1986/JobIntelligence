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

public class WorkdayCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<WorkdayCollector> logger) : IJobCollector
{
    public string SourceName => "workday";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.WorkdayHost))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Workday host");
        if (string.IsNullOrEmpty(company.WorkdayCareerSite))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Workday career site");

        var tenant = company.WorkdayHost.Split('.')[0];
        var jobsUrl = $"https://{company.WorkdayHost}/wday/cxs/{tenant}/{company.WorkdayCareerSite}/jobs";

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("WorkdayJobs");

        var fetched = new List<WorkdayJobPosting>();
        try
        {
            const int limit = 20;
            int offset = 0;
            int total;
            do
            {
                var response = await client.PostAsJsonAsync(jobsUrl, new { appliedFacets = new { }, limit, offset, searchText = "" }, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Workday API returned {Status} for {Company}", (int)response.StatusCode, company.CanonicalName);
                    break;
                }

                var page = await response.Content.ReadFromJsonAsync<WorkdayJobsResponse>(cancellationToken: ct);
                if (page == null) break;

                var pageJobs = page.JobPostings ?? [];
                if (pageJobs.Count == 0) break; // guard against infinite loop if API returns empty pages

                fetched.AddRange(pageJobs);
                total = page.Total;
                offset += pageJobs.Count;

                logger.LogInformation("Workday [{Company}]: page offset={Offset}/{Total}", company.CanonicalName, offset, total);

                if (offset < total)
                    await Task.Delay(500, ct);
            } while (offset < total);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Workday jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => JobId(j.ExternalPath)).ToHashSet();

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

        var detailBaseUrl = $"https://{company.WorkdayHost}/wday/cxs/{tenant}/{company.WorkdayCareerSite}/job";

        // Fetch details for new jobs in parallel, update existing jobs sequentially
        var newJobs      = fetched.Where(j => !existingMap.ContainsKey(JobId(j.ExternalPath))).ToList();
        var existingJobs = fetched.Where(j =>  existingMap.ContainsKey(JobId(j.ExternalPath))).ToList();

        var semaphore = new SemaphoreSlim(3); // conservative for Workday
        var detailTasks = newJobs.Select(async job =>
        {
            var externalId = JobId(job.ExternalPath);
            await semaphore.WaitAsync(ct);
            try   { return (Job: job, ExternalId: externalId, Detail: await FetchDetailAsync(client, detailBaseUrl, externalId, company.CanonicalName, ct)); }
            finally { semaphore.Release(); }
        });

        var newJobResults = await Task.WhenAll(detailTasks);

        foreach (var (job, externalId, detail) in newJobResults)
        {
            if (detail == null) continue;

            var mapped = MapToPosting(job, detail, source.Id, company.Id, company.WorkdayHost!, company.WorkdayCareerSite);
            if (mapped.DescriptionHash != null && removedByHash.TryGetValue(mapped.DescriptionHash, out var prev))
            {
                mapped.PreviousPostingId = prev.Id;
                mapped.RepostCount = (short)(prev.RepostCount + 1);
            }
            db.JobPostings.Add(mapped);
            newCount++;
        }

        foreach (var job in existingJobs)
        {
            var externalId = JobId(job.ExternalPath);
            var existing   = existingMap[externalId];
            var mapped     = MapToPosting(job, null, source.Id, company.Id, company.WorkdayHost!, company.WorkdayCareerSite);
            var changed    = DetectChanges(existing, mapped);
            if (changed.Count > 0)
            {
                db.JobSnapshots.Add(new JobSnapshot
                {
                    JobPostingId  = existing.Id,
                    SnapshotAt    = DateTime.UtcNow,
                    ChangedFields = JsonDocument.Parse(JsonSerializer.Serialize(changed)),
                    RawData = mapped.RawData
                });
                ApplyChanges(existing, mapped);
                updatedCount++;
            }

            existing.LastSeenAt = DateTime.UtcNow;
            existing.IsActive   = true;
            existing.RemovedAt  = null;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Workday [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    // Use the last path segment of externalPath as the stable ID — it contains the requisition number
    // e.g. "/en-US/Search/job/Manufacturing-Production-_R01100869" → "Manufacturing-Production-_R01100869"
    private static string JobId(string? externalPath)
    {
        if (string.IsNullOrEmpty(externalPath)) return string.Empty;
        var lastSlash = externalPath.LastIndexOf('/');
        return lastSlash >= 0 ? externalPath[(lastSlash + 1)..] : externalPath.TrimStart('/');
    }

    private async Task<WorkdayJobDetail?> FetchDetailAsync(
        HttpClient client, string detailBaseUrl, string externalId, string companyName, CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync($"{detailBaseUrl}/{externalId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workday detail API returned {Status} for {ExternalId} ({Company})",
                    (int)response.StatusCode, externalId, companyName);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<WorkdayJobDetail>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Workday detail for {ExternalId} ({Company})", externalId, companyName);
            return null;
        }
        finally
        {
            await Task.Delay(300, ct); // polite delay between detail requests
        }
    }

    private static JobPosting MapToPosting(WorkdayJobPosting job, WorkdayJobDetail? detail, int sourceId, long companyId, string host, string? careerSite)
    {
        var location = job.LocationsText ?? string.Empty;
        var loc = LocationParser.Parse(location);
        var salary = Parse(null);

        var employmentType = job.BulletFields?.FirstOrDefault(b =>
            b.Contains("full", StringComparison.OrdinalIgnoreCase) ||
            b.Contains("part", StringComparison.OrdinalIgnoreCase) ||
            b.Contains("contract", StringComparison.OrdinalIgnoreCase) ||
            b.Contains("intern", StringComparison.OrdinalIgnoreCase))
            ?.ToLowerInvariant() switch
        {
            var t when t != null && t.Contains("full") => "full-time",
            var t when t != null && t.Contains("part") => "part-time",
            var t when t != null && t.Contains("contract") => "contract",
            var t when t != null && t.Contains("intern") => "internship",
            _ => null
        };

        // Some Workday tenants return externalPath as "/job/..." without the career site prefix.
        // In that case we need to inject the career site: "/{careerSite}/job/..."
        var applyUrl = string.IsNullOrEmpty(job.ExternalPath)
            ? null
            : job.ExternalPath.StartsWith("/job/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(careerSite)
                ? $"https://{host}/{careerSite}{job.ExternalPath}"
                : $"https://{host}{job.ExternalPath}";

        var descriptionHtml = detail?.JobPostingInfo?.JobDescription;
        var description = StripHtml(descriptionHtml);
        var descriptionHash = Compute(description);
        var salary2 = Parse(descriptionHtml); // check description for salary disclosure

        return new JobPosting
        {
            ExternalId = JobId(job.ExternalPath),
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Title ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Title),
            LocationRaw = Truncate(location, 500),
            LocationCity = loc.City,
            LocationState = loc.State,
            LocationCountry = loc.Country,
            IsRemote = loc.IsRemote,
            IsHybrid = loc.IsHybrid,
            IsUsPosting = UsLocationClassifier.Classify(Truncate(location, 500)),
            SalaryMin = salary2.Disclosed ? salary2.Min : salary.Min,
            SalaryMax = salary2.Disclosed ? salary2.Max : salary.Max,
            SalaryCurrency = salary2.Disclosed ? salary2.Currency : salary.Currency,
            SalaryPeriod = salary2.Disclosed ? salary2.Period : salary.Period,
            SalaryDisclosed = salary2.Disclosed,
            EmploymentType = employmentType,
            DescriptionHtml = descriptionHtml,
            Description = description,
            DescriptionHash = descriptionHash,
            PostedAt = ParsePostedOn(job.PostedOn),
            ApplyUrl = applyUrl,
            ApplyUrlDomain = host,
            IsActive = true,
            RawData = JsonDocument.Parse(JsonSerializer.Serialize(job))
        };
    }

    // "Posted 3 Days Ago" → subtract days; "Posted 30+ Days Ago" → use 30; "Posted Today" → today
    private static DateTime? ParsePostedOn(string? postedOn)
    {
        if (string.IsNullOrWhiteSpace(postedOn)) return null;
        var lower = postedOn.ToLowerInvariant();
        if (lower.Contains("today")) return DateTime.UtcNow.Date;
        var match = System.Text.RegularExpressions.Regex.Match(lower, @"(\d+)\+?\s+day");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var days))
            return DateTime.UtcNow.Date.AddDays(-days);
        return null;
    }

    private static Dictionary<string, object[]> DetectChanges(JobPosting existing, JobPosting incoming)
    {
        var changes = new Dictionary<string, object[]>();
        if (existing.Title != incoming.Title) changes["title"] = [existing.Title, incoming.Title];
        if (existing.LocationRaw != incoming.LocationRaw) changes["location_raw"] = [existing.LocationRaw ?? "", incoming.LocationRaw ?? ""];
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
        existing.IsUsPosting = incoming.IsUsPosting;
        existing.EmploymentType = incoming.EmploymentType;
        existing.ApplyUrl = incoming.ApplyUrl;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Trim();
    }

    // Workday CXS API models
    private record WorkdayJobsResponse(
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("jobPostings")] List<WorkdayJobPosting>? JobPostings);

    private record WorkdayJobPosting(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("externalPath")] string? ExternalPath,
        [property: JsonPropertyName("locationsText")] string? LocationsText,
        [property: JsonPropertyName("postedOn")] string? PostedOn,
        [property: JsonPropertyName("bulletFields")] List<string>? BulletFields);

    private record WorkdayJobDetail(
        [property: JsonPropertyName("jobPostingInfo")] WorkdayJobPostingInfo? JobPostingInfo);

    private record WorkdayJobPostingInfo(
        [property: JsonPropertyName("jobDescription")] string? JobDescription);
}
