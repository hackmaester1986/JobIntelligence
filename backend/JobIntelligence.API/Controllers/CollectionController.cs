using JobIntelligence.API.Filters;
using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/collection")]
[AdminKey]
public class CollectionController(IServiceScopeFactory scopeFactory, ILogger<CollectionController> logger) : ControllerBase
{
    [HttpPost("trigger")]
    public IActionResult Trigger([FromQuery] string? source)
    {
        logger.LogInformation("Collection trigger received for source: {Source}", source ?? "all");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ICollectionOrchestrator>();
            try
            {
                await orchestrator.RunAsync(source, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background collection failed");
            }
        });

        return Accepted(new { message = $"Collection started for: {source ?? "all sources"}" });
    }

    [HttpPost("discover-companies")]
    public IActionResult DiscoverCompanies()
    {
        logger.LogInformation("Company discovery triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            try
            {
                var result = await svc.DiscoverAndImportAsync(CancellationToken.None);
                logger.LogInformation(
                    "Discovery complete: added={A} skipped={S} failed={F}",
                    result.Added, result.Skipped, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background company discovery failed");
            }
        });

        return Accepted(new { message = "Company discovery started" });
    }
}
