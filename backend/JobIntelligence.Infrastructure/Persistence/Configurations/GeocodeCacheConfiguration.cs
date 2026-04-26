using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class GeocodeCacheConfiguration : IEntityTypeConfiguration<GeocodeCache>
{
    public void Configure(EntityTypeBuilder<GeocodeCache> builder)
    {
        builder.ToTable("geocode_cache");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(x => x.City).HasColumnName("city").IsRequired();
        builder.Property(x => x.State).HasColumnName("state");
        builder.Property(x => x.Country).HasColumnName("country");
        builder.Property(x => x.Latitude).HasColumnName("latitude").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(50).HasDefaultValue("nominatim");
        builder.Property(x => x.GeocodedAt).HasColumnName("geocoded_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(x => new { x.City, x.State, x.Country }).IsUnique()
            .HasDatabaseName("IX_geocode_cache_location");
    }
}
