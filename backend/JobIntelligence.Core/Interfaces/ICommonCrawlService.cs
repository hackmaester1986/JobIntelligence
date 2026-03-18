namespace JobIntelligence.Core.Interfaces;

public interface ICommonCrawlService
{
    Task<CommonCrawlResult> DiscoverSlugsAsync(CancellationToken ct = default);
}

public record CommonCrawlResult(
    List<string> GreenhouseSlugs,
    List<string> LeverSlugs,
    List<string> AshbySlugs,
    List<string> WorkableSlugs,
    List<string> SmartRecruitersSlugs,
    List<string> BambooHrSlugs);
