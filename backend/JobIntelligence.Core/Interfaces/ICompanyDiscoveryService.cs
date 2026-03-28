namespace JobIntelligence.Core.Interfaces;

public interface ICompanyDiscoveryService
{
    Task<DiscoveryResult> DiscoverAndImportAsync(CancellationToken ct = default);
    Task<DiscoveryResult> DiscoverFromSlugsAsync(List<string> greenhouseSlugs, List<string> leverSlugs, List<string> ashbySlugs, List<string> smartRecruitersSlugs, List<WorkdayEntry> workdayEntries, bool dryRun = false, CancellationToken ct = default);
}

public record DiscoveryResult(int Added, int Skipped, int Failed,
    List<string> AddedCompanies, List<string> FailedCompanies,
    Dictionary<string, int> ValidatedPerSource,
    Dictionary<string, int> FailedPerSource);
