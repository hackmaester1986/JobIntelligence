using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("job_postings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(200);
        builder.Property(x => x.Team).HasColumnName("team").HasMaxLength(200);
        builder.Property(x => x.SeniorityLevel).HasColumnName("seniority_level").HasMaxLength(50);
        builder.Property(x => x.EmploymentType).HasColumnName("employment_type").HasMaxLength(50);

        builder.Property(x => x.LocationRaw).HasColumnName("location_raw").HasMaxLength(500);
        builder.Property(x => x.LocationCity).HasColumnName("location_city").HasMaxLength(100);
        builder.Property(x => x.LocationState).HasColumnName("location_state").HasMaxLength(100);
        builder.Property(x => x.LocationCountry).HasColumnName("location_country").HasMaxLength(100);
        builder.Property(x => x.IsRemote).HasColumnName("is_remote").HasDefaultValue(false);
        builder.Property(x => x.IsHybrid).HasColumnName("is_hybrid").HasDefaultValue(false);
        builder.Property(x => x.IsUsPosting).HasColumnName("is_us_posting");
        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");

        builder.Property(x => x.SalaryMin).HasColumnName("salary_min").HasPrecision(12, 2);
        builder.Property(x => x.SalaryMax).HasColumnName("salary_max").HasPrecision(12, 2);
        builder.Property(x => x.SalaryCurrency).HasColumnName("salary_currency").HasMaxLength(10);
        builder.Property(x => x.SalaryPeriod).HasColumnName("salary_period").HasMaxLength(20);
        builder.Property(x => x.SalaryDisclosed).HasColumnName("salary_disclosed").HasDefaultValue(false);

        builder.Property(x => x.Description).HasColumnName("description");
        builder.Property(x => x.DescriptionHtml).HasColumnName("description_html");
        builder.Property(x => x.DescriptionHash).HasColumnName("description_hash").HasMaxLength(64);
        builder.Property(x => x.ApplyUrl).HasColumnName("apply_url").HasMaxLength(1000);
        builder.Property(x => x.ApplyUrlDomain).HasColumnName("apply_url_domain").HasMaxLength(255);

        builder.Property(x => x.PostedAt).HasColumnName("posted_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.RemovedAt).HasColumnName("removed_at");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.RepostCount).HasColumnName("repost_count").HasDefaultValue((short)0);
        builder.Property(x => x.PreviousPostingId).HasColumnName("previous_posting_id");

        builder.Property(x => x.AuthenticityScore).HasColumnName("authenticity_score").HasPrecision(5, 4);
        builder.Property(x => x.AuthenticityLabel).HasColumnName("authenticity_label").HasMaxLength(20);
        builder.Property(x => x.AuthenticityScoreAt).HasColumnName("authenticity_scored_at");
        builder.Property(x => x.SageMakerModelVersion).HasColumnName("sagemaker_model_version").HasMaxLength(50);

        builder.Property(x => x.RawData).HasColumnName("raw_data").HasColumnType("jsonb");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.PostedAt });
        builder.HasIndex(x => new { x.IsActive, x.PostedAt });
        builder.HasIndex(x => new { x.SeniorityLevel, x.PostedAt });
        builder.HasIndex(x => new { x.IsRemote, x.PostedAt });
        builder.HasIndex(x => new { x.AuthenticityLabel, x.PostedAt });
        builder.HasIndex(x => new { x.CompanyId, x.DescriptionHash });
        builder.HasIndex(x => x.PreviousPostingId);
        builder.HasIndex(x => new { x.Latitude, x.Longitude })
            .HasFilter("latitude IS NOT NULL")
            .HasDatabaseName("IX_job_postings_geo");

        // Partial indexes for dashboard stats query (WHERE is_active = true)
        // Covers counts/seniority/departments CTEs that all filter by company_id + is_active
        builder.HasIndex(x => x.CompanyId)
            .IncludeProperties(x => new { x.IsRemote, x.IsHybrid, x.FirstSeenAt })
            .HasFilter("is_active = true")
            .HasDatabaseName("IX_job_postings_active_company_id");

        // Covers the departments CTE GROUP BY
        builder.HasIndex(x => x.Department)
            .HasFilter("is_active = true AND department IS NOT NULL")
            .HasDatabaseName("IX_job_postings_active_department");

        // Covers the seniority CTE GROUP BY
        builder.HasIndex(x => x.SeniorityLevel)
            .HasFilter("is_active = true AND seniority_level IS NOT NULL")
            .HasDatabaseName("IX_job_postings_active_seniority");

        builder.HasOne(x => x.Source)
            .WithMany(x => x.JobPostings)
            .HasForeignKey(x => x.SourceId);

        builder.HasOne(x => x.Company)
            .WithMany(x => x.JobPostings)
            .HasForeignKey(x => x.CompanyId);
    }
}
