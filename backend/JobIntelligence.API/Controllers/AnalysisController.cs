using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalysisController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("sizes")]
    public async Task<IActionResult> GetSizes(CancellationToken ct)
    {
        var sizes = await db.Companies
            .Where(c => c.EmployeeCountRange != null && c.IsTechHiring != false)
            .Select(c => c.EmployeeCountRange!)
            .Distinct()
            .ToListAsync(ct);

        // Sort by the leading number so "11-50" comes before "51-200"
        sizes.Sort((a, b) =>
        {
            var aNum = int.TryParse(a.Split('-', '+')[0], out var x) ? x : int.MaxValue;
            var bNum = int.TryParse(b.Split('-', '+')[0], out var y) ? y : int.MaxValue;
            return aNum.CompareTo(bNum);
        });

        return Ok(sizes);
    }

    [HttpGet("distribution")]
    public async Task<IActionResult> GetDistribution(
        [FromQuery] string metric = "activeJobs",
        [FromQuery] string[]? sizes = null,
        [FromQuery] bool? isUs = null,
        CancellationToken ct = default)
    {
        var sizeFilter = sizes is { Length: > 0 }
            ? "AND c.employee_count_range = ANY(@sizes)"
            : "";

        var usFilter = isUs == true
            ? "AND c.id IN (SELECT DISTINCT company_id FROM job_postings WHERE is_us_posting = TRUE AND is_active = TRUE)"
            : "";

        var (caseExpr, sortExpr) = metric switch
        {
            "remoteJobs" => (
                """
                CASE
                    WHEN c.remote_job_count = 0              THEN '0'
                    WHEN c.remote_job_count BETWEEN 1 AND 5  THEN '1–5'
                    WHEN c.remote_job_count BETWEEN 6 AND 15 THEN '6–15'
                    WHEN c.remote_job_count BETWEEN 16 AND 30 THEN '16–30'
                    WHEN c.remote_job_count BETWEEN 31 AND 50 THEN '31–50'
                    ELSE '51+'
                END
                """,
                """
                CASE
                    WHEN c.remote_job_count = 0              THEN 1
                    WHEN c.remote_job_count BETWEEN 1 AND 5  THEN 2
                    WHEN c.remote_job_count BETWEEN 6 AND 15 THEN 3
                    WHEN c.remote_job_count BETWEEN 16 AND 30 THEN 4
                    WHEN c.remote_job_count BETWEEN 31 AND 50 THEN 5
                    ELSE 6
                END
                """),
            "salaryDisclosureRate" => (
                """
                CASE
                    WHEN c.salary_disclosure_rate = 0                        THEN '0%'
                    WHEN c.salary_disclosure_rate <= 0.25                    THEN '1–25%'
                    WHEN c.salary_disclosure_rate <= 0.50                    THEN '26–50%'
                    WHEN c.salary_disclosure_rate <= 0.75                    THEN '51–75%'
                    WHEN c.salary_disclosure_rate < 1.0                      THEN '76–99%'
                    ELSE '100%'
                END
                """,
                """
                CASE
                    WHEN c.salary_disclosure_rate = 0                        THEN 1
                    WHEN c.salary_disclosure_rate <= 0.25                    THEN 2
                    WHEN c.salary_disclosure_rate <= 0.50                    THEN 3
                    WHEN c.salary_disclosure_rate <= 0.75                    THEN 4
                    WHEN c.salary_disclosure_rate < 1.0                      THEN 5
                    ELSE 6
                END
                """),
            "avgJobLifetimeDays" => (
                """
                CASE
                    WHEN c.avg_job_lifetime_days <= 2  THEN '1–2d'
                    WHEN c.avg_job_lifetime_days <= 4  THEN '3–4d'
                    WHEN c.avg_job_lifetime_days <= 6  THEN '5–6d'
                    WHEN c.avg_job_lifetime_days <= 8  THEN '7–8d'
                    WHEN c.avg_job_lifetime_days <= 10 THEN '9–10d'
                    WHEN c.avg_job_lifetime_days <= 12 THEN '11–12d'
                    WHEN c.avg_job_lifetime_days <= 14 THEN '13–14d'
                    WHEN c.avg_job_lifetime_days <= 16 THEN '15–16d'
                    WHEN c.avg_job_lifetime_days <= 18 THEN '17–18d'
                    WHEN c.avg_job_lifetime_days <= 20 THEN '19–20d'
                    WHEN c.avg_job_lifetime_days <= 22 THEN '21–22d'
                    WHEN c.avg_job_lifetime_days <= 24 THEN '23–24d'
                    WHEN c.avg_job_lifetime_days <= 26 THEN '25–26d'
                    WHEN c.avg_job_lifetime_days <= 28 THEN '27–28d'
                    WHEN c.avg_job_lifetime_days <= 30 THEN '29–30d'
                    WHEN c.avg_job_lifetime_days <= 45 THEN '31–45d'
                    WHEN c.avg_job_lifetime_days <= 60 THEN '46–60d'
                    ELSE '60+d'
                END
                """,
                """
                CASE
                    WHEN c.avg_job_lifetime_days <= 2  THEN 1
                    WHEN c.avg_job_lifetime_days <= 4  THEN 2
                    WHEN c.avg_job_lifetime_days <= 6  THEN 3
                    WHEN c.avg_job_lifetime_days <= 8  THEN 4
                    WHEN c.avg_job_lifetime_days <= 10 THEN 5
                    WHEN c.avg_job_lifetime_days <= 12 THEN 6
                    WHEN c.avg_job_lifetime_days <= 14 THEN 7
                    WHEN c.avg_job_lifetime_days <= 16 THEN 8
                    WHEN c.avg_job_lifetime_days <= 18 THEN 9
                    WHEN c.avg_job_lifetime_days <= 20 THEN 10
                    WHEN c.avg_job_lifetime_days <= 22 THEN 11
                    WHEN c.avg_job_lifetime_days <= 24 THEN 12
                    WHEN c.avg_job_lifetime_days <= 26 THEN 13
                    WHEN c.avg_job_lifetime_days <= 28 THEN 14
                    WHEN c.avg_job_lifetime_days <= 30 THEN 15
                    WHEN c.avg_job_lifetime_days <= 45 THEN 16
                    WHEN c.avg_job_lifetime_days <= 60 THEN 17
                    ELSE 18
                END
                """),
            "duplicateJobs" => (
                """
                CASE
                    WHEN c.duplicate_job_count = 0               THEN '0'
                    WHEN c.duplicate_job_count BETWEEN 1 AND 5   THEN '1–5'
                    WHEN c.duplicate_job_count BETWEEN 6 AND 15  THEN '6–15'
                    WHEN c.duplicate_job_count BETWEEN 16 AND 30 THEN '16–30'
                    ELSE '30+'
                END
                """,
                """
                CASE
                    WHEN c.duplicate_job_count = 0               THEN 1
                    WHEN c.duplicate_job_count BETWEEN 1 AND 5   THEN 2
                    WHEN c.duplicate_job_count BETWEEN 6 AND 15  THEN 3
                    WHEN c.duplicate_job_count BETWEEN 16 AND 30 THEN 4
                    ELSE 5
                END
                """),
            _ => ( // activeJobs (default)
                """
                CASE
                    WHEN c.active_job_count BETWEEN 1 AND 5    THEN '1–5'
                    WHEN c.active_job_count BETWEEN 6 AND 15   THEN '6–15'
                    WHEN c.active_job_count BETWEEN 16 AND 30  THEN '16–30'
                    WHEN c.active_job_count BETWEEN 31 AND 50  THEN '31–50'
                    WHEN c.active_job_count BETWEEN 51 AND 100 THEN '51–100'
                    WHEN c.active_job_count BETWEEN 101 AND 250 THEN '101–250'
                    WHEN c.active_job_count BETWEEN 251 AND 500 THEN '251–500'
                    ELSE '500+'
                END
                """,
                """
                CASE
                    WHEN c.active_job_count BETWEEN 1 AND 5    THEN 1
                    WHEN c.active_job_count BETWEEN 6 AND 15   THEN 2
                    WHEN c.active_job_count BETWEEN 16 AND 30  THEN 3
                    WHEN c.active_job_count BETWEEN 31 AND 50  THEN 4
                    WHEN c.active_job_count BETWEEN 51 AND 100 THEN 5
                    WHEN c.active_job_count BETWEEN 101 AND 250 THEN 6
                    WHEN c.active_job_count BETWEEN 251 AND 500 THEN 7
                    ELSE 8
                END
                """)
        };

        var nullGuard = metric switch
        {
            "salaryDisclosureRate" => "AND c.salary_disclosure_rate IS NOT NULL",
            "avgJobLifetimeDays"   => "AND c.avg_job_lifetime_days IS NOT NULL",
            _                      => ""
        };

        var sql = $"""
            SELECT bucket, COUNT(*)::int AS count
            FROM (
                SELECT {caseExpr} AS bucket,
                       {sortExpr} AS sort_key
                FROM companies c
                WHERE c.is_tech_hiring IS DISTINCT FROM FALSE
                  AND c.active_job_count > 0
                  {nullGuard}
                  {sizeFilter}
                  {usFilter}
            ) sub
            WHERE bucket IS NOT NULL
            GROUP BY bucket, sort_key
            ORDER BY sort_key
            """;

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (sizes is { Length: > 0 })
            cmd.Parameters.AddWithValue("sizes", sizes);

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new { bucket = reader.GetString(0), count = reader.GetInt32(1) });

        return Ok(rows);
    }

    [HttpGet("bucket-companies")]
    public async Task<IActionResult> GetBucketCompanies(
        [FromQuery] string metric = "activeJobs",
        [FromQuery] string bucket = "",
        [FromQuery] string[]? sizes = null,
        [FromQuery] bool? isUs = null,
        CancellationToken ct = default)
    {
        var sizeFilter = sizes is { Length: > 0 }
            ? "AND c.employee_count_range = ANY(@sizes)"
            : "";

        var usFilter = isUs == true
            ? "AND c.id IN (SELECT DISTINCT company_id FROM job_postings WHERE is_us_posting = TRUE AND is_active = TRUE)"
            : "";

        var caseExpr = metric switch
        {
            "remoteJobs" => """
                CASE
                    WHEN c.remote_job_count = 0               THEN '0'
                    WHEN c.remote_job_count BETWEEN 1 AND 5   THEN '1–5'
                    WHEN c.remote_job_count BETWEEN 6 AND 15  THEN '6–15'
                    WHEN c.remote_job_count BETWEEN 16 AND 30 THEN '16–30'
                    WHEN c.remote_job_count BETWEEN 31 AND 50 THEN '31–50'
                    ELSE '51+'
                END
                """,
            "salaryDisclosureRate" => """
                CASE
                    WHEN c.salary_disclosure_rate = 0    THEN '0%'
                    WHEN c.salary_disclosure_rate <= 0.25 THEN '1–25%'
                    WHEN c.salary_disclosure_rate <= 0.50 THEN '26–50%'
                    WHEN c.salary_disclosure_rate <= 0.75 THEN '51–75%'
                    WHEN c.salary_disclosure_rate < 1.0  THEN '76–99%'
                    ELSE '100%'
                END
                """,
            "avgJobLifetimeDays" => """
                CASE
                    WHEN c.avg_job_lifetime_days <= 2  THEN '1–2d'
                    WHEN c.avg_job_lifetime_days <= 4  THEN '3–4d'
                    WHEN c.avg_job_lifetime_days <= 6  THEN '5–6d'
                    WHEN c.avg_job_lifetime_days <= 8  THEN '7–8d'
                    WHEN c.avg_job_lifetime_days <= 10 THEN '9–10d'
                    WHEN c.avg_job_lifetime_days <= 12 THEN '11–12d'
                    WHEN c.avg_job_lifetime_days <= 14 THEN '13–14d'
                    WHEN c.avg_job_lifetime_days <= 16 THEN '15–16d'
                    WHEN c.avg_job_lifetime_days <= 18 THEN '17–18d'
                    WHEN c.avg_job_lifetime_days <= 20 THEN '19–20d'
                    WHEN c.avg_job_lifetime_days <= 22 THEN '21–22d'
                    WHEN c.avg_job_lifetime_days <= 24 THEN '23–24d'
                    WHEN c.avg_job_lifetime_days <= 26 THEN '25–26d'
                    WHEN c.avg_job_lifetime_days <= 28 THEN '27–28d'
                    WHEN c.avg_job_lifetime_days <= 30 THEN '29–30d'
                    WHEN c.avg_job_lifetime_days <= 45 THEN '31–45d'
                    WHEN c.avg_job_lifetime_days <= 60 THEN '46–60d'
                    ELSE '60+d'
                END
                """,
            "duplicateJobs" => """
                CASE
                    WHEN c.duplicate_job_count = 0               THEN '0'
                    WHEN c.duplicate_job_count BETWEEN 1 AND 5   THEN '1–5'
                    WHEN c.duplicate_job_count BETWEEN 6 AND 15  THEN '6–15'
                    WHEN c.duplicate_job_count BETWEEN 16 AND 30 THEN '16–30'
                    ELSE '30+'
                END
                """,
            _ => """
                CASE
                    WHEN c.active_job_count BETWEEN 1 AND 5     THEN '1–5'
                    WHEN c.active_job_count BETWEEN 6 AND 15    THEN '6–15'
                    WHEN c.active_job_count BETWEEN 16 AND 30   THEN '16–30'
                    WHEN c.active_job_count BETWEEN 31 AND 50   THEN '31–50'
                    WHEN c.active_job_count BETWEEN 51 AND 100  THEN '51–100'
                    WHEN c.active_job_count BETWEEN 101 AND 250 THEN '101–250'
                    WHEN c.active_job_count BETWEEN 251 AND 500 THEN '251–500'
                    ELSE '500+'
                END
                """
        };

        var nullGuard = metric switch
        {
            "salaryDisclosureRate" => "AND c.salary_disclosure_rate IS NOT NULL",
            "avgJobLifetimeDays"   => "AND c.avg_job_lifetime_days IS NOT NULL",
            _                      => ""
        };

        var sql = $"""
            SELECT c.id, c.canonical_name, c.logo_url, c.industry,
                   c.active_job_count, c.employee_count_range,
                   c.headquarters_city, c.headquarters_country
            FROM companies c
            WHERE c.is_tech_hiring IS DISTINCT FROM FALSE
              AND c.active_job_count > 0
              {nullGuard}
              {sizeFilter}
              {usFilter}
              AND ({caseExpr}) = @bucket
            ORDER BY c.active_job_count DESC
            """;

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("bucket", bucket);
        if (sizes is { Length: > 0 })
            cmd.Parameters.AddWithValue("sizes", sizes);

        var companies = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            companies.Add(new
            {
                id = reader.GetInt32(0),
                canonicalName = reader.GetString(1),
                logoUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                industry = reader.IsDBNull(3) ? null : reader.GetString(3),
                activeJobCount = reader.GetInt32(4),
                employeeCountRange = reader.IsDBNull(5) ? null : reader.GetString(5),
                headquartersCity = reader.IsDBNull(6) ? null : reader.GetString(6),
                headquartersCountry = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }

        return Ok(new { bucket, companies });
    }
}
