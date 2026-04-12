namespace JobIntelligence.Core.Entities;

public class PageVisit
{
    public long Id { get; set; }
    public string IpAddress { get; set; } = null!;
    public string Path { get; set; } = null!;
    public string? UserAgent { get; set; }
    public DateTime VisitedAt { get; set; }
}
