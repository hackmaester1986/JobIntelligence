namespace JobIntelligence.Core.Entities;

public class GeonamesCity
{
    public long GeonameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AsciiName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string? Admin1Code { get; set; }
    public long Population { get; set; }
}
