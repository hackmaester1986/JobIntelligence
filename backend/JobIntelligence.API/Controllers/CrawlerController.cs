using JobIntelligence.API.Filters;
using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/crawler")]
[AdminKey]
public class CrawlerController(IServiceScopeFactory scopeFactory, ILogger<CrawlerController> logger) : ControllerBase
{
    private static readonly string[] ValidSources = ["greenhouse", "lever", "ashby", "smartrecruiters", "workday"];

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
                    "Common Crawl found {G} Greenhouse, {L} Lever, {A} Ashby, {SR} SmartRecruiters, {WD} Workday slugs",
                    crawlResult.GreenhouseSlugs.Count, crawlResult.LeverSlugs.Count, crawlResult.AshbySlugs.Count,
                    crawlResult.SmartRecruitersSlugs.Count, crawlResult.WorkdayEntries.Count);

                // Step 2: Validate each slug against live APIs and optionally import
                var result = await discovery.DiscoverFromSlugsAsync(
                    crawlResult.GreenhouseSlugs, crawlResult.LeverSlugs, crawlResult.AshbySlugs,
                    crawlResult.SmartRecruitersSlugs, crawlResult.WorkdayEntries, dryRun, CancellationToken.None);

                var sources = new[] { "Greenhouse", "Lever", "Ashby", "SmartRecruiters", "Workday" };
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
        var ashby   = ParseSlugs(request.AshbyUrls,   request.AshbySlugs);
        var gh      = ParseSlugs(request.GreenhouseUrls, request.GreenhouseSlugs);
        var lever   = ParseSlugs(request.LeverUrls,   request.LeverSlugs);
        var sr      = ParseSlugs(request.SmartRecruitersUrls, request.SmartRecruitersSlugs);

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
                var result = await discovery.DiscoverFromSlugsAsync(gh, lever, ashby, sr, [], dryRun, CancellationToken.None);
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
