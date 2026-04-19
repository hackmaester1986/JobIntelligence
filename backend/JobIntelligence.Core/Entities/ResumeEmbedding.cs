using Pgvector;

namespace JobIntelligence.Core.Entities;

public class ResumeEmbedding
{
    public long ResumeId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string EmbeddingText { get; set; } = string.Empty;
    public DateTime EmbeddedAt { get; set; } = DateTime.UtcNow;

    public Resume Resume { get; set; } = null!;
}
