using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-24);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        // Single round-trip: all counts + grouped data in one query using CTEs
        const string sql = """
            WITH counts AS (
                SELECT
                    COUNT(*) FILTER (WHERE is_active)                          AS total_active,
                    COUNT(*) FILTER (WHERE is_active AND is_remote)            AS remote,
                    COUNT(*) FILTER (WHERE is_active AND is_hybrid)            AS hybrid,
                    COUNT(*) FILTER (WHERE is_active AND first_seen_at >= @since) AS active_today
                FROM job_postings
            ),
            company_count AS (
                SELECT COUNT(*) AS total FROM companies WHERE active_job_count > 0
            ),
            top_companies AS (
                SELECT id, canonical_name AS name, logo_url AS "logoUrl", active_job_count AS "jobCount"
                FROM companies WHERE active_job_count > 0
                ORDER BY active_job_count DESC LIMIT 10
            ),
            seniority AS (
                SELECT seniority_level AS label, COUNT(*) AS count
                FROM job_postings WHERE is_active AND seniority_level IS NOT NULL
                GROUP BY seniority_level ORDER BY count DESC
            ),
            departments AS (
                SELECT department, COUNT(*) AS count
                FROM job_postings WHERE is_active AND department IS NOT NULL
                GROUP BY department ORDER BY count DESC LIMIT 10
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

        return Ok(new {
            totalActiveJobs = totalActive,
            totalCompanies  = reader.GetInt64(1),
            remoteJobs,
            hybridJobs,
            onsiteJobs      = totalActive - remoteJobs - hybridJobs,
            activeToday     = reader.GetInt64(4),
            topCompanies    = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(5) ? "[]" : reader.GetString(5)),
            jobsBySeniority = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(6) ? "[]" : reader.GetString(6)),
            topDepartments  = System.Text.Json.JsonSerializer.Deserialize<object>(reader.IsDBNull(7) ? "[]" : reader.GetString(7)),
        });
    }
}
