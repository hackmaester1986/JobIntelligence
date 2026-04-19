namespace JobIntelligence.Core.Interfaces;

public interface IJobEmbeddingService
{
    Task<EmbeddingResult> EmbedJobsAsync(int batchSize = 100, CancellationToken ct = default);
    Task EmbedNewPostingsAsync(IEnumerable<long> jobPostingIds, CancellationToken ct = default);
}

public record EmbeddingResult(int Processed, int Embedded, int Skipped, int Failed);
