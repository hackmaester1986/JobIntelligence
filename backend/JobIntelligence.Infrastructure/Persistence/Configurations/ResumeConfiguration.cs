using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("resumes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.RawText).HasColumnName("raw_text").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name");
        builder.Property(x => x.Email).HasColumnName("email");
        builder.Property(x => x.Location).HasColumnName("location");
        builder.Property(x => x.YearsOfExperience).HasColumnName("years_of_experience");
        builder.Property(x => x.EducationLevel).HasColumnName("education_level");
        builder.Property(x => x.EducationField).HasColumnName("education_field");
        builder.Property(x => x.Skills).HasColumnName("skills").HasColumnType("jsonb");
        builder.Property(x => x.RecentJobTitles).HasColumnName("recent_job_titles").HasColumnType("jsonb");
        builder.Property(x => x.Industries).HasColumnName("industries").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
    }
}
