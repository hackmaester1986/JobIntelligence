namespace JobIntelligence.Core.Interfaces;

public interface IDescriptionEnrichmentService
{
    Task<DescriptionEnrichmentResult> EnrichCompaniesAsync(int batchSize = 50, CancellationToken ct = default);
}

public record DescriptionEnrichmentResult(int Processed, int Enriched, int NotFound, int Failed);
