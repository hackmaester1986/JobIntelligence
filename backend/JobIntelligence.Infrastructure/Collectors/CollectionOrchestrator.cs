using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Parsing;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using static JobIntelligence.Infrastructure.Parsing.SkillMatcher;

namespace JobIntelligence.Infrastructure.Collectors;

public class CollectionOrchestrator(
    IEnumerable<IJobCollector> collectors,
    ApplicationDbContext db,
    ICompanyStatsService statsService,
    IJobEmbeddingService embeddingService,
    IGeocodingService geocodingService,
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

            // Load skill taxonomy once per source run for inline skill tagging
            var skillEntries = (await db.SkillTaxonomies
                .Select(s => new { s.Id, s.CanonicalName, s.Aliases })
                .ToListAsync(ct))
                .Select(s => new SkillEntry(
                    s.Id,
                    s.CanonicalName,
                    s.Aliases.RootElement.EnumerateArray()
                        .Select(a => a.GetString() ?? "").Where(a => a.Length > 0).ToArray()))
                .ToList();

            int totalFetched = 0, totalNew = 0, totalUpdated = 0, totalRemoved = 0;
            var allNewPostingIds = new List<long>();

            foreach (var company in companies)
            {
                try
                {
                    var collectStartedAt = DateTime.UtcNow;
                    var result = await collector.CollectAsync(company, ct);
                    totalFetched += result.Fetched;
                    totalNew += result.New;
                    totalUpdated += result.Updated;
                    totalRemoved += result.Removed;

                    if (result.New > 0)
                    {
                        var newPostings = await db.JobPostings
                            .Where(p => p.CompanyId == company.Id && p.SourceId == source.Id
                                        && p.FirstSeenAt >= collectStartedAt)
                            .Select(p => new { p.Id, p.Description })
                            .ToListAsync(ct);

                        allNewPostingIds.AddRange(newPostings.Select(p => p.Id));

                        foreach (var posting in newPostings)
                        {
                            foreach (var match in Match(posting.Description, skillEntries))
                            {
                                db.JobSkills.Add(new JobSkill
                                {
                                    JobPostingId = posting.Id,
                                    SkillId = match.SkillId,
                                    IsRequired = true,
                                    ExtractionMethod = "keyword"
                                });
                            }

                            var exp  = ExperienceParser.Parse(posting.Description);
                            var edu  = EducationParser.Parse(posting.Description);
                            var auth = WorkAuthorizationParser.Parse(posting.Description);

                            if (exp.Min != null || exp.Max != null || edu != null || auth != null)
                            {
                                await db.JobPostings
                                    .Where(j => j.Id == posting.Id)
                                    .ExecuteUpdateAsync(s => s
                                        .SetProperty(j => j.YearsExperienceMin,      exp.Min)
                                        .SetProperty(j => j.YearsExperienceMax,      exp.Max)
                                        .SetProperty(j => j.EducationLevel,          edu)
                                        .SetProperty(j => j.RequiresUsAuthorization, auth), ct);
                            }
                        }
                    }

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

            if (allNewPostingIds.Count > 0)
            {
                try
                {
                    await embeddingService.EmbedNewPostingsAsync(allNewPostingIds, ct);
                    logger.LogInformation("{Source}: embedded {Count} new postings", collector.SourceName, allNewPostingIds.Count);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Embedding failed for {Source} — {Count} postings skipped", collector.SourceName, allNewPostingIds.Count);
                }

                try
                {
                    await geocodingService.WarmCacheAsync(ct);
                    var toGeocode = await db.JobPostings
                        .Where(p => allNewPostingIds.Contains(p.Id) && !p.IsRemote
                                    && p.Latitude == null
                                    && (p.LocationCity != null || p.LocationState != null || p.LocationRaw != null))
                        .Select(p => new { p.Id, p.LocationCity, p.LocationState, p.LocationCountry, p.LocationRaw })
                        .ToListAsync(ct);

                    int geocoded = 0;
                    foreach (var posting in toGeocode)
                    {
                        var coords = await geocodingService.GeocodeAsync(
                            posting.LocationCity, posting.LocationState, posting.LocationCountry, posting.LocationRaw, ct);
                        if (coords.HasValue)
                        {
                            await db.JobPostings.Where(j => j.Id == posting.Id)
                                .ExecuteUpdateAsync(s => s
                                    .SetProperty(j => j.Latitude, coords.Value.Lat)
                                    .SetProperty(j => j.Longitude, coords.Value.Lng), ct);
                            geocoded++;
                        }
                    }
                    logger.LogInformation("{Source}: geocoded {Geocoded}/{Total} new postings", collector.SourceName, geocoded, toGeocode.Count);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Geocoding failed for {Source}", collector.SourceName);
                }
            }

            await db.CollectionRuns
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CompletedAt, DateTime.UtcNow)
                    .SetProperty(r => r.Status, "success")
                    .SetProperty(r => r.JobsFetched, totalFetched)
                    .SetProperty(r => r.JobsNew, totalNew)
                    .SetProperty(r => r.JobsUpdated, totalUpdated)
                    .SetProperty(r => r.JobsRemoved, totalRemoved), ct);

            logger.LogInformation("{Source} collection complete: fetched={F} new={N} updated={U} removed={R}",
                collector.SourceName, totalFetched, totalNew, totalUpdated, totalRemoved);
        }

        // Write dashboard snapshots once after all collectors finish
        try
        {
            await WriteDashboardSnapshotsAsync(ct);
            logger.LogInformation("Dashboard snapshots written");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write dashboard snapshots");
        }
    }

    public async Task WriteDashboardSnapshotsAsync(CancellationToken ct = default)
    {
        // Use the EF-managed connection — open once and don't dispose (EF owns lifetime)
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);

        try
        {
            foreach (var isUs in new[] { false, true })
            {
                var usFilter = isUs
                    ? "AND (jp.is_us_posting IS TRUE OR jp.is_us_posting IS NULL)"
                    : "";

                var sql = $"""
                    WITH filtered_jobs AS (
                        SELECT jp.company_id, jp.is_remote, jp.is_hybrid, jp.first_seen_at, jp.seniority_level, jp.department
                        FROM job_postings jp
                        JOIN companies c ON c.id = jp.company_id
                        WHERE c.is_tech_hiring IS DISTINCT FROM FALSE AND jp.is_active
                        {usFilter}
                    ),
                    valid_companies AS (
                        SELECT id, canonical_name AS name, logo_url AS "logoUrl", active_job_count AS "jobCount"
                        FROM companies WHERE is_tech_hiring IS DISTINCT FROM FALSE
                    ),
                    counts AS (
                        SELECT
                            COUNT(*) AS total_active,
                            COUNT(*) FILTER (WHERE is_remote) AS remote,
                            COUNT(*) FILTER (WHERE is_hybrid) AS hybrid,
                            COUNT(*) FILTER (WHERE first_seen_at >= CURRENT_DATE) AS active_today
                        FROM filtered_jobs
                    ),
                    seniority AS (
                        SELECT seniority_level AS label, COUNT(*) AS count
                        FROM filtered_jobs WHERE seniority_level IS NOT NULL
                        GROUP BY seniority_level ORDER BY count DESC
                    ),
                    departments AS (
                        SELECT department, COUNT(*) AS count
                        FROM filtered_jobs WHERE department IS NOT NULL
                        GROUP BY department ORDER BY count DESC LIMIT 10
                    ),
                    company_count AS (SELECT COUNT(*) AS total FROM valid_companies),
                    top_companies AS (SELECT * FROM valid_companies ORDER BY "jobCount" DESC LIMIT 10)
                    SELECT
                        (SELECT total_active FROM counts),
                        (SELECT total FROM company_count),
                        (SELECT remote FROM counts),
                        (SELECT hybrid FROM counts),
                        (SELECT active_today FROM counts),
                        (SELECT JSON_AGG(ROW_TO_JSON(top_companies)) FROM top_companies),
                        (SELECT JSON_AGG(ROW_TO_JSON(seniority)) FROM seniority),
                        (SELECT JSON_AGG(ROW_TO_JSON(departments)) FROM departments)
                    """;

                await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 180 };
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);

                var totalActive = reader.GetInt64(0);
                var remote      = reader.GetInt64(2);
                var hybrid      = reader.GetInt64(3);

                var snapshot = new DashboardSnapshot
                {
                    IsUs            = isUs,
                    TotalActiveJobs = totalActive,
                    TotalCompanies  = reader.GetInt64(1),
                    RemoteJobs      = remote,
                    HybridJobs      = hybrid,
                    OnsiteJobs      = totalActive - remote - hybrid,
                    ActiveToday     = reader.GetInt64(4),
                    TopCompanies    = JsonDocument.Parse(reader.IsDBNull(5) ? "[]" : reader.GetString(5)),
                    JobsBySeniority = JsonDocument.Parse(reader.IsDBNull(6) ? "[]" : reader.GetString(6)),
                    TopDepartments  = JsonDocument.Parse(reader.IsDBNull(7) ? "[]" : reader.GetString(7)),
                };

                await reader.CloseAsync();
                db.DashboardSnapshots.Add(snapshot);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<Company>> GetCompaniesForSource(string sourceName, CancellationToken ct)
    {
        return sourceName switch
        {
            "greenhouse" => await db.Companies
                .Where(c => c.GreenhouseBoardToken != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            "lever" => await db.Companies
                .Where(c => c.LeverCompanySlug != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            "ashby" => await db.Companies
                .Where(c => c.AshbyBoardSlug != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            "smartrecruiters" => await db.Companies
                .Where(c => c.SmartRecruitersSlug != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            "workday" => await db.Companies
                .Where(c => c.WorkdayHost != null && c.WorkdayCareerSite != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            "recruitee" => await db.Companies
                .Where(c => c.RecruiteeSlug != null && c.IsTechHiring != false)
                .ToListAsync(ct),
            _ => []
        };
    }
}
