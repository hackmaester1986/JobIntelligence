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

        // Fetch details for new jobs in parallel, update existing jobs sequentially
        var newJobs      = fetched.Where(j => !existingMap.ContainsKey(j.Id ?? string.Empty)).ToList();
        var existingJobs = fetched.Where(j =>  existingMap.ContainsKey(j.Id ?? string.Empty)).ToList();

        var semaphore = new SemaphoreSlim(5);
        var detailTasks = newJobs.Select(async job =>
        {
            await semaphore.WaitAsync(ct);
            try   { return (Job: job, Detail: await FetchDetailAsync(client, job.Ref, job.Id, company.CanonicalName, ct)); }
            finally { semaphore.Release(); }
        });

        var newJobResults = await Task.WhenAll(detailTasks);

        foreach (var (job, detail) in newJobResults)
        {
            if (detail == null) continue;

            var mapped = MapToPosting(job, detail, source.Id, company.Id);
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
            var existing = existingMap[job.Id ?? string.Empty];
            var mapped   = MapToPosting(job, null, source.Id, company.Id);
            var changed  = DetectChanges(existing, mapped);
            if (changed.Count > 0)
            {
                db.JobSnapshots.Add(new JobSnapshot
                {
                    JobPostingId = existing.Id,
                    SnapshotAt   = DateTime.UtcNow,
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

        // Enrich company from API data
        var firstJob = fetched.FirstOrDefault();
        if (firstJob != null)
        {
            var apiName = firstJob.Company?.Name;
            if (!string.IsNullOrEmpty(apiName))
                company.CanonicalName = apiName;

            var apiIndustry = firstJob.Industry?.Label;
            if (!string.IsNullOrEmpty(apiIndustry) && string.IsNullOrEmpty(company.Industry))
                company.Industry = apiIndustry;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("SmartRecruiters [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private async Task<SmartRecruitersJobDetail?> FetchDetailAsync(
        HttpClient client, string? refUrl, string? jobId, string companyName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refUrl))
        {
            logger.LogWarning("SmartRecruiters [{Company}]: no ref URL for job {JobId}", companyName, jobId);
            return null;
        }

        try
        {
            var detail = await client.GetFromJsonAsync<SmartRecruitersJobDetail>(refUrl, ct);
            return detail;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SmartRecruiters [{Company}]: failed to fetch detail for job {JobId}", companyName, jobId);
            return null;
        }
        finally
        {
            await Task.Delay(300, ct);
        }
    }

    private static JobPosting MapToPosting(SmartRecruitersJob job, SmartRecruitersJobDetail? detail, int sourceId, long companyId)
    {
        var fullLocation = job.Location?.FullLocation;
        var city = job.Location?.City ?? string.Empty;
        var country = job.Location?.Country ?? string.Empty;
        var locationText = fullLocation ?? string.Join(", ", new[] { city, country }.Where(s => !string.IsNullOrEmpty(s)));
        var loc = LocationParser.Parse(locationText);

        var isRemote = job.Location?.Remote == true || loc.IsRemote;
        var isHybrid = job.Location?.Hybrid == true || loc.IsHybrid;

        var employmentType = job.TypeOfEmployment?.Label?.ToLowerInvariant() switch
        {
            var t when t != null && t.Contains("full") => "full-time",
            var t when t != null && t.Contains("part") => "part-time",
            var t when t != null && t.Contains("contract") => "contract",
            var t when t != null && t.Contains("intern") => "internship",
            _ => null
        };

        var descriptionHtml = BuildDescriptionHtml(detail);
        var description = StripHtml(descriptionHtml);
        var descriptionHash = Compute(description);
        var salary = Parse(descriptionHtml);

        var applyUrl = detail?.PostingUrl ?? detail?.ApplyUrl ?? BuildApplyUrl(job.Company?.Name ?? companyId.ToString(), job.Id, job.Name);

        return new JobPosting
        {
            ExternalId = job.Id ?? string.Empty,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Name ?? string.Empty,
            SeniorityLevel = job.ExperienceLevel?.Label ?? TitleParser.Parse(job.Name),
            Department = !string.IsNullOrEmpty(job.Department?.Label) ? job.Department.Label : job.Function?.Label,
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
            DescriptionHtml = descriptionHtml,
            Description = description,
            DescriptionHash = descriptionHash,
            ApplyUrl = applyUrl,
            ApplyUrlDomain = "jobs.smartrecruiters.com",
            PostedAt = job.ReleasedDate.HasValue
                ? DateTime.SpecifyKind(job.ReleasedDate.Value, DateTimeKind.Utc)
                : null,
            IsActive = true,
            RawData = JsonDocument.Parse(JsonSerializer.Serialize(job))
        };
    }

    private static string? BuildDescriptionHtml(SmartRecruitersJobDetail? detail)
    {
        var sections = detail?.JobAd?.Sections;
        if (sections == null) return null;

        var parts = new List<string>();
        foreach (var section in new[] { sections.CompanyDescription, sections.JobDescription, sections.Qualifications, sections.AdditionalInformation })
        {
            if (!string.IsNullOrWhiteSpace(section?.Text))
                parts.Add(section.Text);
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Replace("&#xa0;", " ").Trim();
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
        existing.IsUsPosting = incoming.IsUsPosting;
        existing.Department = incoming.Department;
        existing.EmploymentType = incoming.EmploymentType;
        existing.ApplyUrl = incoming.ApplyUrl;
        existing.ApplyUrlDomain = incoming.ApplyUrlDomain;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string BuildApplyUrl(string companySlug, string? jobId, string? jobTitle)
    {
        var titleSlug = string.IsNullOrEmpty(jobTitle)
            ? jobId ?? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(jobTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"https://jobs.smartrecruiters.com/{companySlug}/{jobId}-{titleSlug}";
    }

    private record SmartRecruitersResponse(
        [property: JsonPropertyName("content")] List<SmartRecruitersJob>? Content,
        [property: JsonPropertyName("totalFound")] int TotalFound);

    private record SmartRecruitersJob(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("company")] SmartRecruitersCompany? Company,
        [property: JsonPropertyName("department")] SmartRecruitersLabel? Department,
        [property: JsonPropertyName("function")] SmartRecruitersLabel? Function,
        [property: JsonPropertyName("industry")] SmartRecruitersLabel? Industry,
        [property: JsonPropertyName("location")] SmartRecruitersLocation? Location,
        [property: JsonPropertyName("typeOfEmployment")] SmartRecruitersLabel? TypeOfEmployment,
        [property: JsonPropertyName("experienceLevel")] SmartRecruitersLabel? ExperienceLevel,
        [property: JsonPropertyName("ref")] string? Ref,
        [property: JsonPropertyName("releasedDate")] DateTime? ReleasedDate);

    private record SmartRecruitersJobDetail(
        [property: JsonPropertyName("postingUrl")] string? PostingUrl,
        [property: JsonPropertyName("applyUrl")] string? ApplyUrl,
        [property: JsonPropertyName("jobAd")] SmartRecruitersJobAd? JobAd);

    private record SmartRecruitersJobAd(
        [property: JsonPropertyName("sections")] SmartRecruitersJobAdSections? Sections);

    private record SmartRecruitersJobAdSections(
        [property: JsonPropertyName("companyDescription")] SmartRecruitersSection? CompanyDescription,
        [property: JsonPropertyName("jobDescription")] SmartRecruitersSection? JobDescription,
        [property: JsonPropertyName("qualifications")] SmartRecruitersSection? Qualifications,
        [property: JsonPropertyName("additionalInformation")] SmartRecruitersSection? AdditionalInformation);

    private record SmartRecruitersSection(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("text")] string? Text);

    private record SmartRecruitersCompany(
        [property: JsonPropertyName("name")] string? Name);

    private record SmartRecruitersLabel(
        [property: JsonPropertyName("label")] string? Label);

    private record SmartRecruitersLocation(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("fullLocation")] string? FullLocation,
        [property: JsonPropertyName("remote")] bool? Remote,
        [property: JsonPropertyName("hybrid")] bool? Hybrid);
}
