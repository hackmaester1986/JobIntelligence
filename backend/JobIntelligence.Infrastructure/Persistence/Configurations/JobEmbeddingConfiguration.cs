using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class JobEmbeddingConfiguration : IEntityTypeConfiguration<JobEmbedding>
{
    public void Configure(EntityTypeBuilder<JobEmbedding> builder)
    {
        builder.ToTable("job_embeddings");
        builder.HasKey(x => x.JobPostingId);
        builder.Property(x => x.JobPostingId).HasColumnName("job_posting_id");
        builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
        builder.Property(x => x.EmbeddingText).HasColumnName("embedding_text").IsRequired();
        builder.Property(x => x.EmbeddedAt).HasColumnName("embedded_at").HasDefaultValueSql("NOW()");

        builder.HasOne(x => x.JobPosting)
            .WithOne(x => x.Embedding)
            .HasForeignKey<JobEmbedding>(x => x.JobPostingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
