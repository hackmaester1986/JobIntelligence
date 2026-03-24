namespace JobIntelligence.Core.Interfaces;

public interface IWikidataEnrichmentService
{
    Task<WikidataEnrichmentResult> EnrichCompaniesAsync(int batchSize = 100, CancellationToken ct = default);
}

public record WikidataEnrichmentResult(int Processed, int Enriched, int NotFound, int Failed);
