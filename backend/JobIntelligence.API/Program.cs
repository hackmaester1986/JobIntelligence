using JobIntelligence.API.Services;
using JobIntelligence.Infrastructure;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<CollectionSchedulerService>();

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
