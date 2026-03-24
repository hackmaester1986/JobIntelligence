using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.CanonicalName).HasColumnName("canonical_name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(255);
        builder.Property(x => x.GreenhouseBoardToken).HasColumnName("greenhouse_board_token").HasMaxLength(200);
        builder.Property(x => x.LeverCompanySlug).HasColumnName("lever_company_slug").HasMaxLength(200);
        builder.Property(x => x.AshbyBoardSlug).HasColumnName("ashby_board_slug").HasMaxLength(200);
        builder.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(100);
        builder.Property(x => x.IsTechHiring).HasColumnName("is_tech_hiring");
        builder.Property(x => x.EmployeeCountRange).HasColumnName("employee_count_range").HasMaxLength(50);
        builder.Property(x => x.LinkedInUrl).HasColumnName("linkedin_url").HasMaxLength(500);
        builder.Property(x => x.LogoUrl).HasColumnName("logo_url").HasMaxLength(500);
        builder.Property(x => x.HeadquartersCity).HasColumnName("headquarters_city").HasMaxLength(100);
        builder.Property(x => x.HeadquartersCountry).HasColumnName("headquarters_country").HasMaxLength(100);
        builder.Property(x => x.WikidataId).HasColumnName("wikidata_id").HasMaxLength(20);
        builder.Property(x => x.WikidataEnrichedAt).HasColumnName("wikidata_enriched_at");
        builder.Property(x => x.DescriptionEnrichedAt).HasColumnName("description_enriched_at");
        builder.Property(x => x.FoundingYear).HasColumnName("founding_year");
        builder.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.ActiveJobCount).HasColumnName("active_job_count").HasDefaultValue(0);
        builder.Property(x => x.RemovedJobCount).HasColumnName("removed_job_count").HasDefaultValue(0);
        builder.Property(x => x.RemoteJobCount).HasColumnName("remote_job_count").HasDefaultValue(0);
        builder.Property(x => x.TotalJobsEverSeen).HasColumnName("total_jobs_ever_seen").HasDefaultValue(0);
        builder.Property(x => x.DuplicateJobCount).HasColumnName("duplicate_job_count").HasDefaultValue(0);
        builder.Property(x => x.AvgJobLifetimeDays).HasColumnName("avg_job_lifetime_days");
        builder.Property(x => x.AvgRepostCount).HasColumnName("avg_repost_count");
        builder.Property(x => x.SalaryDisclosureRate).HasColumnName("salary_disclosure_rate");
        builder.Property(x => x.StatsComputedAt).HasColumnName("stats_computed_at");

        builder.Property(x => x.WorkdayHost).HasColumnName("workday_host").HasMaxLength(200);
        builder.Property(x => x.WorkdayCareerSite).HasColumnName("workday_career_site").HasMaxLength(200);

        builder.HasIndex(x => x.NormalizedName).IsUnique();
        builder.HasIndex(x => x.GreenhouseBoardToken);
        builder.HasIndex(x => x.LeverCompanySlug);
        builder.HasIndex(x => x.AshbyBoardSlug);
        builder.HasIndex(x => x.WorkdayHost);
        builder.HasIndex(x => x.ActiveJobCount);
    }
}
