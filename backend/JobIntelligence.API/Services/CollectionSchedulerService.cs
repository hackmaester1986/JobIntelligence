using JobIntelligence.Core.Interfaces;

namespace JobIntelligence.API.Services;

public class CollectionSchedulerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CollectionSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("CollectionSchedule:Enabled", true))
        {
            logger.LogInformation("Collection scheduler is disabled");
            return;
        }

        var runAtHour = configuration.GetValue("CollectionSchedule:RunAtHourUtc", 6);
        logger.LogInformation("Collection scheduler started — runs daily at {Hour:D2}:00 UTC", runAtHour);

        while (!ct.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun(runAtHour);
            logger.LogInformation("Next scheduled collection in {Hours:F1} hours", delay.TotalHours);

            await Task.Delay(delay, ct);
            if (ct.IsCancellationRequested) break;

            logger.LogInformation("Scheduled collection starting");
            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ICollectionOrchestrator>();
                await orchestrator.RunAsync(ct: ct);
                logger.LogInformation("Scheduled collection complete");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled collection failed");
            }
        }
    }

    private static TimeSpan TimeUntilNextRun(int runAtHour)
    {
        var now = DateTime.UtcNow;
        var next = DateTime.UtcNow.Date.AddHours(runAtHour);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
