using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/crawler")]
public class CrawlerController(IServiceScopeFactory scopeFactory, ILogger<CrawlerController> logger) : ControllerBase
{
    private static readonly string[] ValidSources = ["greenhouse", "lever", "ashby", "smartrecruiters", "workday"];

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
