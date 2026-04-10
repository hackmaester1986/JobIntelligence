using Amazon.Extensions.NETCore.Setup;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureAppConfiguration((ctx, config) =>
{
    if (!ctx.HostingEnvironment.IsDevelopment())
    {
        config.AddSystemsManager(source =>
        {
            source.Path = "/jobintelligence";
            source.AwsOptions = new AWSOptions { Region = Amazon.RegionEndpoint.USEast1 };
            source.Optional = false;
        });
    }
});

builder.ConfigureServices((ctx, services) =>
{
    services.AddInfrastructure(ctx.Configuration);
});

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var orchestrator = host.Services.GetRequiredService<ICollectionOrchestrator>();

var source = args.FirstOrDefault();
logger.LogInformation("Worker starting. Source: {Source}", source ?? "all");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await orchestrator.RunAsync(source, cts.Token);
    logger.LogInformation("Worker finished successfully");
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Worker cancelled");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Worker failed");
    return 1;
}
