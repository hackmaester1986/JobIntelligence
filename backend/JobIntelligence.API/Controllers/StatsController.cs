using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var totalActiveJobs = await db.JobPostings.CountAsync(j => j.IsActive, ct);
        var totalCompanies = await db.Companies.CountAsync(c => c.JobPostings.Any(j => j.IsActive), ct);
        var remoteJobs = await db.JobPostings.CountAsync(j => j.IsActive && j.IsRemote, ct);
        var hybridJobs = await db.JobPostings.CountAsync(j => j.IsActive && j.IsHybrid, ct);
        var since = DateTime.UtcNow.AddHours(-24);
        var activeToday = await db.JobPostings.CountAsync(j => j.IsActive && j.FirstSeenAt >= since, ct);

        var topCompanies = await db.Companies
            .Where(c => c.JobPostings.Any(j => j.IsActive))
            .Select(c => new { c.Id, Name = c.CanonicalName, c.LogoUrl,
                JobCount = c.JobPostings.Count(j => j.IsActive) })
            .OrderByDescending(x => x.JobCount).Take(10).ToListAsync(ct);

        var bySeniority = await db.JobPostings
            .Where(j => j.IsActive && j.SeniorityLevel != null)
            .GroupBy(j => j.SeniorityLevel!)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ToListAsync(ct);

        var topDepts = await db.JobPostings
            .Where(j => j.IsActive && j.Department != null)
            .GroupBy(j => j.Department!)
            .Select(g => new { Department = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(10).ToListAsync(ct);

        return Ok(new {
            totalActiveJobs, totalCompanies, remoteJobs, hybridJobs,
            onsiteJobs = totalActiveJobs - remoteJobs - hybridJobs,
            activeToday, topCompanies, jobsBySeniority = bySeniority,
            topDepartments = topDepts
        });
    }
}
