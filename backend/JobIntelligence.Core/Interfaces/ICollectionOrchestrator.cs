namespace JobIntelligence.Core.Interfaces;

public interface ICollectionOrchestrator
{
    Task RunAsync(string? sourceName = null, CancellationToken ct = default);
}
