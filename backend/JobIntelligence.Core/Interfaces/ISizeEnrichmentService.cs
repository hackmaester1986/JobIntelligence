namespace JobIntelligence.Core.Interfaces;

public interface ISizeEnrichmentService
{
    Task<SizeEnrichmentResult> EnrichAsync(int batchSize = 50, CancellationToken ct = default);
}

public record SizeEnrichmentResult(int Processed, int Enriched, int NotFound, int Failed);
