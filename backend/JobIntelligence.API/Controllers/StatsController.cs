using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(ApplicationDbContext db,IMemoryCache memoryCache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStats([FromQuery] bool? isUs, CancellationToken ct)
    {
        var cacheKey = $"dashboard_stats_{isUs}";
        if (memoryCache.TryGetValue(cacheKey, out var cached))
              return Ok(cached);

        var since = DateTime.UtcNow.AddHours(-24);
        // When isUs=true: include only US + unknown postings; otherwise all
        var usFilter = isUs == true
            ? "AND (jp.is_us_posting IS TRUE OR jp.is_us_posting IS NULL)"
            : "";

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Single round-trip: all counts + grouped data in one query using CTEs
        var sql = $"""
            WITH filtered_jobs AS (
                SELECT
                    jp.company_id,
                    jp.is_active,
                    jp.is_remote,
                    jp.is_hybrid,
                    jp.first_seen_at,
                    jp.seniority_level,
                    jp.department
                FROM job_postings jp
                JOIN companies c
                ON c.id = jp.company_id
                WHERE c.is_tech_hiring IS DISTINCT FROM FALSE and jp.is_active
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
                FROM filtered_jobs
                WHERE seniority_level IS NOT NULL
                GROUP BY seniority_level
                ORDER BY count DESC
            ),
            departments AS (
                SELECT department, COUNT(*) AS count
                FROM filtered_jobs
                WHERE department IS NOT NULL
                GROUP BY department
                ORDER BY count DESC
                LIMIT 10
            ),
            company_count AS (
                SELECT COUNT(*) AS total FROM valid_companies
            ),
            top_companies AS (
                SELECT *
                FROM valid_companies
                ORDER BY "jobCount" DESC LIMIT 10
            )
            SELECT
                (SELECT total_active  FROM counts)       AS total_active_jobs,
                (SELECT total         FROM company_count) AS total_companies,
                (SELECT remote        FROM counts)       AS remote_jobs,
                (SELECT hybrid        FROM counts)       AS hybrid_jobs,
                (SELECT active_today  FROM counts)       AS active_today,
                (SELECT JSON_AGG(ROW_TO_JSON(top_companies)) FROM top_companies) AS top_companies,
                (SELECT JSON_AGG(ROW_TO_JSON(seniority))     FROM seniority)     AS by_seniority,
                (SELECT JSON_AGG(ROW_TO_JSON(departments))   FROM departments)   AS top_departments
            
            
        """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("since", since);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var totalActive = reader.GetInt64(0);
        var remoteJobs  = reader.GetInt64(2);
        var hybridJobs  = reader.GetInt64(3);

        var stats = new {
            totalActiveJobs = totalActive,
            totalCompanies  = reader.GetInt64(1),
            remoteJobs,
            hybridJobs,
            onsiteJobs      = totalActive - remoteJobs - hybridJobs,
            activeToday     = reader.GetInt64(4),
            topCompanies    = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(5) ? "[]" : reader.GetString(5)),
            jobsBySeniority = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(6) ? "[]" : reader.GetString(6)),
            topDepartments  = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(7) ? "[]" : reader.GetString(7)),
        };
        memoryCache.Set(cacheKey, stats, TimeSpan.FromMinutes(60));
        return Ok(stats);
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> GetStatsSnapshot([FromQuery] bool? isUs, CancellationToken ct)
    {
        var snapshot = await db.DashboardSnapshots
            .Where(s => s.IsUs == (isUs == true))
            .OrderByDescending(s => s.SnapshotAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot == null)
            return NotFound(new { error = "No snapshot available yet. Run a collection first." });

        return Ok(new
        {
            snapshotAt      = snapshot.SnapshotAt,
            totalActiveJobs = snapshot.TotalActiveJobs,
            totalCompanies  = snapshot.TotalCompanies,
            remoteJobs      = snapshot.RemoteJobs,
            hybridJobs      = snapshot.HybridJobs,
            onsiteJobs      = snapshot.OnsiteJobs,
            activeToday     = snapshot.ActiveToday,
            topCompanies    = snapshot.TopCompanies,
            jobsBySeniority = snapshot.JobsBySeniority,
            topDepartments  = snapshot.TopDepartments,
        });
    }
}
