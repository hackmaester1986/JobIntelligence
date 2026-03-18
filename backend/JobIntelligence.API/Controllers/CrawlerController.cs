using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/crawler")]
public class CrawlerController(IServiceScopeFactory scopeFactory, ILogger<CrawlerController> logger) : ControllerBase
{
    [HttpPost("common-crawl")]
    public IActionResult DiscoverViaCommonCrawl()
    {
        logger.LogInformation("Common Crawl discovery triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var crawler = scope.ServiceProvider.GetRequiredService<ICommonCrawlService>();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();

            try
            {
                // Step 1: Discover slugs from Common Crawl index
                var crawlResult = await crawler.DiscoverSlugsAsync(CancellationToken.None);
                logger.LogInformation(
                    "Common Crawl found {G} Greenhouse, {L} Lever, {A} Ashby, {W} Workable, {SR} SmartRecruiters, {BH} BambooHR slugs",
                    crawlResult.GreenhouseSlugs.Count, crawlResult.LeverSlugs.Count, crawlResult.AshbySlugs.Count,
                    crawlResult.WorkableSlugs.Count, crawlResult.SmartRecruitersSlugs.Count, crawlResult.BambooHrSlugs.Count);

                // Step 2: Validate each slug against live APIs and import
                var result = await discovery.DiscoverFromSlugsAsync(
                    crawlResult.GreenhouseSlugs, crawlResult.LeverSlugs, crawlResult.AshbySlugs,
                    crawlResult.SmartRecruitersSlugs, CancellationToken.None);

                logger.LogInformation(
                    "Common Crawl discovery complete: added={A} skipped={S} failed={F}",
                    result.Added, result.Skipped, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Common Crawl discovery failed");
            }
        });

        return Accepted(new { message = "Common Crawl discovery started" });
    }
}
