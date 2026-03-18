namespace JobIntelligence.Core.Entities;

public class CompanyJobSnapshot
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long CollectionRunId { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
    public int ActiveJobCount { get; set; }
    public int NewCount { get; set; }
    public int RemovedCount { get; set; }

    public Company Company { get; set; } = null!;
    public CollectionRun CollectionRun { get; set; } = null!;
}
