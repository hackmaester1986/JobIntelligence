using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class CompanyJobSnapshotConfiguration : IEntityTypeConfiguration<CompanyJobSnapshot>
{
    public void Configure(EntityTypeBuilder<CompanyJobSnapshot> builder)
    {
        builder.ToTable("company_job_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CollectionRunId).HasColumnName("collection_run_id").IsRequired();
        builder.Property(x => x.SnapshotAt).HasColumnName("snapshot_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.ActiveJobCount).HasColumnName("active_job_count");
        builder.Property(x => x.NewCount).HasColumnName("new_count");
        builder.Property(x => x.RemovedCount).HasColumnName("removed_count");

        builder.HasIndex(x => new { x.CompanyId, x.SnapshotAt });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId);

        builder.HasOne(x => x.CollectionRun)
            .WithMany()
            .HasForeignKey(x => x.CollectionRunId);
    }
}
