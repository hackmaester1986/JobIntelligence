using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCompanies(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = db.Companies.AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(c => EF.Functions.ILike(c.CanonicalName, $"%{q}%"));

        var total = await query.CountAsync(ct);

        var companies = await query
            .OrderBy(c => c.CanonicalName)
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
                ActiveJobCount = c.JobPostings.Count(j => j.IsActive)
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
                ActiveJobCount = c.JobPostings.Count(j => j.IsActive)
            })
            .FirstOrDefaultAsync(ct);
        if (company == null) return NotFound();
        return Ok(company);
    }

    [HttpGet("{id:long}/jobs")]
    public async Task<IActionResult> GetCompanyJobs(long id, CancellationToken ct)
    {
        var jobs = await db.JobPostings
            .Include(j => j.Source)
            .Where(j => j.CompanyId == id && j.IsActive)
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
