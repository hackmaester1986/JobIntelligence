namespace JobIntelligence.Core.Interfaces;

public interface ICompanyStatsService
{
    Task RefreshStatsAsync(long companyId, CancellationToken ct = default);
}
