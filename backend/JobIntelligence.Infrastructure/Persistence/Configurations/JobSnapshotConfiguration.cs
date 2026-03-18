using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class JobSnapshotConfiguration : IEntityTypeConfiguration<JobSnapshot>
{
    public void Configure(EntityTypeBuilder<JobSnapshot> builder)
    {
        builder.ToTable("job_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.JobPostingId).HasColumnName("job_posting_id").IsRequired();
        builder.Property(x => x.SnapshotAt).HasColumnName("snapshot_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.ChangedFields).HasColumnName("changed_fields").HasColumnType("jsonb");
        builder.Property(x => x.RawData).HasColumnName("raw_data").HasColumnType("jsonb");

        builder.HasIndex(x => new { x.JobPostingId, x.SnapshotAt });

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.Snapshots)
            .HasForeignKey(x => x.JobPostingId);
    }
}
