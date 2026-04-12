using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public partial class SizeEnrichmentService(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    IConfiguration configuration,
    ILogger<SizeEnrichmentService> logger) : ISizeEnrichmentService
{
    // Maps the lower bound of a LinkedIn employee range to our canonical range
    private static readonly (int MaxLower, string Range)[] RangeMap =
    [
        (50,    "1-50"),
        (200,   "50-200"),
        (500,   "200-500"),
        (1000,  "500-1000"),
        (5000,  "1000-5000"),
        (10000, "5000-10000"),
    ];

    public async Task<SizeEnrichmentResult> EnrichAsync(int batchSize = 50, CancellationToken ct = default)
    {
        var braveApiKey = configuration["BraveSearch:ApiKey"];
        if (string.IsNullOrEmpty(braveApiKey))
        {
            logger.LogError("BraveSearch:ApiKey is not configured — cannot run size enrichment");
            return new SizeEnrichmentResult(0, 0, 0, 0);
        }

        var companies = await db.Companies
            .Where(c => c.SizeEnrichedAt == null && c.EmployeeCountRange == null && c.IsTechHiring != false)
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        logger.LogInformation("Size enrichment: processing {Count} companies", companies.Count);

        int processed = 0, enriched = 0, notFound = 0, failed = 0;
        var client = httpClientFactory.CreateClient("BraveSearch");

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Skip slug-derived names — searching for "janestreetevents" wastes quota
                if (IsSlugDerivedName(company.CanonicalName))
                {
                    logger.LogDebug("Skipping slug-derived name {Name}", company.CanonicalName);
                    company.SizeEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    notFound++;
                    processed++;
                    continue;
                }

                var range = await TryBraveSearchAsync(client, company.CanonicalName, braveApiKey, ct);

                if (range != null)
                {
                    company.EmployeeCountRange = range;
                    company.UpdatedAt = DateTime.UtcNow;
                    enriched++;
                    logger.LogInformation("Size enriched {Name} → {Range}", company.CanonicalName, range);
                }
                else
                {
                    notFound++;
                    logger.LogDebug("No size found for {Name}", company.CanonicalName);
                }

                company.SizeEnrichedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                processed++;
                await Task.Delay(300, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enrich size for {Name}", company.CanonicalName);
                failed++;
                try
                {
                    company.SizeEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx, "SaveChangesAsync failed for {Name}", company.CanonicalName);
                }
            }
        }

        logger.LogInformation(
            "Size enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
            processed, enriched, notFound, failed);

        return new SizeEnrichmentResult(processed, enriched, notFound, failed);
    }

    private async Task<string?> TryBraveSearchAsync(
        HttpClient client, string companyName, string apiKey, CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"\"{companyName}\" number of employees");
        var url = $"https://api.search.brave.com/res/v1/web/search?q={query}&count=10";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", apiKey);
        request.Headers.Add("Accept", "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Brave Search request failed for {Name}", companyName);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Brave Search returned {Status} for {Name}", (int)response.StatusCode, companyName);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<BraveSearchResponse>(json);

        foreach (var hit in result?.Web?.Results ?? [])
        {
            var text = $"{hit.Title} {hit.Description}";
            var range = ParseEmployeeRange(text);
            if (range != null) return range;
        }

        return null;
    }

// Parses employee count from snippet text in various formats:
    //   "1,001-5,000 employees"  → average (3,000) → bucket
    //   "10,001+ employees"      → lower bound → bucket
    //   "5,234 employees"        → direct → bucket
    //   "5.2K employees"         → 5200 → bucket
    private static string? ParseEmployeeRange(string text)
    {
        // Range: "1,001-5,000 employees" or "51-200 employees"
        var rangeMatch = EmployeeRangePattern().Match(text);
        if (rangeMatch.Success)
        {
            var lower = ParseInt(rangeMatch.Groups[1].Value);
            var upper = ParseInt(rangeMatch.Groups[2].Value);
            var avg   = (lower + upper) / 2;
            return MapToRange(avg);
        }

        // Plus: "10,001+ employees"
        var plusMatch = EmployeePlusPattern().Match(text);
        if (plusMatch.Success)
        {
            var lower = ParseInt(plusMatch.Groups[1].Value);
            return MapToRange(lower);
        }

        // K/M suffix: "5.2K employees" / "1.3M employees"
        var kmMatch = EmployeeKMPattern().Match(text);
        if (kmMatch.Success)
        {
            var num        = double.Parse(kmMatch.Groups[1].Value);
            var multiplier = kmMatch.Groups[2].Value.ToUpperInvariant() == "M" ? 1_000_000 : 1_000;
            return MapToRange((int)(num * multiplier));
        }

        // Single number: "5,234 employees"
        var singleMatch = EmployeeSinglePattern().Match(text);
        if (singleMatch.Success)
        {
            var n = ParseInt(singleMatch.Groups[1].Value);
            if (n > 0) return MapToRange(n);
        }

        return null;
    }

    private static string? MapToRange(int n)
    {
        foreach (var (maxLower, range) in RangeMap)
            if (n <= maxLower) return range;
        return "10000+";
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Replace(",", ""), out var n) ? n : 0;

    private static bool IsSlugDerivedName(string name)
    {
        if (name.Contains(' ')) return false;          // has spaces → real name
        if (name.Contains('-') || name.Contains('_')) return true;  // hyphen/underscore slug

        var lower = name.ToLowerInvariant();
        if (name == lower) return true;                // all lowercase → slug

        // Single-word title-cased: skip if it looks like a concatenated slug
        // (no vowels, or more than 12 chars with no recognizable structure)
        var vowels = lower.Count(c => "aeiou".Contains(c));
        if (vowels == 0) return true;                  // no vowels → definitely a slug (e.g. Ntx, Evgsn)
        if (name.Length > 12 && !name.Any(char.IsUpper)) return true; // long lowercase blob

        return false;
    }

    private static bool IsValidRange(string? s) =>
        s is "1-50" or "50-200" or "200-500" or "500-1000" or "1000-5000" or "5000-10000" or "10000+";

    [GeneratedRegex(@"([\d,]+)\s*[-–]\s*([\d,]+)\s*employees", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeeRangePattern();

    [GeneratedRegex(@"([\d,]+)\+\s*employees", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeePlusPattern();

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*([KkMm])\s*employees", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeeKMPattern();

    [GeneratedRegex(@"([\d,]+)\s*employees", RegexOptions.IgnoreCase)]
    private static partial Regex EmployeeSinglePattern();

    private record BraveSearchResponse(
        [property: JsonPropertyName("web")] BraveWebResults? Web);

    private record BraveWebResults(
        [property: JsonPropertyName("results")] List<BraveResult>? Results);

    private record BraveResult(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("description")] string Description);
}
