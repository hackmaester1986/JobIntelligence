namespace JobIntelligence.Core.Interfaces;

public interface IGeocodingService
{
    Task<(double Lat, double Lng)?> GeocodeAsync(
        string? city, string? state, string? country, string? locationRaw,
        CancellationToken ct = default);

    Task WarmCacheAsync(CancellationToken ct = default);
}
