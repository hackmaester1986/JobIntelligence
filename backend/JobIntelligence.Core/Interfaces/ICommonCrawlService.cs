namespace JobIntelligence.Core.Interfaces;

public interface ICommonCrawlService
{
    Task<CommonCrawlResult> DiscoverSlugsAsync(string? source = null, CancellationToken ct = default);
}

public record WorkdayEntry(string Host, string CareerSite);

public record CommonCrawlResult(
    List<string> GreenhouseSlugs,
    List<string> LeverSlugs,
    List<string> AshbySlugs,
    List<string> SmartRecruitersSlugs,
    List<WorkdayEntry> WorkdayEntries);
