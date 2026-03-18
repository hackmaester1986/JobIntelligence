using JobIntelligence.Core.Entities;

namespace JobIntelligence.Core.Interfaces;

public interface IJobCollector
{
    string SourceName { get; }
    Task<CollectionResult> CollectAsync(Company company, CancellationToken ct = default);
}

public record CollectionResult(
    string CompanyName,
    int Fetched,
    int New,
    int Updated,
    int Removed,
    string? Error = null
);
