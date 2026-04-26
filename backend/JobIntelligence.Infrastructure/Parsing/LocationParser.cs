using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class LocationParser
{
    private static readonly HashSet<string> RemoteKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "remote", "anywhere", "worldwide", "work from home", "wfh", "distributed", "remoto" };

    private static readonly HashSet<string> HybridKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "hybrid", "flexible" };

    private static readonly HashSet<string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY",
        "LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND",
        "OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    private static readonly string[] DescriptionRemoteKeywords =
        ["remote", "work from home", "wfh", "fully remote", "100% remote", "remote-first", "remote first"];

    public static bool HasRemoteInDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        var lower = description.ToLowerInvariant();
        return DescriptionRemoteKeywords.Any(k => lower.Contains(k));
    }

    public record ParsedLocation(
        string? City, string? State, string? Country, bool IsRemote, bool IsHybrid);

    public static ParsedLocation Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new ParsedLocation(null, null, null, false, false);

        var normalized = raw.Trim();
        var lower = normalized.ToLowerInvariant();

        bool isRemote = RemoteKeywords.Any(k => lower.Contains(k));
        bool isHybrid = HybridKeywords.Any(k => lower.Contains(k));

        // Strip known noise words
        var cleaned = Regex.Replace(normalized,
            @"\b(remote|hybrid|flexible|on-?site|in-?office|US only|US Only|field based)\b", "",
            RegexOptions.IgnoreCase)
            .Replace("()", "").Replace("- -", "-").Trim(" ,()-".ToCharArray());

        string? city = null, state = null, country = null;

        // --- Pre-processing pipeline ---

        // 1. Strip store prefix: "Store 05648 Ipswich MA" → "Ipswich MA"
        cleaned = Regex.Replace(cleaned, @"^Store\s+\w+\s+", "", RegexOptions.IgnoreCase);

        // 2. Normalize separators
        cleaned = cleaned.Replace(";", ",");

        // 3. Strip zip code suffix: "4408 Little Rd Arlington TX 76016-5605" → strip trailing zip
        cleaned = Regex.Replace(cleaned, @"\s+\d{5}(-\d{4})?\s*$", "").Trim();

        // 4. Strip leading street address (starts with number + word): "1824 Ashville Rd Leeds AL" → try to keep last city+state
        var streetMatch = Regex.Match(cleaned, @"^\d+\s+\S+\s+\S+\s+(.+)$");
        if (streetMatch.Success)
            cleaned = streetMatch.Groups[1].Value.Trim();

        // 5. US | ST | City - Address: "US | IL | Chicago - 200 South Wacker Drive"
        var pipeMatch = Regex.Match(cleaned, @"^US\s*\|\s*([A-Z]{2})\s*\|\s*([^|,\-]+)", RegexOptions.IgnoreCase);
        if (pipeMatch.Success && UsStates.Contains(pipeMatch.Groups[1].Value))
            return new ParsedLocation(pipeMatch.Groups[2].Value.Trim(), pipeMatch.Groups[1].Value.ToUpperInvariant(), "US", isRemote, isHybrid);

        // 6. USA - ST - City: "USA - CA - Healdsburg"
        var usaDashMatch = Regex.Match(cleaned, @"^USA?\s*-\s*([A-Z]{2})\s*-\s*(.+)", RegexOptions.IgnoreCase);
        if (usaDashMatch.Success && UsStates.Contains(usaDashMatch.Groups[1].Value))
            return new ParsedLocation(usaDashMatch.Groups[2].Value.Trim(), usaDashMatch.Groups[1].Value.ToUpperInvariant(), "US", isRemote, isHybrid);

        // 7. US ST CityName: "US NJ Morristown", "US AZ Phoenix"
        var usStateCityMatch = Regex.Match(cleaned, @"^US\s+([A-Z]{2})\s+(.+)", RegexOptions.IgnoreCase);
        if (usStateCityMatch.Success && UsStates.Contains(usStateCityMatch.Groups[1].Value))
            return new ParsedLocation(usStateCityMatch.Groups[2].Value.Trim(), usStateCityMatch.Groups[1].Value.ToUpperInvariant(), "US", isRemote, isHybrid);

        // 8. Strip parentheticals (after structured patterns so we don't lose "São Paulo (SP)")
        //    First try to extract state from paren
        var parenStateMatch = Regex.Match(cleaned, @"\(([A-Z]{2})\)");
        if (parenStateMatch.Success && UsStates.Contains(parenStateMatch.Groups[1].Value))
        {
            state = parenStateMatch.Groups[1].Value.ToUpperInvariant();
            country = "US";
        }
        cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)", "").Trim();

        // --- Comma-split logic (existing, now also handles "Franklin, Tennessee") ---
        var parts = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            var possibleCity = parts[0];
            // Strip parentheticals from parts[1] so "OH (Omniplex)" → "OH", "MO (Bass Pro Shops Base Camp)" → "MO"
            var possibleStateOrCountry = Regex.Replace(parts[1], @"\s*\([^)]*\)", "").Trim();

            if (UsStates.Contains(possibleStateOrCountry))
            {
                city = possibleCity;
                state = possibleStateOrCountry.ToUpperInvariant();
                country = "US";
            }
            else
            {
                // Could be full state name: "Franklin, Tennessee"
                var stateFromName = StateNameToAbbrev(possibleStateOrCountry.Trim());
                if (stateFromName != null)
                {
                    city = possibleCity;
                    state = stateFromName;
                    country = "US";
                }
                else
                {
                    city = possibleCity;
                    country = NormalizeCountry(possibleStateOrCountry);
                }
            }
        }
        else if (parts.Length == 1)
        {
            var single = parts[0];

            // 9. City-City-Country / City-Region-Country (dash patterns, no comma)
            if (single.Contains('-'))
            {
                var dashParts = single.Split('-')
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToArray();

                if (dashParts.Length >= 2)
                {
                    var lastPart = dashParts[^1];
                    var countryFromDash = NormalizeCountry(lastPart);
                    if (countryFromDash != null)
                    {
                        city = dashParts[0];
                        country = countryFromDash;
                    }
                    else
                    {
                        // "City - Qualifier" — use first part as city
                        city = dashParts[0];
                        // If state was extracted from paren above, keep it
                    }
                }
            }
            else
            {
                // 10. City ST (last word is US state, no comma): "West Terre Haute IN", "Lincoln NE"
                var words = single.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 2 && UsStates.Contains(words[^1]))
                {
                    city = string.Join(" ", words[..^1]);
                    state = words[^1].ToUpperInvariant();
                    country = "US";
                }
                else if (single.Length > 0 && !isRemote)
                {
                    // Single token: try as country, else treat as city
                    var normalizedCountry = NormalizeCountry(single);
                    if (normalizedCountry != null)
                        country = normalizedCountry;
                    else if (single.Length > 3 || (single.Length <= 3 && !UsStates.Contains(single)))
                        city = single; // bare city name like "Paris", "Mumbai", "Lynn"
                }
            }
        }

        // Don't store placeholder city values
        if (city != null && RemoteKeywords.Any(k => city.Equals(k, StringComparison.OrdinalIgnoreCase)))
            city = null;

        return new ParsedLocation(city, state, country, isRemote, isHybrid);
    }

    private static string? StateNameToAbbrev(string name) => name.ToUpperInvariant() switch
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

    private static string? NormalizeCountry(string raw)
    {
        return raw.Trim().ToUpperInvariant() switch
        {
            "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
            "UK" or "GB" or "GBR" or "UNITED KINGDOM" or "GREAT BRITAIN" or "ENGLAND" => "GB",
            "CA" or "CANADA" => "CA",
            "AU" or "AUSTRALIA" => "AU",
            "DE" or "GERMANY" or "DEUTSCHLAND" => "DE",
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
            _ => null
        };
    }
}
