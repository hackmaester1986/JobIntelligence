using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class ResumeEmbeddingConfiguration : IEntityTypeConfiguration<ResumeEmbedding>
{
    public void Configure(EntityTypeBuilder<ResumeEmbedding> builder)
    {
        builder.ToTable("resume_embeddings");
        builder.HasKey(x => x.ResumeId);
        builder.Property(x => x.ResumeId).HasColumnName("resume_id");
        builder.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
        builder.Property(x => x.EmbeddingText).HasColumnName("embedding_text").IsRequired();
        builder.Property(x => x.EmbeddedAt).HasColumnName("embedded_at").HasDefaultValueSql("NOW()");

        builder.HasOne(x => x.Resume)
            .WithOne(x => x.Embedding)
            .HasForeignKey<ResumeEmbedding>(x => x.ResumeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
