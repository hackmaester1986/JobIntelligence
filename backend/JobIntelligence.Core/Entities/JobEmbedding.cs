using Pgvector;

namespace JobIntelligence.Core.Entities;

public class JobEmbedding
{
    public long JobPostingId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string EmbeddingText { get; set; } = string.Empty;
    public DateTime EmbeddedAt { get; set; } = DateTime.UtcNow;

    public JobPosting JobPosting { get; set; } = null!;
}
