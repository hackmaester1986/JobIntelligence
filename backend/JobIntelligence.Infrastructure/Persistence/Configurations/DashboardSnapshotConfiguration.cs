using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class DashboardSnapshotConfiguration : IEntityTypeConfiguration<DashboardSnapshot>
{
    public void Configure(EntityTypeBuilder<DashboardSnapshot> builder)
    {
        builder.ToTable("dashboard_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.SnapshotAt).HasColumnName("snapshot_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.IsUs).HasColumnName("is_us").IsRequired();
        builder.Property(x => x.TotalActiveJobs).HasColumnName("total_active_jobs");
        builder.Property(x => x.TotalCompanies).HasColumnName("total_companies");
        builder.Property(x => x.RemoteJobs).HasColumnName("remote_jobs");
        builder.Property(x => x.HybridJobs).HasColumnName("hybrid_jobs");
        builder.Property(x => x.OnsiteJobs).HasColumnName("onsite_jobs");
        builder.Property(x => x.ActiveToday).HasColumnName("active_today");
        builder.Property(x => x.TopCompanies).HasColumnName("top_companies").HasColumnType("jsonb");
        builder.Property(x => x.JobsBySeniority).HasColumnName("jobs_by_seniority").HasColumnType("jsonb");
        builder.Property(x => x.TopDepartments).HasColumnName("top_departments").HasColumnType("jsonb");

        builder.HasIndex(x => new { x.IsUs, x.SnapshotAt });
    }
}
