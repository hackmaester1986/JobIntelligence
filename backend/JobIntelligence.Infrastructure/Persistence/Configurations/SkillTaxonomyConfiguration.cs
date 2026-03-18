using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class SkillTaxonomyConfiguration : IEntityTypeConfiguration<SkillTaxonomy>
{
    public void Configure(EntityTypeBuilder<SkillTaxonomy> builder)
    {
        builder.ToTable("skill_taxonomy");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.CanonicalName).HasColumnName("canonical_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        builder.Property(x => x.Aliases).HasColumnName("aliases").HasColumnType("jsonb").HasDefaultValueSql("'[]'");
        builder.Property(x => x.ParentSkillId).HasColumnName("parent_skill_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.CanonicalName).IsUnique();

        builder.HasOne(x => x.ParentSkill)
            .WithMany(x => x.ChildSkills)
            .HasForeignKey(x => x.ParentSkillId)
            .IsRequired(false);
    }
}
