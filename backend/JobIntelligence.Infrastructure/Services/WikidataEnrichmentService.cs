using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class WikidataEnrichmentService(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<WikidataEnrichmentService> logger) : IWikidataEnrichmentService
{
    private const string SearchApiUrl = "https://www.wikidata.org/w/api.php";
    private const string SparqlUrl = "https://query.wikidata.org/sparql";

    // Legal suffixes to strip when searching (improves match rate)
    private static readonly string[] LegalSuffixes =
        [" inc", " inc.", " llc", " llc.", " corp", " corp.", " ltd", " ltd.",
         " limited", " gmbh", " co.", " co", " plc", " sa", " ag", " bv", " nv"];

    // Words in Wikidata descriptions that indicate a non-company result
    private static readonly string[] NonCompanyKeywords =
        ["born", "politician", "actor", "actress", "singer", "musician",
         "athlete", "author", "writer", "director", "painter", "city",
         "municipality", "village", "river", "mountain", "album", "film"];

    public async Task<WikidataEnrichmentResult> EnrichCompaniesAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var http = httpClientFactory.CreateClient("Wikidata");

        int totalProcessed = 0, enriched = 0, notFound = 0, failed = 0;

        while (!ct.IsCancellationRequested)
        {
            var companies = await db.Companies
                .Where(c => c.WikidataEnrichedAt == null)
                .OrderBy(c => c.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (companies.Count == 0) break;

            logger.LogInformation("Wikidata enrichment: processing batch of {Count} (total so far: {Total})",
                companies.Count, totalProcessed);

            foreach (var company in companies)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var qid = await FindWikidataIdAsync(http, company, ct);

                    if (qid != null)
                    {
                        company.WikidataId = qid; // set before SPARQL so it's saved even if enrichment fails
                        await EnrichFromWikidataAsync(http, company, qid, ct);
                        enriched++;
                        logger.LogInformation("Enriched {Name} ({QId})", company.CanonicalName, qid);
                    }
                    else
                    {
                        notFound++;
                        logger.LogDebug("No Wikidata match for {Name}", company.CanonicalName);
                    }

                    company.WikidataEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    await Task.Delay(3000, ct); // 2 requests per company at 3s = ~0.67 req/sec
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to enrich {Name}", company.CanonicalName);
                    failed++;
                    try
                    {
                        company.WikidataEnrichedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                    catch (Exception saveEx)
                    {
                        logger.LogError(saveEx, "SaveChangesAsync failed for {Name} — migration may not be applied", company.CanonicalName);
                    }
                }
            }

            totalProcessed += companies.Count;
        }

        logger.LogInformation(
            "Wikidata enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
            totalProcessed, enriched, notFound, failed);

        return new WikidataEnrichmentResult(totalProcessed, enriched, notFound, failed);
    }

    private async Task<string?> FindWikidataIdAsync(HttpClient http, Company company, CancellationToken ct)
    {
        var searchName = StripLegalSuffixes(company.CanonicalName);
        var url = $"{SearchApiUrl}?action=wbsearchentities&search={Uri.EscapeDataString(searchName)}&language=en&type=item&format=json&limit=5";

        WikidataSearchResponse? response;
        try
        {
            var httpResponse = await GetWithRetryAsync(http, url, ct);
            response = await httpResponse.Content.ReadFromJsonAsync<WikidataSearchResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Wikidata search failed for {Name}", company.CanonicalName);
            return null;
        }

        if (response?.Search == null || response.Search.Count == 0)
            return null;

        var normalizedSearchName = searchName.ToLowerInvariant().Trim();

        foreach (var result in response.Search)
        {
            if (!IsLikelyCompany(result)) continue;

            var resultLabel = (result.Label ?? "").ToLowerInvariant().Trim();
            var resultStripped = StripLegalSuffixes(resultLabel);

            if (resultStripped == normalizedSearchName ||
                resultStripped.Contains(normalizedSearchName) ||
                normalizedSearchName.Contains(resultStripped))
            {
                logger.LogDebug("Matched {Name} → {QId} ({Label})", company.CanonicalName, result.Id, result.Label);
                return result.Id;
            }
        }

        return null;
    }

    private async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
    {
        int[] retryDelaysMs = [10_000, 30_000, 60_000];

        for (int attempt = 0; attempt <= retryDelaysMs.Length; attempt++)
        {
            var response = await http.GetAsync(url, ct);

            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                return response;

            if (attempt >= retryDelaysMs.Length)
                return response; // give up after final retry

            var delay = retryDelaysMs[attempt];
            var retryAfterHeader = response.Headers.RetryAfter;
            if (retryAfterHeader?.Delta is { } delta)
                delay = Math.Max(delay, (int)delta.TotalMilliseconds + 1_000);
            else if (retryAfterHeader?.Date is { } date)
                delay = Math.Max(delay, (int)(date - DateTimeOffset.UtcNow).TotalMilliseconds + 1_000);

            logger.LogWarning("Wikidata rate limited (429), waiting {Delay}s before retry {Attempt}",
                delay / 1000, attempt + 1);
            await Task.Delay(delay, ct);
        }

        throw new InvalidOperationException("Unreachable");
    }

    private async Task EnrichFromWikidataAsync(HttpClient http, Company company, string qid, CancellationToken ct)
    {
        // Use separate subqueries for multi-valued properties (HQ) to avoid Cartesian products
        // that cause LIMIT 1 to miss scalar fields like employees and inception.
        var query = $$"""
            SELECT ?industryLabel ?employees ?inception ?hqCityLabel ?hqCountryLabel ?linkedin ?logo WHERE {
              BIND(wd:{{qid}} AS ?company)
              OPTIONAL { ?company wdt:P452 ?industry }
              OPTIONAL { ?company wdt:P1128 ?employees }
              OPTIONAL { ?company wdt:P571 ?inception }
              OPTIONAL { ?company wdt:P4264 ?linkedin }
              OPTIONAL { ?company wdt:P154 ?logo }
              OPTIONAL {
                SELECT ?hqCity ?hqCountry WHERE {
                  wd:{{qid}} wdt:P159 ?hqCity .
                  OPTIONAL { ?hqCity wdt:P17 ?hqCountry }
                }
                LIMIT 1
              }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en" }
            }
            LIMIT 1
            """;

        var rows = await ExecuteSparqlAsync(http, query, ct);
        if (rows.Count == 0) return;

        var row = rows[0];

        if (company.Industry == null && row.TryGetValue("industryLabel", out var industry) && !string.IsNullOrWhiteSpace(industry))
            company.Industry = industry;

        if (company.EmployeeCountRange == null && row.TryGetValue("employees", out var empStr))
        {
            // Wikidata returns decimals like "+13000" — strip sign prefix
            if (decimal.TryParse(empStr.TrimStart('+'), out var emp))
                company.EmployeeCountRange = ToEmployeeRange((int)emp);
        }

        if (company.FoundingYear == null && row.TryGetValue("inception", out var inception))
        {
            // Wikidata time format: "2002-03-14T00:00:00Z"
            if (DateTime.TryParse(inception, out var inceptionDate))
                company.FoundingYear = inceptionDate.Year;
        }

        if (company.HeadquartersCity == null && row.TryGetValue("hqCityLabel", out var city) && !string.IsNullOrWhiteSpace(city))
            company.HeadquartersCity = city;

        if (company.HeadquartersCountry == null && row.TryGetValue("hqCountryLabel", out var country) && !string.IsNullOrWhiteSpace(country))
            company.HeadquartersCountry = country;

        if (company.LinkedInUrl == null && row.TryGetValue("linkedin", out var linkedInId) && !string.IsNullOrWhiteSpace(linkedInId))
            company.LinkedInUrl = $"https://www.linkedin.com/company/{linkedInId}/";

        if (company.LogoUrl == null && row.TryGetValue("logo", out var logoFile) && !string.IsNullOrWhiteSpace(logoFile))
        {
            var fileName = logoFile.Contains('/') ? logoFile[(logoFile.LastIndexOf('/') + 1)..] : logoFile;
            company.LogoUrl = $"https://commons.wikimedia.org/wiki/Special:FilePath/{Uri.EscapeDataString(fileName)}?width=200";
        }

        company.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<List<Dictionary<string, string>>> ExecuteSparqlAsync(HttpClient http, string query, CancellationToken ct)
    {
        var url = $"{SparqlUrl}?query={Uri.EscapeDataString(query)}&format=json";

        try
        {
            var httpResponse = await GetWithRetryAsync(http, url, ct);
            if (!httpResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("SPARQL returned {Status}", httpResponse.StatusCode);
                return [];
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<SparqlResponse>(cancellationToken: ct);
            return response?.Results?.Bindings
                .Select(b => b.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value))
                .ToList() ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SPARQL query failed");
            return [];
        }
    }

    private static bool IsLikelyCompany(WikidataSearchResult result)
    {
        if (result.Description == null) return true;
        var desc = result.Description.ToLowerInvariant();
        return !NonCompanyKeywords.Any(desc.Contains);
    }

    private static string StripLegalSuffixes(string name)
    {
        var lower = name.ToLowerInvariant().Trim();
        foreach (var suffix in LegalSuffixes)
        {
            if (lower.EndsWith(suffix))
                return lower[..^suffix.Length].Trim();
        }
        return lower;
    }

    private static string ToEmployeeRange(int count) => count switch
    {
        < 50 => "1-50",
        < 200 => "50-200",
        < 500 => "200-500",
        < 1_000 => "500-1000",
        < 5_000 => "1000-5000",
        < 10_000 => "5000-10000",
        _ => "10000+"
    };

    // --- Wikidata API response types ---

    private record WikidataSearchResponse(
        [property: JsonPropertyName("search")] List<WikidataSearchResult> Search);

    private record WikidataSearchResult(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("description")] string? Description);

    private record SparqlResponse(
        [property: JsonPropertyName("results")] SparqlResults? Results);

    private record SparqlResults(
        [property: JsonPropertyName("bindings")] List<Dictionary<string, SparqlValue>> Bindings);

    private record SparqlValue(
        [property: JsonPropertyName("value")] string Value);
}
