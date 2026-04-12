namespace JobIntelligence.Infrastructure.Parsing;

public static class LocationParser
{
    private static readonly HashSet<string> RemoteKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "remote", "anywhere", "worldwide", "work from home", "wfh", "distributed" };

    private static readonly HashSet<string> HybridKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "hybrid", "flexible" };

    // Common US state abbreviations
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

        // Strip known noise words to get at the location part
        var cleaned = System.Text.RegularExpressions.Regex
            .Replace(normalized, @"\b(remote|hybrid|flexible|on-?site|in-?office|US only|US Only)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Replace("()", "").Replace("- -", "-").Trim(" ,()-".ToCharArray());

        string? city = null, state = null, country = null;

        // Split on comma
        var parts = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            var possibleCity = parts[0];
            var possibleStateOrCountry = parts[1];

            if (UsStates.Contains(possibleStateOrCountry))
            {
                city = possibleCity;
                state = possibleStateOrCountry.ToUpperInvariant();
                country = "US";
                // Optional explicit country in part[2]
            }
            else
            {
                city = possibleCity;
                country = NormalizeCountry(possibleStateOrCountry);
            }
        }
        else if (parts.Length == 1 && !isRemote && !isHybrid)
        {
            // Could be a country or city alone
            var single = parts[0];
            if (single.Length > 0)
                country = NormalizeCountry(single) ?? (single.Length <= 3 ? single.ToUpperInvariant() : null);
        }

        // Don't store placeholder city values
        if (city != null && RemoteKeywords.Any(k => city.Equals(k, StringComparison.OrdinalIgnoreCase)))
            city = null;

        return new ParsedLocation(city, state, country, isRemote, isHybrid);
    }

    private static string? NormalizeCountry(string raw)
    {
        return raw.Trim().ToUpperInvariant() switch
        {
            "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
            "UK" or "GB" or "UNITED KINGDOM" or "GREAT BRITAIN" or "ENGLAND" => "GB",
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
            _ => null
        };
    }
}
