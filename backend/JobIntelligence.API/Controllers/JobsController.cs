using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController(ApplicationDbContext db, IMemoryCache cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? q,
        [FromQuery] string? skill,
        [FromQuery] string? source,
        [FromQuery] long? companyId,
        [FromQuery] string? seniority,
        [FromQuery] bool? isRemote,
        [FromQuery] bool? isUs,
        [FromQuery] string? authenticityLabel,
        [FromQuery] string[]? industries,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] int radiusMiles = 25,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        // Proximity param validation
        if (lat.HasValue != lng.HasValue)
            return BadRequest(new { error = "Both lat and lng must be provided together." });
        if (lat.HasValue && (lat < -90 || lat > 90 || lng < -180 || lng > 180))
            return BadRequest(new { error = "lat must be in [-90,90] and lng in [-180,180]." });
        if (radiusMiles > 200) radiusMiles = 200;

        var proximityMode = lat.HasValue && lng.HasValue;

        var query = db.JobPostings
            .Where(j => j.IsActive && j.Company.IsTechHiring != false)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(j => EF.Functions.ILike(j.Title, $"%{q}%"));

        if (!string.IsNullOrEmpty(skill))
            query = query.Where(j => j.Skills.Any(s => EF.Functions.ILike(s.Skill.CanonicalName, $"%{skill}%")));

        if (industries is { Length: > 0 })
            query = query.Where(j => industries.Contains(j.Company.Industry));

        if (!string.IsNullOrEmpty(source))
            query = query.Where(j => j.Source.Name == source);

        if (companyId.HasValue)
            query = query.Where(j => j.CompanyId == companyId.Value);

        if (!string.IsNullOrEmpty(seniority))
            query = query.Where(j => j.SeniorityLevel == seniority);

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        // isUs=true → US + unknown; isUs=false → international + unknown; null → all
        if (isUs == true)
            query = query.Where(j => j.IsUsPosting == true || j.IsUsPosting == null);
        else if (isUs == false)
            query = query.Where(j => j.IsUsPosting == false || j.IsUsPosting == null);

        if (!string.IsNullOrEmpty(authenticityLabel))
            query = query.Where(j => j.AuthenticityLabel == authenticityLabel);

        if (proximityMode)
        {
            // Proximity path: filter to geocoded jobs within radius, order by distance
            query = query.Where(j => j.Latitude != null);

            var userLat = lat!.Value;
            var userLng = lng!.Value;
            var radius = (double)radiusMiles;

            // Haversine distance in miles using EF.Functions raw SQL expression
            var jobsWithDistance = await query
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
                    j.IsRemoteInDescription,
                    j.SalaryMin,
                    j.SalaryMax,
                    j.SalaryCurrency,
                    j.ApplyUrl,
                    j.PostedAt,
                    j.FirstSeenAt,
                    j.AuthenticityScore,
                    j.AuthenticityLabel,
                    j.Latitude,
                    j.Longitude,
                    Company = new { j.Company.Id, j.Company.CanonicalName, j.Company.LogoUrl, j.Company.Industry },
                    Source = new { j.Source.Name }
                })
                .ToListAsync(ct);

            var filtered = jobsWithDistance
                .Select(j => new
                {
                    j.Id, j.Title, j.Department, j.SeniorityLevel, j.EmploymentType,
                    j.LocationRaw, j.IsRemote, j.IsHybrid, j.IsRemoteInDescription,
                    j.SalaryMin, j.SalaryMax, j.SalaryCurrency, j.ApplyUrl,
                    j.PostedAt, j.FirstSeenAt, j.AuthenticityScore, j.AuthenticityLabel,
                    j.Company, j.Source,
                    DistanceMiles = j.Latitude.HasValue
                        ? HaversineMiles(userLat, userLng, j.Latitude.Value, j.Longitude!.Value)
                        : double.MaxValue
                })
                .Where(j => j.DistanceMiles <= radius)
                .OrderBy(j => j.DistanceMiles)
                .ToList();

            var total = filtered.Count;
            var paged = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(j => new
                {
                    j.Id, j.Title, j.Department, j.SeniorityLevel, j.EmploymentType,
                    j.LocationRaw, j.IsRemote, j.IsHybrid, j.IsRemoteInDescription,
                    j.SalaryMin, j.SalaryMax, j.SalaryCurrency, j.ApplyUrl,
                    j.PostedAt, j.FirstSeenAt, j.AuthenticityScore, j.AuthenticityLabel,
                    j.Company, j.Source,
                    DistanceMiles = Math.Round(j.DistanceMiles, 1)
                })
                .ToList();

            return Ok(new { total, page, pageSize, proximityMode = true, data = paged });
        }

        var cacheKey = $"jobs:count:{q}:{skill}:{source}:{companyId}:{seniority}:{isRemote}:{isUs}:{authenticityLabel}:{string.Join(",", industries ?? [])}";
        if (!cache.TryGetValue(cacheKey, out int jobTotal))
        {
            jobTotal = await query.CountAsync(ct);
            cache.Set(cacheKey, jobTotal, TimeSpan.FromMinutes(5));
        }

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
                j.IsRemoteInDescription,
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

        return Ok(new { total = jobTotal, page, pageSize, proximityMode = false, data = jobs });
    }

    private static double HaversineMiles(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 3958.8;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;

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
