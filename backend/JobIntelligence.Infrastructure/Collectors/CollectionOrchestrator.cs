using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Collectors;

public class CollectionOrchestrator(
    IEnumerable<IJobCollector> collectors,
    ApplicationDbContext db,
    ICompanyStatsService statsService,
    ILogger<CollectionOrchestrator> logger) : ICollectionOrchestrator
{
    public async Task RunAsync(string? sourceName = null, CancellationToken ct = default)
    {
        var activeCollectors = sourceName != null
            ? collectors.Where(c => c.SourceName == sourceName).ToList()
            : collectors.ToList();

        if (activeCollectors.Count == 0)
        {
            logger.LogWarning("No collectors found for source: {Source}", sourceName ?? "all");
            return;
        }

        foreach (var collector in activeCollectors)
        {
            var source = await db.JobSources.FirstOrDefaultAsync(s => s.Name == collector.SourceName && s.IsActive, ct);
            if (source == null)
            {
                logger.LogWarning("Source '{Source}' not found or inactive", collector.SourceName);
                continue;
            }

            var run = new CollectionRun { SourceId = source.Id, StartedAt = DateTime.UtcNow, Status = "running" };
            db.CollectionRuns.Add(run);
            await db.SaveChangesAsync(ct);

            var companies = await GetCompaniesForSource(collector.SourceName, ct);
            logger.LogInformation("Starting {Source} collection for {Count} companies", collector.SourceName, companies.Count);

            int totalFetched = 0, totalNew = 0, totalUpdated = 0, totalRemoved = 0;

            foreach (var company in companies)
            {
                try
                {
                    var result = await collector.CollectAsync(company, ct);
                    totalFetched += result.Fetched;
                    totalNew += result.New;
                    totalUpdated += result.Updated;
                    totalRemoved += result.Removed;

                    run.JobsFetched = totalFetched;
                    run.JobsNew = totalNew;
                    run.JobsUpdated = totalUpdated;
                    run.JobsRemoved = totalRemoved;

                    var activeCount = await db.JobPostings
                        .CountAsync(p => p.CompanyId == company.Id && p.SourceId == source.Id && p.IsActive, ct);
                    db.CompanyJobSnapshots.Add(new Core.Entities.CompanyJobSnapshot
                    {
                        CompanyId = company.Id,
                        CollectionRunId = run.Id,
                        SnapshotAt = DateTime.UtcNow,
                        ActiveJobCount = activeCount,
                        NewCount = result.New,
                        RemovedCount = result.Removed
                    });

                    await statsService.RefreshStatsAsync(company.Id, ct);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error collecting from {Source} for {Company}", collector.SourceName, company.CanonicalName);
                    db.ChangeTracker.Clear();
                }

                await Task.Delay(100, ct);
            }

            run.CompletedAt = DateTime.UtcNow;
            run.Status = "success";
            await db.SaveChangesAsync(ct);

            logger.LogInformation("{Source} collection complete: fetched={F} new={N} updated={U} removed={R}",
                collector.SourceName, totalFetched, totalNew, totalUpdated, totalRemoved);
        }
    }

    private async Task<List<Company>> GetCompaniesForSource(string sourceName, CancellationToken ct)
    {
        return sourceName switch
        {
            "greenhouse" => await db.Companies
                .Where(c => c.GreenhouseBoardToken != null)
                .ToListAsync(ct),
            "lever" => await db.Companies
                .Where(c => c.LeverCompanySlug != null)
                .ToListAsync(ct),
            "ashby" => await db.Companies
                .Where(c => c.AshbyBoardSlug != null)
                .ToListAsync(ct),
            "smartrecruiters" => await db.Companies
                .Where(c => c.SmartRecruitersSlug != null)
                .ToListAsync(ct),
            "workday" => await db.Companies
                .Where(c => c.WorkdayHost != null && c.WorkdayCareerSite != null)
                .ToListAsync(ct),
            _ => await db.Companies.ToListAsync(ct)
        };
    }
}
