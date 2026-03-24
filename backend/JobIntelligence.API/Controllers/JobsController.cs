using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? q,
        [FromQuery] string? source,
        [FromQuery] long? companyId,
        [FromQuery] string? seniority,
        [FromQuery] bool? isRemote,
        [FromQuery] string? authenticityLabel,
        [FromQuery] string? industry,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = db.JobPostings
            .Include(j => j.Company)
            .Include(j => j.Source)
            .Where(j => j.IsActive)
            .AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(j => EF.Functions.ILike(j.Title, $"%{q}%")
                || EF.Functions.ILike(j.Description ?? "", $"%{q}%")
                || EF.Functions.ILike(j.Company.CanonicalName, $"%{q}%"));

        if (!string.IsNullOrEmpty(industry))
            query = query.Where(j => EF.Functions.ILike(j.Company.Industry ?? "", $"%{industry}%"));

        if (!string.IsNullOrEmpty(source))
            query = query.Where(j => j.Source.Name == source);

        if (companyId.HasValue)
            query = query.Where(j => j.CompanyId == companyId.Value);

        if (!string.IsNullOrEmpty(seniority))
            query = query.Where(j => j.SeniorityLevel == seniority);

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        if (!string.IsNullOrEmpty(authenticityLabel))
            query = query.Where(j => j.AuthenticityLabel == authenticityLabel);

        var total = await query.CountAsync(ct);

        var jobs = await query
            .OrderByDescending(j => j.FirstSeenAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Department,
                j.SeniorityLevel,
                j.EmploymentType,
                j.LocationRaw,
                j.IsRemote,
                j.IsHybrid,
                j.SalaryMin,
                j.SalaryMax,
                j.SalaryCurrency,
                j.ApplyUrl,
                j.PostedAt,
                j.FirstSeenAt,
                j.AuthenticityScore,
                j.AuthenticityLabel,
                Company = new { j.Company.Id, j.Company.CanonicalName, j.Company.LogoUrl, j.Company.Industry },
                Source = new { j.Source.Name }
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, data = jobs });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetJob(long id, CancellationToken ct)
    {
        var job = await db.JobPostings
            .Include(j => j.Company)
            .Include(j => j.Source)
            .Include(j => j.Skills).ThenInclude(s => s.Skill)
            .Where(j => j.Id == id)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Department,
                j.Team,
                j.SeniorityLevel,
                j.EmploymentType,
                j.LocationRaw,
                j.LocationCity,
                j.LocationCountry,
                j.IsRemote,
                j.IsHybrid,
                j.SalaryMin,
                j.SalaryMax,
                j.SalaryCurrency,
                j.SalaryPeriod,
                j.SalaryDisclosed,
                j.Description,
                j.DescriptionHtml,
                j.ApplyUrl,
                j.PostedAt,
                j.FirstSeenAt,
                j.AuthenticityScore,
                j.AuthenticityLabel,
                Company = new { j.Company.Id, j.Company.CanonicalName, j.Company.LogoUrl, j.Company.Industry },
                Source = new { j.Source.Name },
                Skills = j.Skills.Select(s => s.Skill.CanonicalName)
            })
            .FirstOrDefaultAsync(ct);

        if (job == null) return NotFound();
        return Ok(job);
    }
}
