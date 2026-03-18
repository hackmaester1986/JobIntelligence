namespace JobIntelligence.Core.Entities;

public class JobSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public short? RateLimitRps { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CollectionRun> CollectionRuns { get; set; } = new List<CollectionRun>();
    public ICollection<JobPosting> JobPostings { get; set; } = new List<JobPosting>();
}
