using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("industries")]
    public async Task<IActionResult> GetIndustries(CancellationToken ct)
    {
        var industries = await db.Companies
            .Where(c => c.Industry != null && c.IsTechHiring != false)
            .Select(c => c.Industry!)
            .Distinct()
            .OrderBy(i => i)
            .ToListAsync(ct);
        return Ok(industries);
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanies(
        [FromQuery] string? q,
        [FromQuery] string[]? industries,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = db.Companies.Where(c => c.IsTechHiring != false).AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(c => EF.Functions.ILike(c.CanonicalName, $"%{q}%"));

        if (industries is { Length: > 0 })
            query = query.Where(c => industries.Contains(c.Industry));

        var total = await query.CountAsync(ct);

        var companies = await query
            .OrderByDescending(c => c.ActiveJobCount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.CanonicalName,
                c.Industry,
                c.EmployeeCountRange,
                c.HeadquartersCity,
                c.HeadquartersCountry,
                c.LogoUrl,
                c.ActiveJobCount,
                c.RemovedJobCount,
                c.RemoteJobCount,
                c.TotalJobsEverSeen,
                c.DuplicateJobCount,
                c.AvgJobLifetimeDays,
                c.AvgRepostCount,
                c.SalaryDisclosureRate,
                c.StatsComputedAt
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, data = companies });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetCompany(long id, CancellationToken ct)
    {
        var company = await db.Companies
            .Where(c => c.Id == id)
            .Select(c => new {
                c.Id, c.CanonicalName, c.Industry, c.EmployeeCountRange,
                c.HeadquartersCity, c.HeadquartersCountry, c.LogoUrl,
                c.ActiveJobCount, c.RemovedJobCount, c.RemoteJobCount, c.TotalJobsEverSeen,
                c.DuplicateJobCount, c.AvgJobLifetimeDays, c.AvgRepostCount,
                c.SalaryDisclosureRate, c.StatsComputedAt
            })
            .FirstOrDefaultAsync(ct);
        if (company == null) return NotFound();
        return Ok(company);
    }

    [HttpGet("{id:long}/snapshots")]
    public async Task<IActionResult> GetSnapshots(long id, [FromQuery] string range = "1w", CancellationToken ct = default)
    {
        var cutoff = range switch {
            "1m" => DateTime.UtcNow.AddMonths(-1),
            "3m" => DateTime.UtcNow.AddMonths(-3),
            "6m" => DateTime.UtcNow.AddMonths(-6),
            _    => DateTime.UtcNow.AddDays(-7),
        };

        var trunc = range is "3m" or "6m" ? "DATE_TRUNC('week', snapshot_at)" : "DATE(snapshot_at)";

        var sql = $"""
            SELECT
                {trunc} AS date,
                (ARRAY_AGG(active_job_count ORDER BY snapshot_at DESC))[1] AS active_jobs,
                SUM(new_count)     AS added,
                SUM(removed_count) AS removed
            FROM company_job_snapshots
            WHERE company_id = @companyId
              AND snapshot_at >= @cutoff
              AND snapshot_at > (
                  SELECT MIN(snapshot_at) FROM company_job_snapshots WHERE company_id = @companyId
              )
            GROUP BY {trunc}
            ORDER BY 1
            """;

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("companyId", id);
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new
            {
                date       = reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                activeJobs = reader.GetInt32(1),   // ARRAY_AGG preserves int4
                added      = reader.GetInt64(2),   // SUM returns int8
                removed    = reader.GetInt64(3),
            });
        }

        return Ok(rows);
    }

    [HttpGet("{id:long}/jobs")]
    public async Task<IActionResult> GetCompanyJobs(long id, [FromQuery] bool? isUs, CancellationToken ct)
    {
        var query = db.JobPostings
            .Include(j => j.Source)
            .Where(j => j.CompanyId == id && j.IsActive);

        if (isUs == true)
            query = query.Where(j => j.IsUsPosting == true || j.IsUsPosting == null);

        var jobs = await query
            .OrderByDescending(j => j.FirstSeenAt)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Department,
                j.SeniorityLevel,
                j.LocationRaw,
                j.IsRemote,
                j.FirstSeenAt,
                j.AuthenticityScore,
                j.AuthenticityLabel,
                Source = new { j.Source.Name }
            })
            .ToListAsync(ct);

        return Ok(jobs);
    }
}
