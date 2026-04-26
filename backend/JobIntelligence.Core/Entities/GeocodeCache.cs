namespace JobIntelligence.Core.Entities;

public class GeocodeCache
{
    public long Id { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? Country { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Source { get; set; } = "nominatim";
    public DateTime GeocodedAt { get; set; } = DateTime.UtcNow;
}
