using System.Threading.RateLimiting;
using Amazon.Extensions.NETCore.Setup;
using JobIntelligence.API.Services;
using JobIntelligence.Infrastructure;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddSystemsManager(source =>
    {
        source.Path = "/jobintelligence";
        source.AwsOptions = new Amazon.Extensions.NETCore.Setup.AWSOptions
        {
            Region = Amazon.RegionEndpoint.USEast1
        };
        source.Optional = false;
        source.ReloadAfter = TimeSpan.FromMinutes(30);
    });
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<CollectionSchedulerService>();
builder.Services.AddMemoryCache();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("chat-per-ip", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromHours(24),
                PermitLimit = 10,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"error":"Daily chat limit reached. Please try again tomorrow."}""", ct);
    };
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.MapControllers();

// Auto-apply migrations and seed on startup in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await dbContext.Database.MigrateAsync();
    await JobIntelligence.Infrastructure.Persistence.DbSeeder.SeedAsync(dbContext, logger);

    // Mark any runs left in "running" state from a previous process as failed
    var staleRuns = await dbContext.CollectionRuns
        .Where(r => r.Status == "running")
        .ToListAsync();
    if (staleRuns.Count > 0)
    {
        foreach (var stale in staleRuns)
        {
            stale.Status = "failed";
            stale.CompletedAt = DateTime.UtcNow;
            stale.ErrorMessage = "Process terminated before completion";
        }
        await dbContext.SaveChangesAsync();
        logger.LogWarning("Marked {Count} stale collection runs as failed", staleRuns.Count);
    }
}

app.Run();
