using System.Text.Json;
using System.Text.Json.Serialization;
using JobIntelligence.API.Filters;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/crawler")]
[AdminKey]
public class CrawlerController(IServiceScopeFactory scopeFactory, ILogger<CrawlerController> logger) : ControllerBase
{
    private static readonly string[] ValidSources = ["greenhouse", "lever", "ashby", "smartrecruiters", "workday", "recruitee"];

    public record SlugDiscoveryRequest(
        List<string>? AshbyUrls,
        List<string>? AshbySlugs,
        List<string>? GreenhouseUrls,
        List<string>? GreenhouseSlugs,
        List<string>? LeverUrls,
        List<string>? LeverSlugs,
        List<string>? SmartRecruitersUrls,
        List<string>? SmartRecruitersSlugs);

    [HttpPost("common-crawl")]
    public IActionResult DiscoverViaCommonCrawl([FromQuery] string? source, [FromQuery] bool dryRun = false)
    {
        if (source != null && !ValidSources.Contains(source, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Unknown source '{source}'.", validSources = ValidSources });

        var label = source ?? "all sources";
        logger.LogInformation("Common Crawl discovery triggered for {Source} (dryRun={DryRun})", label, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var crawler = scope.ServiceProvider.GetRequiredService<ICommonCrawlService>();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();

            try
            {
                // Step 1: Discover slugs from Common Crawl index
                var crawlResult = await crawler.DiscoverSlugsAsync(source, CancellationToken.None);
                logger.LogInformation(
                    "Common Crawl found {G} Greenhouse, {L} Lever, {A} Ashby, {SR} SmartRecruiters, {WD} Workday, {RC} Recruitee slugs",
                    crawlResult.GreenhouseSlugs.Count, crawlResult.LeverSlugs.Count, crawlResult.AshbySlugs.Count,
                    crawlResult.SmartRecruitersSlugs.Count, crawlResult.WorkdayEntries.Count, crawlResult.RecruiteeSlugs.Count);

                // Step 2: Validate each slug against live APIs and optionally import
                var result = await discovery.DiscoverFromSlugsAsync(
                    crawlResult.GreenhouseSlugs, crawlResult.LeverSlugs, crawlResult.AshbySlugs,
                    crawlResult.SmartRecruitersSlugs, crawlResult.WorkdayEntries,
                    crawlResult.RecruiteeSlugs, dryRun, CancellationToken.None);

                var sources = new[] { "Greenhouse", "Lever", "Ashby", "SmartRecruiters", "Workday", "Recruitee" };
                foreach (var s in sources)
                {
                    var v = result.ValidatedPerSource.GetValueOrDefault(s);
                    var f = result.FailedPerSource.GetValueOrDefault(s);
                    logger.LogInformation("  {Source}: validated={V} failed={F}", s, v, f);
                }
                logger.LogInformation(
                    "Common Crawl discovery complete (dryRun={DryRun}): validated={A} skipped={S} failed={F}",
                    dryRun, result.Added, result.Skipped, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Common Crawl discovery failed");
            }
        });

        return Accepted(new { message = $"Common Crawl discovery started for {label}", dryRun });
    }

    [HttpPost("slugs")]
    public IActionResult DiscoverFromSlugs([FromBody] SlugDiscoveryRequest request, [FromQuery] bool dryRun = false)
    {
        var ashby = ParseSlugs(request.AshbyUrls,   request.AshbySlugs);
        var gh    = ParseSlugs(request.GreenhouseUrls, request.GreenhouseSlugs);
        var lever = ParseSlugs(request.LeverUrls,   request.LeverSlugs);
        var sr    = ParseSlugs(request.SmartRecruitersUrls, request.SmartRecruitersSlugs);

        var total = ashby.Count + gh.Count + lever.Count + sr.Count;
        if (total == 0)
            return BadRequest(new { error = "No slugs or URLs provided." });

        logger.LogInformation(
            "Slug discovery triggered: greenhouse={G} lever={L} ashby={A} smartrecruiters={SR} dryRun={D}",
            gh.Count, lever.Count, ashby.Count, sr.Count, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            try
            {
                var result = await discovery.DiscoverFromSlugsAsync(gh, lever, ashby, sr, [], [], dryRun, CancellationToken.None);
                logger.LogInformation(
                    "Slug discovery complete (dryRun={D}): added={A} skipped={S} failed={F}",
                    dryRun, result.Added, result.Skipped, result.Failed);
                foreach (var kvp in result.ValidatedPerSource)
                    logger.LogInformation("  {Source}: validated={V} failed={F}", kvp.Key, kvp.Value,
                        result.FailedPerSource.GetValueOrDefault(kvp.Key));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Slug discovery failed");
            }
        });

        return Accepted(new { message = $"Slug discovery started: {total} slugs", dryRun, ashby, greenhouse = gh, lever, smartRecruiters = sr });
    }

    private static List<string> ParseSlugs(List<string>? urls, List<string>? slugs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in urls ?? [])
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) continue;
            var slug = uri.AbsolutePath.Trim('/').Split('/')[0];
            if (!string.IsNullOrWhiteSpace(slug))
                result.Add(slug);
        }

        foreach (var slug in slugs ?? [])
            if (!string.IsNullOrWhiteSpace(slug))
                result.Add(slug.Trim());

        return [.. result];
    }

    // Each keyword generates a separate Brave query, surfacing different companies per term
    private static readonly string[] SearchKeywords =
    [
        "software engineer", "product manager", "data engineer", "devops", "backend engineer",
        "frontend engineer", "machine learning", "fullstack", "platform engineer", "site reliability",
        "data scientist", "security engineer", "mobile engineer", "cloud engineer", "engineering manager",
        "software developer", "qa engineer", "infrastructure engineer", "artificial intelligence",
        "cloud architect", "technical program manager", "solutions architect", "ai engineer"
    ];

    [HttpPost("brave-search")]
    public async Task<IActionResult> DiscoverViaBraveSearch(
        [FromQuery] string? source,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse"]      = "boards.greenhouse.io",
            ["lever"]           = "jobs.lever.co",
            ["ashby"]           = "jobs.ashbyhq.com",
            ["smartrecruiters"] = "jobs.smartrecruiters.com",
        };

        if (source != null && !targets.ContainsKey(source))
            return BadRequest(new { error = $"Unknown source '{source}'. Valid: {string.Join(", ", targets.Keys)}" });

        using var scope = scopeFactory.CreateScope();
        var config      = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var discovery   = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var apiKey = config["BraveSearch:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(500, new { error = "BraveSearch:ApiKey not configured" });

        var client = httpFactory.CreateClient("BraveSearch");

        var activeSources = source != null
            ? targets.Where(kv => kv.Key == source).ToList()
            : targets.ToList();

        var existingGreenhouse      = await db.Companies.Where(c => c.GreenhouseBoardToken != null).Select(c => c.GreenhouseBoardToken!).ToHashSetAsync(ct);
        var existingLever           = await db.Companies.Where(c => c.LeverCompanySlug != null).Select(c => c.LeverCompanySlug!).ToHashSetAsync(ct);
        var existingAshby           = await db.Companies.Where(c => c.AshbyBoardSlug != null).Select(c => c.AshbyBoardSlug!).ToHashSetAsync(ct);
        var existingSmartRecruiters = await db.Companies.Where(c => c.SmartRecruitersSlug != null).Select(c => c.SmartRecruitersSlug!).ToHashSetAsync(ct);

        var summary = new Dictionary<string, object>();

        foreach (var (sourceName, domain) in activeSources)
        {
            var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var keyword in SearchKeywords)
            {
                // Brave caps offset at 9 (10 pages max); use offset 0 and 1 per keyword for variety
                for (int offset = 0; offset <= 1; offset++)
                {
                    var q = Uri.EscapeDataString($"site:{domain} {keyword}");
                    var url = $"https://api.search.brave.com/res/v1/web/search?q={q}&count=20&offset={offset}&search_lang=en";

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("X-Subscription-Token", apiKey);
                    request.Headers.Add("Accept", "application/json");

                    try
                    {
                        var response = await client.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode)
                        {
                            logger.LogWarning("BraveSearch {Status} for {Source}/{Keyword}", (int)response.StatusCode, sourceName, keyword);
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(ct);
                        var result = JsonSerializer.Deserialize<BraveSearchResponse>(json);

                        foreach (var hit in result?.Web?.Results ?? [])
                        {
                            if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)) continue;
                            var slug = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                            if (!string.IsNullOrWhiteSpace(slug))
                                discovered.Add(slug);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "BraveSearch request failed for {Source}/{Keyword}", sourceName, keyword);
                    }

                    await Task.Delay(300, ct); // stay under rate limit
                }
            }

            var existingForSource = sourceName switch
            {
                "greenhouse"      => existingGreenhouse,
                "lever"           => existingLever,
                "ashby"           => existingAshby,
                "smartrecruiters" => existingSmartRecruiters,
                _                 => []
            };

            var newSlugs = discovered.Where(s => !existingForSource.Contains(s)).ToList();
            logger.LogInformation("BraveSearch [{Source}]: discovered={D} new={N} dryRun={DR}",
                sourceName, discovered.Count, newSlugs.Count, dryRun);

            if (newSlugs.Count == 0)
            {
                summary[sourceName] = new { discovered = discovered.Count, new_ = 0 };
                continue;
            }

            var discoverResult = await discovery.DiscoverFromSlugsAsync(
                sourceName == "greenhouse"      ? newSlugs : [],
                sourceName == "lever"           ? newSlugs : [],
                sourceName == "ashby"           ? newSlugs : [],
                sourceName == "smartrecruiters" ? newSlugs : [],
                [], [], dryRun, ct);

            summary[sourceName] = new
            {
                discovered = discovered.Count,
                new_       = newSlugs.Count,
                validated  = discoverResult.ValidatedPerSource.GetValueOrDefault(ToDisplayName(sourceName)),
                added      = dryRun ? (object)"(dry run)" : discoverResult.Added,
                skipped    = discoverResult.Skipped,
                failed     = discoverResult.Failed,
            };
        }

        return Ok(new { dryRun, summary });
    }

    private static string ToDisplayName(string source) => source switch
    {
        "greenhouse"      => "Greenhouse",
        "lever"           => "Lever",
        "ashby"           => "Ashby",
        "smartrecruiters" => "SmartRecruiters",
        _                 => source
    };

    private record BraveSearchResponse(
        [property: JsonPropertyName("web")] BraveWebResults? Web);

    private record BraveWebResults(
        [property: JsonPropertyName("results")] List<BraveSearchResult>? Results);

    private record BraveSearchResult(
        [property: JsonPropertyName("url")]         string Url,
        [property: JsonPropertyName("title")]       string? Title,
        [property: JsonPropertyName("description")] string? Description);

    [HttpPost("yc-discover")]
    public IActionResult DiscoverFromYC(
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool dryRun = false)
    {
        logger.LogInformation("YC discovery triggered (activeOnly={A} dryRun={D})", activeOnly, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var discovery   = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var client      = httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);

            // Load normalized company names to skip companies already in the DB
            var knownNames = await db.Companies
                .Where(c => c.NormalizedName != null)
                .Select(c => c.NormalizedName!)
                .ToHashSetAsync();

            var slugsToTry = new List<string>();
            int page = 1;

            while (true)
            {
                try
                {
                    var url = $"https://api.ycombinator.com/v0.1/companies?page={page}&per_page=100";
                    var json = await client.GetStringAsync(url);
                    var response = JsonSerializer.Deserialize<YcResponse>(json);

                    if (response?.Companies == null || response.Companies.Count == 0)
                    {
                        page++;
                        continue;
                    }

                    foreach (var company in response.Companies)
                    {
                        if (string.IsNullOrWhiteSpace(company.Slug)) continue;
                        if (activeOnly && !string.Equals(company.Status, "Active", StringComparison.OrdinalIgnoreCase)) continue;
                        var normalized = company.Slug.ToLowerInvariant().Replace("-", "").Replace("_", "");
                        if (knownNames.Contains(normalized)) continue;
                        slugsToTry.Add(company.Slug);
                    }

                    logger.LogInformation("YC discovery: fetched page {Page}/{Total} ({Count} candidates so far)",
                        page, response.TotalPages, slugsToTry.Count);

                    if (page >= response.TotalPages) break;
                    page++;
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "YC discovery: failed fetching page {Page}", page);
                    break;
                }
            }

            logger.LogInformation("YC discovery: {Count} slugs to validate against ATS APIs", slugsToTry.Count);

            if (slugsToTry.Count == 0)
            {
                logger.LogInformation("YC discovery: nothing new to discover");
                return;
            }

            // Try each slug against all 4 ATS sources simultaneously
           var result = await discovery.DiscoverFromSlugsAsync(
                slugsToTry, slugsToTry, slugsToTry, slugsToTry, [], [], dryRun, CancellationToken.None);

            foreach (var kvp in result.ValidatedPerSource)
                logger.LogInformation("YC discovery [{Source}]: validated={V} failed={F}",
                    kvp.Key, kvp.Value, result.FailedPerSource.GetValueOrDefault(kvp.Key));
            
            logger.LogInformation(
                "YC discovery complete (dryRun={D}): added={A} skipped={S} failed={F}",
                dryRun, result.Added, result.Skipped, result.Failed);
        });

        return Accepted(new { message = "YC discovery started", activeOnly, dryRun });
    }

    private record YcResponse(
        [property: JsonPropertyName("companies")]   List<YcCompany> Companies,
        [property: JsonPropertyName("page")]        int Page,
        [property: JsonPropertyName("totalPages")]  int TotalPages);

    private record YcCompany(
        [property: JsonPropertyName("slug")]        string? Slug,
        [property: JsonPropertyName("status")]      string? Status,
        [property: JsonPropertyName("teamSize")]    int? TeamSize);

    [HttpPost("wikidata-enrich")]
    public IActionResult EnrichFromWikidata([FromQuery] int batchSize = 100)
    {
        logger.LogInformation("Wikidata enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IWikidataEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Wikidata enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Wikidata enrichment failed");
            }
        });

        return Accepted(new { message = $"Wikidata enrichment started for batch of {batchSize}" });
    }

    [HttpPost("web-enrich")]
    public IActionResult EnrichFromWeb([FromQuery] int batchSize = 20)
    {
        logger.LogInformation("Web enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IWebEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Web enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Web enrichment failed");
            }
        });

        return Accepted(new { message = $"Web enrichment started for batch of {batchSize}" });
    }

    [HttpPost("description-enrich")]
    public IActionResult EnrichFromDescriptions([FromQuery] int batchSize = 50)
    {
        logger.LogInformation("Description enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IDescriptionEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Description enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Description enrichment failed");
            }
        });

        return Accepted(new { message = $"Description enrichment started for batch of {batchSize}" });
    }
}
