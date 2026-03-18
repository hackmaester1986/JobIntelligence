using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class CollectionRun
{
    public long Id { get; set; }
    public int? SourceId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";
    public int JobsFetched { get; set; }
    public int JobsNew { get; set; }
    public int JobsUpdated { get; set; }
    public int JobsRemoved { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonDocument? Metadata { get; set; }

    public JobSource? Source { get; set; }
}
