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

public class LeverCollector(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<LeverCollector> logger) : IJobCollector
{
    public string SourceName => "lever";

    public async Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(company.LeverCompanySlug))
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "No Lever company slug");

        var source = await db.JobSources.FirstAsync(s => s.Name == SourceName, ct);
        var client = httpClientFactory.CreateClient("Lever");

        List<LeverPosting>? postings;
        try
        {
            postings = await client.GetFromJsonAsync<List<LeverPosting>>(
                $"v0/postings/{company.LeverCompanySlug}?mode=json", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Lever board not found for {Company} (slug: {Slug}) — clearing slug",
                company.CanonicalName, company.LeverCompanySlug);
            company.LeverCompanySlug = null;
            await db.SaveChangesAsync(ct);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, "Board not found (404) — slug cleared");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch Lever postings for {Company}", company.CanonicalName);
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0, ex.Message);
        }

        if (postings == null || postings.Count == 0)
            return new CollectionResult(company.CanonicalName, 0, 0, 0, 0);

        int newCount = 0, updatedCount = 0, removedCount = 0;
        var fetchedIds = postings.Select(p => p.Id).ToHashSet();

        // Batch-load all existing postings for this company+source in one query
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

        // Mark removed postings
        foreach (var existing in existingMap.Values.Where(e => e.IsActive && !fetchedIds.Contains(e.ExternalId)))
        {
            existing.IsActive = false;
            existing.RemovedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            removedCount++;
        }

        // Upsert
        foreach (var posting in postings)
        {
            existingMap.TryGetValue(posting.Id ?? string.Empty, out var existing);

            var mapped = MapToPosting(posting, source.Id, company.Id);

            if (existing == null)
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
        logger.LogInformation("Lever [{Company}]: fetched={F} new={N} updated={U} removed={R}",
            company.CanonicalName, postings.Count, newCount, updatedCount, removedCount);

        return new CollectionResult(company.CanonicalName, postings.Count, newCount, updatedCount, removedCount);
    }

    private static JobPosting MapToPosting(LeverPosting posting, int sourceId, long companyId)
    {
        var locationText = posting.Categories?.Location ?? string.Empty;
        var commitment = posting.Categories?.Commitment ?? string.Empty;
        var loc = LocationParser.Parse(locationText);
        var descriptionHtml = BuildDescriptionHtml(posting);
        var salary = Parse(descriptionHtml);
        var isRemote = loc.IsRemote || commitment.Contains("remote", StringComparison.OrdinalIgnoreCase);
        var isHybrid = loc.IsHybrid || commitment.Contains("hybrid", StringComparison.OrdinalIgnoreCase);

        var employmentType = commitment.ToLowerInvariant() switch
        {
            var c when c.Contains("full") => "full-time",
            var c when c.Contains("part") => "part-time",
            var c when c.Contains("contract") => "contract",
            var c when c.Contains("intern") => "internship",
            _ => null
        };

        var postedAt = posting.CreatedAt.HasValue
            ? DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(posting.CreatedAt.Value).UtcDateTime, DateTimeKind.Utc)
            : (DateTime?)null;

        return new JobPosting
        {
            ExternalId = posting.Id ?? string.Empty,
            SourceId = sourceId,
            CompanyId = companyId,
            Title = posting.Text ?? string.Empty,
            SeniorityLevel = TitleParser.Parse(posting.Text),
            Department = posting.Categories?.Department,
            Team = posting.Categories?.Team,
            LocationRaw = locationText,
            LocationCity = loc.City,
            LocationState = loc.State,
            LocationCountry = loc.Country,
            IsRemote = isRemote,
            IsHybrid = isHybrid,
            IsUsPosting = UsLocationClassifier.Classify(locationText),
            SalaryMin = salary.Min,
            SalaryMax = salary.Max,
            SalaryCurrency = salary.Currency,
            SalaryPeriod = salary.Period,
            SalaryDisclosed = salary.Disclosed,
            EmploymentType = employmentType,
            DescriptionHtml = descriptionHtml,
            Description = StripHtml(descriptionHtml),
            IsRemoteInDescription = LocationParser.HasRemoteInDescription(StripHtml(descriptionHtml)),
            DescriptionHash = Compute(StripHtml(descriptionHtml)),
            ApplyUrl = posting.ApplyUrl,
            ApplyUrlDomain = ExtractDomain(posting.ApplyUrl),
            PostedAt = postedAt,
            IsActive = true,
            RawData = JsonDocument.Parse(JsonSerializer.Serialize(posting))
        };
    }

    private static Dictionary<string, object[]> DetectChanges(JobPosting existing, JobPosting incoming)
    {
        var changes = new Dictionary<string, object[]>();
        if (existing.Title != incoming.Title) changes["title"] = [existing.Title, incoming.Title];
        if (existing.LocationRaw != incoming.LocationRaw) changes["location_raw"] = [existing.LocationRaw ?? "", incoming.LocationRaw ?? ""];
        if (existing.Department != incoming.Department) changes["department"] = [existing.Department ?? "", incoming.Department ?? ""];
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

    private static string? BuildDescriptionHtml(LeverPosting posting)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(posting.Description))
            parts.Add(posting.Description);
        if (!string.IsNullOrWhiteSpace(posting.DescriptionBody))
            parts.Add(posting.DescriptionBody);
        if (posting.Lists != null)
        {
            foreach (var section in posting.Lists)
            {
                if (!string.IsNullOrWhiteSpace(section.Content))
                    parts.Add(string.IsNullOrWhiteSpace(section.Text)
                        ? section.Content
                        : $"<p><strong>{section.Text}</strong></p>{section.Content}");
            }
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

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

    // Lever API models
    private record LeverPosting(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("descriptionBody")] string? DescriptionBody,
        [property: JsonPropertyName("lists")] List<LeverSection>? Lists,
        [property: JsonPropertyName("applyUrl")] string? ApplyUrl,
        [property: JsonPropertyName("categories")] LeverCategories? Categories,
        [property: JsonPropertyName("createdAt")] long? CreatedAt
    );

    private record LeverSection(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("content")] string? Content
    );

    private record LeverCategories(
        [property: JsonPropertyName("commitment")] string? Commitment,
        [property: JsonPropertyName("department")] string? Department,
        [property: JsonPropertyName("location")] string? Location,
        [property: JsonPropertyName("team")] string? Team
    );
}
