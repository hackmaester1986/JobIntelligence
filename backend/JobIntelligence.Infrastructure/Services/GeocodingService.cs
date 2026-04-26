using System.Collections.Concurrent;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class GeocodingService(
    ApplicationDbContext db,
    ILogger<GeocodingService> logger) : IGeocodingService
{
    private readonly ConcurrentDictionary<string, (double Lat, double Lng)?> _cache = new();
    private bool _warmed;

    private static readonly HashSet<string> RemoteKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "remote", "anywhere", "worldwide", "wfh", "distributed", "remoto" };

    public async Task WarmCacheAsync(CancellationToken ct = default)
    {
        if (_warmed) return;
        var entries = await db.GeocodeCaches.AsNoTracking().ToListAsync(ct);
        foreach (var e in entries)
            _cache[CacheKey(e.City, e.State, e.Country)] = (e.Latitude, e.Longitude);
        _warmed = true;
        logger.LogInformation("GeocodingService: warmed cache with {Count} entries", entries.Count);
    }

    public async Task<(double Lat, double Lng)?> GeocodeAsync(
        string? city, string? state, string? country, string? locationRaw,
        CancellationToken ct = default)
    {
        if (RemoteKeywords.Any(k =>
            (city?.Contains(k, StringComparison.OrdinalIgnoreCase) == true) ||
            (locationRaw?.Contains(k, StringComparison.OrdinalIgnoreCase) == true)))
            return null;

        var countryCode = NormalizeToIsoCode(country);

        // Tier 1: GeoNames city lookup
        if (!string.IsNullOrWhiteSpace(city))
        {
            // City is a US state abbreviation (e.g. "MO", "OH") — reroute to state centroid
            if (StateCentroids.ContainsKey(city.ToUpperInvariant()) && city.Length == 2)
                return StateCentroids[city.ToUpperInvariant()];

            // City is a US state full name (e.g. "Nebraska", "Massachusetts") — reroute to state centroid
            var stateAbbrev = StateNameToAbbrev(city);
            if (stateAbbrev != null && StateCentroids.TryGetValue(stateAbbrev, out var centroidFromName))
                return centroidFromName;

            var resolvedCity = CityAliases.TryGetValue(city, out var alias) ? alias : city;
            var key = CacheKey(resolvedCity, state, countryCode);
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var result = await GeoNamesLookupAsync(resolvedCity, state, countryCode, ct);
            _cache[key] = result;
            if (result.HasValue)
                await SaveToCacheAsync(resolvedCity, state, countryCode, result.Value, ct);
            return result;
        }

        // Tier 2: US state centroid
        if (!string.IsNullOrWhiteSpace(state) && StateCentroids.TryGetValue(state.ToUpperInvariant(), out var stateCentroid))
        {
            logger.LogDebug("GeocodingService: state centroid for {State}", state);
            return stateCentroid;
        }

        // Tier 3: country centroid
        if (countryCode != null && CountryCentroids.TryGetValue(countryCode, out var countryCentroid))
        {
            logger.LogDebug("GeocodingService: country centroid for {Country}", countryCode);
            return countryCentroid;
        }

        return null;
    }

    private async Task<(double Lat, double Lng)?> GeoNamesLookupAsync(
        string city, string? state, string? countryCode, CancellationToken ct)
    {
        if (countryCode == null) return null;

        var query = db.GeonamesCities
            .AsNoTracking()
            .Where(g => EF.Functions.ILike(g.AsciiName, city))
            .Where(g => g.CountryCode == countryCode);

        if (countryCode == "US" && !string.IsNullOrWhiteSpace(state))
            query = query.Where(g => g.Admin1Code == state.ToUpperInvariant());

        var match = await query
            .OrderByDescending(g => g.Population)
            .Select(g => new { g.Latitude, g.Longitude })
            .FirstOrDefaultAsync(ct);

        return match == null ? null : (match.Latitude, match.Longitude);
    }

    private async Task SaveToCacheAsync(string city, string? state, string? country, (double Lat, double Lng) coords, CancellationToken ct)
    {
        try
        {
            var existing = await db.GeocodeCaches
                .FirstOrDefaultAsync(g => g.City == city && g.State == state && g.Country == country, ct);
            if (existing == null)
            {
                db.GeocodeCaches.Add(new Core.Entities.GeocodeCache
                {
                    City = city, State = state, Country = country,
                    Latitude = coords.Lat, Longitude = coords.Lng,
                });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save geocode cache entry for {City}", city);
        }
    }

    private static string CacheKey(string? city, string? state, string? country) =>
        $"{city?.ToLowerInvariant()}|{state?.ToLowerInvariant()}|{country?.ToLowerInvariant()}";

    private static readonly Dictionary<string, string> CityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["New York"] = "New York City",
        ["New York City"] = "New York City",
    };

    private static readonly HashSet<string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY",
        "LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND",
        "OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    private static string? StateNameToAbbrev(string name) => name.Trim().ToUpperInvariant() switch
    {
        "ALABAMA" => "AL", "ALASKA" => "AK", "ARIZONA" => "AZ", "ARKANSAS" => "AR",
        "CALIFORNIA" => "CA", "COLORADO" => "CO", "CONNECTICUT" => "CT", "DELAWARE" => "DE",
        "FLORIDA" => "FL", "GEORGIA" => "GA", "HAWAII" => "HI", "IDAHO" => "ID",
        "ILLINOIS" => "IL", "INDIANA" => "IN", "IOWA" => "IA", "KANSAS" => "KS",
        "KENTUCKY" => "KY", "LOUISIANA" => "LA", "MAINE" => "ME", "MARYLAND" => "MD",
        "MASSACHUSETTS" => "MA", "MICHIGAN" => "MI", "MINNESOTA" => "MN", "MISSISSIPPI" => "MS",
        "MISSOURI" => "MO", "MONTANA" => "MT", "NEBRASKA" => "NE", "NEVADA" => "NV",
        "NEW HAMPSHIRE" => "NH", "NEW JERSEY" => "NJ", "NEW MEXICO" => "NM", "NEW YORK" => "NY",
        "NORTH CAROLINA" => "NC", "NORTH DAKOTA" => "ND", "OHIO" => "OH", "OKLAHOMA" => "OK",
        "OREGON" => "OR", "PENNSYLVANIA" => "PA", "RHODE ISLAND" => "RI", "SOUTH CAROLINA" => "SC",
        "SOUTH DAKOTA" => "SD", "TENNESSEE" => "TN", "TEXAS" => "TX", "UTAH" => "UT",
        "VERMONT" => "VT", "VIRGINIA" => "VA", "WASHINGTON" => "WA", "WEST VIRGINIA" => "WV",
        "WISCONSIN" => "WI", "WYOMING" => "WY", "DISTRICT OF COLUMBIA" => "DC",
        _ => null
    };

    private static string? NormalizeToIsoCode(string? country) => country?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
        "GB" or "UK" or "UNITED KINGDOM" or "GREAT BRITAIN" or "ENGLAND" => "GB",
        "CA" or "CANADA" => "CA",
        "AU" or "AUSTRALIA" => "AU",
        "DE" or "GERMANY" => "DE",
        "FR" or "FRANCE" => "FR",
        "IN" or "INDIA" => "IN",
        "BR" or "BRAZIL" or "BRASIL" => "BR",
        "SG" or "SINGAPORE" => "SG",
        "NL" or "NETHERLANDS" => "NL",
        "SE" or "SWEDEN" => "SE",
        "PL" or "POLAND" => "PL",
        "ES" or "SPAIN" => "ES",
        "IL" or "ISRAEL" => "IL",
        "IE" or "IRELAND" => "IE",
        "NO" or "NORWAY" => "NO",
        "CO" or "COLOMBIA" => "CO",
        "MX" or "MEXICO" => "MX",
        "JP" or "JAPAN" => "JP",
        "CN" or "CHINA" => "CN",
        "HK" or "HONG KONG" => "HK",
        "MY" or "MALAYSIA" => "MY",
        "RO" or "ROMANIA" => "RO",
        "BE" or "BELGIUM" => "BE",
        "IT" or "ITALY" => "IT",
        "PT" or "PORTUGAL" => "PT",
        "CH" or "SWITZERLAND" => "CH",
        var x => x // pass through if already ISO
    };

    private static readonly Dictionary<string, (double Lat, double Lng)> StateCentroids = new()
    {
        ["AL"] = (32.806671, -86.791130), ["AK"] = (61.370716, -152.404419),
        ["AZ"] = (33.729759, -111.431221), ["AR"] = (34.969704, -92.373123),
        ["CA"] = (36.116203, -119.681564), ["CO"] = (39.059811, -105.311104),
        ["CT"] = (41.597782, -72.755371), ["DE"] = (39.318523, -75.507141),
        ["FL"] = (27.766279, -81.686783), ["GA"] = (33.040619, -83.643074),
        ["HI"] = (21.094318, -157.498337), ["ID"] = (44.240459, -114.478828),
        ["IL"] = (40.349457, -88.986137), ["IN"] = (39.849426, -86.258278),
        ["IA"] = (42.011539, -93.210526), ["KS"] = (38.526600, -96.726486),
        ["KY"] = (37.668140, -84.670067), ["LA"] = (31.169960, -91.867805),
        ["ME"] = (44.693947, -69.381927), ["MD"] = (39.063946, -76.802101),
        ["MA"] = (42.230171, -71.530106), ["MI"] = (43.326618, -84.536095),
        ["MN"] = (45.694454, -93.900192), ["MS"] = (32.741646, -89.678696),
        ["MO"] = (38.456085, -92.288368), ["MT"] = (46.921925, -110.454353),
        ["NE"] = (41.125370, -98.268082), ["NV"] = (38.313515, -117.055374),
        ["NH"] = (43.452492, -71.563896), ["NJ"] = (40.298904, -74.521011),
        ["NM"] = (34.840515, -106.248482), ["NY"] = (42.165726, -74.948051),
        ["NC"] = (35.630066, -79.806419), ["ND"] = (47.528912, -99.784012),
        ["OH"] = (40.388783, -82.764915), ["OK"] = (35.565342, -96.928917),
        ["OR"] = (44.572021, -122.070938), ["PA"] = (40.590752, -77.209755),
        ["RI"] = (41.680893, -71.511780), ["SC"] = (33.856892, -80.945007),
        ["SD"] = (44.299782, -99.438828), ["TN"] = (35.747845, -86.692345),
        ["TX"] = (31.054487, -97.563461), ["UT"] = (40.150032, -111.862434),
        ["VT"] = (44.045876, -72.710686), ["VA"] = (37.769337, -78.169968),
        ["WA"] = (47.400902, -121.490494), ["WV"] = (38.491226, -80.954453),
        ["WI"] = (44.268543, -89.616508), ["WY"] = (42.755966, -107.302490),
        ["DC"] = (38.897438, -77.026817),
    };

    private static readonly Dictionary<string, (double Lat, double Lng)> CountryCentroids = new()
    {
        ["US"] = (39.500000, -98.350000),
        ["GB"] = (55.378052, -3.435973),
        ["CA"] = (56.130366, -106.346771),
        ["AU"] = (-25.274398, 133.775136),
        ["DE"] = (51.165691, 10.451526),
        ["FR"] = (46.227638, 2.213749),
        ["IN"] = (20.593684, 78.962880),
        ["BR"] = (-14.235004, -51.925280),
        ["SG"] = (1.352083, 103.819836),
        ["NL"] = (52.132633, 5.291266),
        ["SE"] = (60.128161, 18.643501),
        ["PL"] = (51.919438, 19.145136),
        ["ES"] = (40.463667, -3.749220),
        ["IL"] = (31.046051, 34.851612),
        ["IE"] = (53.412910, -8.243890),
        ["NO"] = (60.472024, 8.468946),
        ["CO"] = (4.570868, -74.297333),
        ["MX"] = (23.634501, -102.552784),
        ["JP"] = (36.204824, 138.252924),
        ["CN"] = (35.861660, 104.195397),
        ["HK"] = (22.396428, 114.109497),
        ["MY"] = (4.210484, 101.975766),
        ["RO"] = (45.943161, 24.966760),
        ["BE"] = (50.503887, 4.469936),
        ["IT"] = (41.871940, 12.567380),
        ["PT"] = (39.399872, -8.224454),
        ["CH"] = (46.818188, 8.227512),
    };
}
