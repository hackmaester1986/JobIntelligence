using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class MlPredictionConfiguration : IEntityTypeConfiguration<MlPrediction>
{
    public void Configure(EntityTypeBuilder<MlPrediction> builder)
    {
        builder.ToTable("ml_predictions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.JobPostingId).HasColumnName("job_posting_id").IsRequired();
        builder.Property(x => x.ModelVersion).HasColumnName("model_version").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PredictedAt).HasColumnName("predicted_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.AuthenticityScore).HasColumnName("authenticity_score").HasPrecision(5, 4).IsRequired();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Features).HasColumnName("features").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.EndpointName).HasColumnName("endpoint_name").HasMaxLength(200);

        builder.HasIndex(x => new { x.JobPostingId, x.PredictedAt });

        builder.HasOne(x => x.JobPosting)
            .WithMany(x => x.MlPredictions)
            .HasForeignKey(x => x.JobPostingId);
    }
}
