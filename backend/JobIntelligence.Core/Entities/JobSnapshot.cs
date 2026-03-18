using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class JobSnapshot
{
    public long Id { get; set; }
    public long JobPostingId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
    public JsonDocument? ChangedFields { get; set; }
    public JsonDocument? RawData { get; set; }

    public JobPosting JobPosting { get; set; } = null!;
}
