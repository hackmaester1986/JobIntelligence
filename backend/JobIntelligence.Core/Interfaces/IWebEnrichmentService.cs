namespace JobIntelligence.Core.Interfaces;

public interface IWebEnrichmentService
{
    Task<WebEnrichmentResult> EnrichCompaniesAsync(int batchSize = 20, CancellationToken ct = default);
}

public record WebEnrichmentResult(int Processed, int Enriched, int NotFound, int Failed);
