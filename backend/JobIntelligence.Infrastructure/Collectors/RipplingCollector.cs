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

public class RipplingCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<RipplingCollector> logger) : IJobCollector
{
    public string SourceName => "rippling";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.RipplingSlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Rippling slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Rippling");
        var listUrl = $"platform/api/ats/v1/board/{company.RipplingSlug}/jobs";

        List<RipplingListJob> listed;
        try
        {
            var items = await client.GetFromJsonAsync<List<RipplingListJob>>(listUrl, ct);
            listed = items ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Rippling board not found for {Company} (slug: {Slug}) — clearing slug",
                company.CanonicalName, company.RipplingSlug);
            company.RipplingSlug = null;
            await db.SaveChangesAsync(ct);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "Board not found (404) — slug cleared");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Rippling jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        // The list endpoint repeats the same UUID per location — deduplicate, keeping all locations per job
        var jobsByUuid = listed
            .Where(j => !string.IsNullOrEmpty(j.Uuid))
            .GroupBy(j => j.Uuid!)
            .ToDictionary(g => g.Key, g => g.ToList());

        int newCount = 0, updatedCount = 0, removedCount = 0;
        string? resolvedCompanyName = null;
        var fetchedUuids = jobsByUuid.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        foreach (var existing in existingMap.Values.Where(e => e.IsActive && !fetchedUuids.Contains(e.ExternalId)))
        {
            existing.IsActive = false;
            existing.RemovedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            removedCount++;
        }

        foreach (var (uuid, listEntries) in jobsByUuid)
        {
            RipplingJobDetail? detail = null;
            try
            {
                detail = await client.GetFromJsonAsync<RipplingJobDetail>(
                    $"platform/api/ats/v1/board/{company.RipplingSlug}/jobs/{uuid}", ct);
                await Task.Delay(120, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Rippling [{Company}]: failed to fetch detail for {Uuid}", company.CanonicalName, uuid);
            }

            resolvedCompanyName ??= detail?.CompanyName;
            var primary = listEntries[0];
            var workLocations = detail?.WorkLocations ?? primary.WorkLocations ?? [];
            var mapped = MapToPosting(uuid, primary, detail, workLocations, source.Id, company.Id, company.RipplingSlug!);

            if (!existingMap.TryGetValue(uuid, out var existing))
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

        if (!string.IsNullOrWhiteSpace(resolvedCompanyName)
            && string.Equals(company.CanonicalName, company.RipplingSlug, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Rippling [{Slug}]: updating canonical name to {Name}", company.RipplingSlug, resolvedCompanyName);
            company.CanonicalName = resolvedCompanyName;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Rippling [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, jobsByUuid.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, jobsByUuid.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(
        string uuid, RipplingListJob primary, RipplingJobDetail? detail,
        List<string> workLocationStrings, int sourceId, long companyId, string slug)
    {
        var isRemote = workLocationStrings.Any(l => l.StartsWith("Remote", StringComparison.OrdinalIgnoreCase))
                       || primary.IsRemote == true;

        var locationRaw = workLocationStrings.Count > 0
            ? string.Join(", ", workLocationStrings.Distinct())
            : isRemote ? "Remote" : "";

        // Parse city/state/country from the first non-remote location string e.g. "San Francisco, CA"
        string? city = null, state = null, country = null;
        var firstOnsite = workLocationStrings.FirstOrDefault(l => !l.StartsWith("Remote", StringComparison.OrdinalIgnoreCase));
        if (firstOnsite != null)
        {
            var parts = firstOnsite.Split(',', StringSplitOptions.TrimEntries);
            city = parts.Length > 0 ? parts[0] : null;
            state = parts.Length > 1 ? NormalizeState(parts[1]) : null;
            country = parts.Length > 2 ? NormalizeCountryCode(parts[2]) : null;
        }

        var descriptionHtml = ExtractDescriptionHtml(detail?.Description);
        var descriptionText = StripHtml(descriptionHtml);

        ParsedSalary salary;
        var salaryFromApi = detail?.PayRangeDetails?.FirstOrDefault();
        if (salaryFromApi?.RangeStart != null || salaryFromApi?.RangeEnd != null)
        {
            salary = new ParsedSalary(
                salaryFromApi.RangeStart, salaryFromApi.RangeEnd,
                salaryFromApi.Currency, MapFrequency(salaryFromApi.Frequency),
                true);
        }
        else
        {
            salary = Parse(descriptionText ?? "");
        }

        var rawEmploymentType = ExtractEmploymentTypeLabel(detail?.EmploymentType ?? primary.EmploymentType);
        var employmentType = rawEmploymentType?.ToLowerInvariant() switch
        {
            "full_time" or "fulltime" or "full-time" or "salaried_ft" => "Full-time",
            "part_time" or "parttime" or "part-time" or "salaried_pt" => "Part-time",
            "contract" or "contractor" => "Contract",
            "internship" or "intern" => "Internship",
            _ => null
        };

        var applyUrl = $"https://ats.rippling.com/{slug}/jobs/{uuid}";

        var title = detail?.Name ?? detail?.Title ?? primary.Name ?? primary.Title ?? string.Empty;

        return new JobPosting
        {
            ExternalId = uuid,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = title,
            SeniorityLevel = TitleParser.Parse(title),
            Department = detail?.Department?.Name ?? primary.Department?.Name,
            LocationRaw = Truncate(locationRaw, 500),
            LocationCity = city,
            LocationState = state,
            LocationCountry = country,
            IsRemote = isRemote,
            IsHybrid = false,
            IsUsPosting = UsLocationClassifier.Classify(locationRaw),
            Description = descriptionText,
            DescriptionHtml = descriptionHtml,
            DescriptionHash = Compute(descriptionText ?? ""),
            IsRemoteInDescription = LocationParser.HasRemoteInDescription(descriptionText ?? ""),
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            ApplyUrl = applyUrl,
            ApplyUrlDomain = "ats.rippling.com",
            PostedAt = ParseDate(detail?.CreatedOn ?? detail?.PostedAt ?? primary.PostedAt),
            IsActive = true,
            RawData = detail != null
                ? JsonDocument.Parse(JsonSerializer.Serialize(detail))
                : JsonDocument.Parse(JsonSerializer.Serialize(primary))
        };
    }

    private static string? NormalizeState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        return state.Length == 2 ? state.ToUpperInvariant() : null;
    }

    private static string? NormalizeCountryCode(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        return country.Length == 2 ? country.ToUpperInvariant() : null;
    }

    private static string? MapFrequency(string? frequency) => frequency?.ToUpperInvariant() switch
    {
        "YEARLY" or "ANNUAL" => "year",
        "MONTHLY" => "month",
        "WEEKLY" => "week",
        "HOURLY" => "hour",
        _ => null
    };

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTime.TryParse(value, null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    private static Dictionary<string, object[]> DetectChanges(JobPosting existing, JobPosting incoming)
    {
        var changes = new Dictionary<string, object[]>();
        if (existing.Title != incoming.Title) changes["title"] = [existing.Title, incoming.Title];
        if (existing.LocationRaw != incoming.LocationRaw) changes["location_raw"] = [existing.LocationRaw ?? "", incoming.LocationRaw ?? ""];
        if (existing.Department != incoming.Department) changes["department"] = [existing.Department ?? "", incoming.Department ?? ""];
        if (existing.IsRemote != incoming.IsRemote) changes["is_remote"] = [existing.IsRemote, incoming.IsRemote];
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
        existing.DescriptionHtml = incoming.DescriptionHtml;
        existing.Description = incoming.Description;
        existing.IsRemoteInDescription = incoming.IsRemoteInDescription;
        existing.DescriptionHash = incoming.DescriptionHash;
        existing.ApplyUrl = incoming.ApplyUrl;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static string? ExtractDescriptionHtml(JsonElement? element)
    {
        if (element is not { } el) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind != JsonValueKind.Object) return null;

        // Rippling description is { "company": "<html>", "role": "<html>", ... }
        // Concatenate all string property values in order
        var parts = new List<string>();
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var val = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(val)) parts.Add(val);
            }
        }
        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static string? ExtractEmploymentTypeLabel(JsonElement? element)
    {
        if (element is not { } el) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind == JsonValueKind.Object)
        {
            // { "label": "SALARIED_FT", "id": "Salaried, full-time" }
            if (el.TryGetProperty("label", out var label)) return label.GetString();
            if (el.TryGetProperty("id", out var id)) return id.GetString();
        }
        return null;
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    // DTOs — workLocations is string[] in both list and detail responses
    private record RipplingListJob(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("department")] RipplingDepartment? Department,
        [property: JsonPropertyName("workLocations")] List<string>? WorkLocations,
        [property: JsonPropertyName("employmentType")] JsonElement? EmploymentType,
        [property: JsonPropertyName("isRemote")] bool? IsRemote,
        [property: JsonPropertyName("postedAt")] string? PostedAt);

    private record RipplingJobDetail(
        [property: JsonPropertyName("uuid")] string? Uuid,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("companyName")] string? CompanyName,
        [property: JsonPropertyName("department")] RipplingDepartment? Department,
        [property: JsonPropertyName("workLocations")] List<string>? WorkLocations,
        [property: JsonPropertyName("employmentType")] JsonElement? EmploymentType,
        [property: JsonPropertyName("isRemote")] bool? IsRemote,
        // description is { "company": "<html>", "role": "<html>", ... }
        [property: JsonPropertyName("description")] JsonElement? Description,
        [property: JsonPropertyName("payRangeDetails")] List<RipplingSalary>? PayRangeDetails,
        [property: JsonPropertyName("createdOn")] string? CreatedOn,
        [property: JsonPropertyName("postedAt")] string? PostedAt);

    private record RipplingDepartment(
        [property: JsonPropertyName("name")] string? Name);

    private record RipplingSalary(
        [property: JsonPropertyName("rangeStart")] decimal? RangeStart,
        [property: JsonPropertyName("rangeEnd")] decimal? RangeEnd,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("frequency")] string? Frequency);
}
