using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.Infrastructure.Services;

public class CompanyStatsService(ApplicationDbContext db) : ICompanyStatsService
{
    public async Task RefreshStatsAsync(long companyId, CancellationToken ct = default)
    {
        var company = await db.Companies.FindAsync([companyId], ct);
        if (company is null) return;

        var stats = await db.JobPostings
            .Where(j => j.CompanyId == companyId)
            .GroupBy(_ => 1)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                ActiveJobCount       = g.Count(j => j.IsActive),
                RemovedJobCount      = g.Count(j => !j.IsActive),
                RemoteJobCount       = g.Count(j => j.IsActive && j.IsRemote),
                TotalJobsEverSeen    = g.Count(),
                AvgJobLifetimeDays   = (double?)g.Where(j => j.RemovedAt != null)
                                         .Average(j => (double?)(j.LastSeenAt - j.FirstSeenAt).TotalDays),
                AvgRepostCount       = (double?)g.Where(j => j.IsActive)
                                         .Average(j => (double?)j.RepostCount),
                SalaryDisclosureRate = (double?)g.Where(j => j.IsActive)
                                         .Average(j => j.SalaryDisclosed ? 1.0 : 0.0),
            })
            .FirstOrDefaultAsync(ct);

        var groupCounts = await db.JobPostings
            .Where(j => j.CompanyId == companyId && j.DescriptionHash != null)
            .GroupBy(j => j.DescriptionHash!)
            .Select(g => g.Count())
            .ToListAsync(ct);
        var duplicateCount = groupCounts.Where(c => c > 1).Sum();

        company.ActiveJobCount       = stats?.ActiveJobCount       ?? 0;
        company.RemovedJobCount      = stats?.RemovedJobCount      ?? 0;
        company.RemoteJobCount       = stats?.RemoteJobCount       ?? 0;
        company.TotalJobsEverSeen    = stats?.TotalJobsEverSeen    ?? 0;
        company.DuplicateJobCount    = duplicateCount;
        company.AvgJobLifetimeDays   = stats?.AvgJobLifetimeDays;
        company.AvgRepostCount       = stats?.AvgRepostCount;
        company.SalaryDisclosureRate = stats?.SalaryDisclosureRate;
        company.StatsComputedAt      = DateTime.UtcNow;
        // No SaveChangesAsync — caller owns the transaction
    }
}
