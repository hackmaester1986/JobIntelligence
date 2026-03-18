namespace JobIntelligence.Core.Entities;

public class JobSkill
{
    public long Id { get; set; }
    public long JobPostingId { get; set; }
    public int SkillId { get; set; }
    public bool IsRequired { get; set; } = true;
    public string? ExtractionMethod { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public JobPosting JobPosting { get; set; } = null!;
    public SkillTaxonomy Skill { get; set; } = null!;
}
