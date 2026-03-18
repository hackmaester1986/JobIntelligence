using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class JobSourceConfiguration : IEntityTypeConfiguration<JobSource>
{
    public void Configure(EntityTypeBuilder<JobSource> builder)
    {
        builder.ToTable("job_sources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseUrl).HasColumnName("base_url").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ApiVersion).HasColumnName("api_version").HasMaxLength(20);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.RateLimitRps).HasColumnName("rate_limit_rps");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
