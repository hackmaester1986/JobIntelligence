using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class JobSkillConfiguration : IEntityTypeConfiguration<JobSkill>
{
    public void Configure(EntityTypeBuilder<JobSkill> builder)
    {
        builder.ToTable("job_skills");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.JobPostingId).HasColumnName("job_posting_id").IsRequired();
        builder.Property(x => x.SkillId).HasColumnName("skill_id").IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").HasDefaultValue(true);
        builder.Property(x => x.ExtractionMethod).HasColumnName("extraction_method").HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.JobPostingId);
        builder.HasIndex(x => new { x.SkillId, x.CreatedAt });
        builder.HasIndex(x => new { x.JobPostingId, x.SkillId }).IsUnique();

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Skills)
            .HasForeignKey(x => x.JobPostingId);

        builder.HasOne(x => x.Skill)
            .WithMany(x => x.JobSkills)
            .HasForeignKey(x => x.SkillId);
    }
}
