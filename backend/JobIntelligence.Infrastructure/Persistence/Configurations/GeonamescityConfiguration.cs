using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobIntelligence.Infrastructure.Persistence.Configurations;

public class GeonamescityConfiguration : IEntityTypeConfiguration<GeonamesCity>
{
    public void Configure(EntityTypeBuilder<GeonamesCity> builder)
    {
        builder.ToTable("geonames_cities");
        builder.HasKey(x => x.GeonameId);
        builder.Property(x => x.GeonameId).HasColumnName("geoname_id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AsciiName).HasColumnName("ascii_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Latitude).HasColumnName("latitude").IsRequired();
        builder.Property(x => x.Longitude).HasColumnName("longitude").IsRequired();
        builder.Property(x => x.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsRequired();
        builder.Property(x => x.Admin1Code).HasColumnName("admin1_code").HasMaxLength(20);
        builder.Property(x => x.Population).HasColumnName("population");
        builder.HasIndex(x => new { x.AsciiName, x.Admin1Code, x.CountryCode })
            .HasDatabaseName("IX_geonames_cities_lookup");
    }
}
