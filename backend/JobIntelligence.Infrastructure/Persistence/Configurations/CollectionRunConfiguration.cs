using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class CollectionRunConfiguration : IEntityTypeConfiguration<CollectionRun>
{
    public void Configure(EntityTypeBuilder<CollectionRun> builder)
    {
        builder.ToTable("collection_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.SourceId).HasColumnName("source_id");
        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("running");
        builder.Property(x => x.JobsFetched).HasColumnName("jobs_fetched").HasDefaultValue(0);
        builder.Property(x => x.JobsNew).HasColumnName("jobs_new").HasDefaultValue(0);
        builder.Property(x => x.JobsUpdated).HasColumnName("jobs_updated").HasDefaultValue(0);
        builder.Property(x => x.JobsRemoved).HasColumnName("jobs_removed").HasDefaultValue(0);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

        builder.HasIndex(x => new { x.SourceId, x.StartedAt });

        builder.HasOne(x => x.Source)
            .WithMany(x => x.CollectionRuns)
            .HasForeignKey(x => x.SourceId)
            .IsRequired(false);
    }
}
