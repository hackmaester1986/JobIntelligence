namespace JobIntelligence.Core.Entities;

public class Resume
{
    public long Id { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Location { get; set; }
    public int? YearsOfExperience { get; set; }
    public string? EducationLevel { get; set; }   // bachelor, master, phd, associate
    public string? EducationField { get; set; }   // computer science, electrical engineering, etc.
    public string[] Skills { get; set; } = [];
    public string[] RecentJobTitles { get; set; } = [];
    public string[] Industries { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ResumeEmbedding? Embedding { get; set; }
}
