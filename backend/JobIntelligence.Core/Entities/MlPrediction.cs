using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class MlPrediction
{
    public long Id { get; set; }
    public long JobPostingId { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public DateTime PredictedAt { get; set; } = DateTime.UtcNow;
    public decimal AuthenticityScore { get; set; }
    public string Label { get; set; } = string.Empty;
    public JsonDocument Features { get; set; } = JsonDocument.Parse("{}");
    public string? EndpointName { get; set; }

    public JobPosting JobPosting { get; set; } = null!;
}
