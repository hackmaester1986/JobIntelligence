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

public class RecruiteeCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<RecruiteeCollector> logger) : IJobCollector
{
    public string SourceName => "recruitee";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.RecruiteeSlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Recruitee slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Recruitee");
        var apiUrl = $"https://{company.RecruiteeSlug}.recruitee.com/api/offers/";

        List<RecruiteeOffer> fetched;
        try
        {
            var response = await client.GetFromJsonAsync<RecruiteeResponse>(apiUrl, ct);
            fetched = response?.Offers?.Where(o => o.Status == "published").ToList() ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Recruitee board not found for {Company} (slug: {Slug}) — clearing slug",
                company.CanonicalName, company.RecruiteeSlug);
            company.RecruiteeSlug = null;
            await db.SaveChangesAsync(ct);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "Board not found (404) — slug cleared");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Recruitee jobs for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = fetched.Select(j => j.Id.ToString()).ToHashSet();

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
            var externalId = job.Id.ToString();
            var mapped = MapToPosting(job, source.Id, company.Id, company.RecruiteeSlug!);

            if (!existingMap.TryGetValue(externalId, out var existing))
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
        logger.LogInformation("Recruitee [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, fetched.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(RecruiteeOffer job, int sourceId, long companyId, string slug)
    {
        var loc = job.Locations?.FirstOrDefault();
        var city = loc?.City;
        var countryCode = loc?.CountryCode;
        var stateCode = loc?.StateCode;
        var locationRaw = job.Location ?? string.Empty;

        var isRemote = job.Remote == true;
        var isHybrid = job.Hybrid == true;

        var descriptionHtml = string.Join("", new[]
        {
            job.Description,
            job.Requirements
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var descriptionText = StripHtml(descriptionHtml);
        var salaryFromApi = job.Salary;
        SalaryParser.ParsedSalary salary;
        if (salaryFromApi?.Min != null || salaryFromApi?.Max != null)
        {
            salary = new SalaryParser.ParsedSalary(
                salaryFromApi.Min, salaryFromApi.Max,
                salaryFromApi.Currency, salaryFromApi.Period,
                salaryFromApi.Min != null || salaryFromApi.Max != null);
        }
        else
        {
            salary = Parse(descriptionHtml);
        }

        var employmentType = job.EmploymentTypeCode?.ToLowerInvariant() switch
        {
            "fulltime_permanent" or "fulltime" => "Full-time",
            "parttime_permanent" or "parttime" => "Part-time",
            "temporary" or "contract" => "Contract",
            "internship" or "intern" => "Internship",
            _ => null
        };

        var usState = ResolveUsState(stateCode, loc?.State);

        return new JobPosting
        {
            ExternalId = job.Id.ToString(),
            SourceId = sourceId,
            CompanyId = companyId,
            Title = job.Title ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(job.Title),
            Department = job.Department,
            LocationRaw = Truncate(locationRaw, 500),
            LocationCity = city,
            LocationState = usState,
            LocationCountry = countryCode,
            IsRemote = isRemote,
            IsHybrid = isHybrid,
            IsUsPosting = UsLocationClassifier.Classify(locationRaw),
            Description = descriptionText,
            DescriptionHtml = descriptionHtml.Length > 0 ? descriptionHtml : null,
            DescriptionHash = Compute(descriptionText),
            IsRemoteInDescription = LocationParser.HasRemoteInDescription(descriptionText),
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            ApplyUrl = job.CareersApplyUrl,
            ApplyUrlDomain = $"{slug}.recruitee.com",
            PostedAt = DateTime.TryParse(job.PublishedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var publishedAt)
                ? publishedAt
                : null,
            IsActive = true,
            RawData = JsonDocument.Parse(JsonSerializer.Serialize(job))
        };
    }

    // Recruitee returns US state as a full name in loc.State; stateCode is a numeric FIPS code, not useful.
    private static string? ResolveUsState(string? stateCode, string? stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return null;
        return StateNameToAbbrev(stateName) ?? (stateName.Length == 2 ? stateName.ToUpperInvariant() : null);
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

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? StateNameToAbbrev(string name) => name.Trim().ToUpperInvariant() switch
    {
        "ALABAMA" => "AL", "ALASKA" => "AK", "ARIZONA" => "AZ", "ARKANSAS" => "AR",
        "CALIFORNIA" => "CA", "COLORADO" => "CO", "CONNECTICUT" => "CT", "DELAWARE" => "DE",
        "FLORIDA" => "FL", "GEORGIA" => "GA", "HAWAII" => "HI", "IDAHO" => "ID",
        "ILLINOIS" => "IL", "INDIANA" => "IN", "IOWA" => "IA", "KANSAS" => "KS",
        "KENTUCKY" => "KY", "LOUISIANA" => "LA", "MAINE" => "ME", "MARYLAND" => "MD",
        "MASSACHUSETTS" => "MA", "MICHIGAN" => "MI", "MINNESOTA" => "MN", "MISSISSIPPI" => "MS",
        "MISSOURI" => "MO", "MONTANA" => "MT", "NEBRASKA" => "NE", "NEVADA" => "NV",
        "NEW HAMPSHIRE" => "NH", "NEW JERSEY" => "NJ", "NEW MEXICO" => "NM", "NEW YORK" => "NY",
        "NORTH CAROLINA" => "NC", "NORTH DAKOTA" => "ND", "OHIO" => "OH", "OKLAHOMA" => "OK",
        "OREGON" => "OR", "PENNSYLVANIA" => "PA", "RHODE ISLAND" => "RI", "SOUTH CAROLINA" => "SC",
        "SOUTH DAKOTA" => "SD", "TENNESSEE" => "TN", "TEXAS" => "TX", "UTAH" => "UT",
        "VERMONT" => "VT", "VIRGINIA" => "VA", "WASHINGTON" => "WA", "WEST VIRGINIA" => "WV",
        "WISCONSIN" => "WI", "WYOMING" => "WY", "DISTRICT OF COLUMBIA" => "DC",
        _ => null
    };

    private record RecruiteeResponse(
        [property: JsonPropertyName("offers")] List<RecruiteeOffer>? Offers);

    private record RecruiteeOffer(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("department")] string? Department,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("locations")] List<RecruiteeLocation>? Locations,
        [property: JsonPropertyName("remote")] bool? Remote,
        [property: JsonPropertyName("hybrid")] bool? Hybrid,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("requirements")] string? Requirements,
        [property: JsonPropertyName("salary")] RecruiteeSalary? Salary,
        [property: JsonPropertyName("careers_url")] string? CareersUrl,
        [property: JsonPropertyName("careers_apply_url")] string? CareersApplyUrl,
        [property: JsonPropertyName("published_at")] string? PublishedAt,
        [property: JsonPropertyName("updated_at")] string? UpdatedAt,
        [property: JsonPropertyName("employment_type_code")] string? EmploymentTypeCode,
        [property: JsonPropertyName("country_code")] string? CountryCode);

    private record RecruiteeLocation(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("state_code")] string? StateCode);

    private record RecruiteeSalary(
        [property: JsonPropertyName("min")] decimal? Min,
        [property: JsonPropertyName("max")] decimal? Max,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("period")] string? Period);
}
