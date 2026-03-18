using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class SkillTaxonomy
{
    public int Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public JsonDocument Aliases { get; set; } = JsonDocument.Parse("[]");
    public int? ParentSkillId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SkillTaxonomy? ParentSkill { get; set; }
    public ICollection<SkillTaxonomy> ChildSkills { get; set; } = new List<SkillTaxonomy>();
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
}
