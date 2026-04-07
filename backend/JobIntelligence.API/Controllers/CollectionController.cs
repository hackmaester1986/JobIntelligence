using JobIntelligence.API.Filters;
using JobIntelligence.API.Services;
using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/collection")]
[AdminKey]
public class CollectionController(
    IServiceScopeFactory scopeFactory,
    CollectionCancellationService cancellation,
    ILogger<CollectionController> logger) : ControllerBase
{
    [HttpPost("trigger")]
    public IActionResult Trigger([FromQuery] string? source)
    {
        if (cancellation.IsRunning)
            return Conflict(new { message = "A collection is already running. POST to /cancel to stop it first." });

        logger.LogInformation("Collection trigger received for source: {Source}", source ?? "all");
        var ct = cancellation.StartNew();

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ICollectionOrchestrator>();
            try
            {
                await orchestrator.RunAsync(source, ct);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Collection cancelled");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background collection failed");
            }
        });

        return Accepted(new { message = $"Collection started for: {source ?? "all sources"}" });
    }

    [HttpPost("cancel")]
    public IActionResult Cancel()
    {
        if (!cancellation.IsRunning)
            return Ok(new { message = "No collection is currently running." });

        cancellation.Cancel();
        logger.LogWarning("Collection cancelled via API");
        return Ok(new { message = "Cancellation requested. The collection will stop at the next checkpoint." });
    }

    [HttpGet("status")]
    public IActionResult Status() =>
        Ok(new { running = cancellation.IsRunning });

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
