using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class UsLocationClassifier
{
    private static readonly string[] NonUsCountries =
    [
        "germany", "france", "united kingdom", "england", "scotland", "wales", "northern ireland",
        "india", "romania", "portugal", "spain", "canada", "japan", "australia", "ireland",
        "slovenia", "netherlands", "belgium", "switzerland", "austria", "italy", "poland",
        "sweden", "norway", "denmark", "finland", "czech republic", "czechia", "ukraine",
        "turkey", "israel", "singapore", "south korea", "new zealand", "argentina", "colombia",
        "peru", "chile", "brazil", "mexico", "taiwan", "morocco", "costa rica", "philippines",
        "thailand", "indonesia", "malaysia", "vietnam", "pakistan", "bangladesh", "sri lanka",
        "nepal", "kenya", "nigeria", "south africa", "egypt", "united arab emirates",
        "saudi arabia", "qatar", "bahrain", "kuwait", "china", "hong kong", "russia", "belarus",
        "hungary", "slovakia", "croatia", "serbia", "bulgaria", "greece", "luxembourg",
        "estonia", "latvia", "lithuania", "cyprus"
    ];

    // Non-US ISO 3166-1 alpha-2 codes as seen in location strings (lowercase)
    private static readonly HashSet<string> NonUsIsoCodes = new()
    {
        "gb", "de", "fr", "in", "ro", "pt", "es", "jp", "au", "ie",
        "be", "si", "nl", "ch", "at", "it", "pl", "se", "no", "dk",
        "fi", "cz", "ua", "tr", "il", "sg", "kr", "nz", "ar", "co",
        "pe", "cl", "br", "mx", "tw", "ma", "cr", "ph", "th", "id",
        "my", "vn", "pk", "bd", "lk", "np", "ke", "ng", "za", "eg",
        "ae", "sa", "qa", "bh", "kw", "cn", "hk", "ru", "by", "hu",
        "sk", "hr", "rs", "bg", "gr", "lu", "ee", "lv", "lt"
    };

    // Well-known non-US cities — match at start of string or as full value
    private static readonly HashSet<string> NonUsCityPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // India
        "bengaluru", "mumbai", "chennai", "gurugram", "gurgaon", "delhi", "new delhi",
        "hyderabad", "pune", "kolkata", "ahmedabad", "noida",
        // UK
        "london", "manchester", "birmingham", "glasgow", "edinburgh",
        // Germany
        "berlin", "münchen", "munich", "hamburg", "frankfurt", "cologne", "düsseldorf", "lingen", "leverkusen",
        // France
        "paris", "lyon", "marseille", "ivry", "poitiers",
        // Canada
        "toronto", "vancouver", "montreal", "calgary", "ottawa", "mississauga",
        // Oceania
        "sydney", "melbourne", "brisbane", "perth", "auckland",
        // Asia
        "tokyo", "osaka", "seoul", "beijing", "shanghai", "shenzhen", "taipei",
        "singapore", "bangkok", "jakarta", "kuala lumpur", "hanoi", "ho chi minh",
        // Europe
        "amsterdam", "rotterdam", "brussels", "zurich", "geneva", "vienna",
        "stockholm", "oslo", "copenhagen", "helsinki",
        "madrid", "barcelona", "lisbon", "porto", "logroño",
        "rome", "milan", "warsaw", "prague", "budapest", "bucharest",
        "ieper",
        // Middle East / Africa / LatAm
        "dubai", "abu dhabi", "riyadh", "tel aviv", "istanbul",
        "moscow", "kyiv", "kiev",
        "nairobi", "lagos", "johannesburg", "cairo",
        "mexico city", "buenos aires", "bogota", "lima", "santiago",
        "sao paulo", "rio de janeiro", "arequipa",
        // Other
        "fes", "san josé"
    };

    private static readonly string[] CanadianProvinces =
    [
        "ontario", "british columbia", "alberta", "quebec", "manitoba",
        "saskatchewan", "nova scotia", "new brunswick", "newfoundland", "prince edward"
    ];

    private static readonly string[] IndianStates =
    [
        "karnataka", "maharashtra", "tamil nadu", "telangana", "andhra pradesh",
        "kerala", "haryana", "uttar pradesh", "gujarat", "rajasthan",
        "west bengal"
    ];

    private static readonly string[] GermanStates =
    [
        "bavaria", "bayern", "nordrhein", "niedersachsen", "sachsen",
        "thüringen", "hessen", "württemberg"
    ];

    private static readonly HashSet<string> UsStateAbbr = new(StringComparer.Ordinal)
    {
        "AL","AK","AZ","AR","CA","CO","CT","DE","FL","GA","HI","ID","IL","IN","IA","KS","KY",
        "LA","ME","MD","MA","MI","MN","MS","MO","MT","NE","NV","NH","NJ","NM","NY","NC","ND",
        "OH","OK","OR","PA","RI","SC","SD","TN","TX","UT","VT","VA","WA","WV","WI","WY","DC"
    };

    private static readonly string[] UsStateNames =
    [
        "alabama", "alaska", "arizona", "arkansas", "california", "colorado",
        "connecticut", "delaware", "florida", "georgia", "hawaii", "idaho",
        "illinois", "indiana", "iowa", "kansas", "kentucky", "louisiana",
        "maine", "maryland", "massachusetts", "michigan", "minnesota", "mississippi",
        "missouri", "montana", "nebraska", "nevada", "new hampshire", "new jersey",
        "new mexico", "new york", "north carolina", "north dakota", "ohio", "oklahoma",
        "oregon", "pennsylvania", "rhode island", "south carolina", "south dakota",
        "tennessee", "texas", "utah", "vermont", "virginia", "washington",
        "west virginia", "wisconsin", "wyoming", "district of columbia"
    ];

    /// <summary>
    /// Returns true = US, false = international, null = unknown (include in all filters).
    /// </summary>
    public static bool? Classify(string? locationRaw)
    {
        if (string.IsNullOrWhiteSpace(locationRaw)) return null;

        var text = locationRaw.Trim();
        var lower = text.ToLowerInvariant();

        // Non-Latin scripts (Georgian, CJK, Arabic, Cyrillic, Devanagari, etc.)
        // Allow Latin-1 Supplement + Latin Extended A/B (0x00C0–0x02FF) for European city names
        if (text.Any(c => c > '\u02FF')) return false;

        // Non-US country names (whole word)
        foreach (var country in NonUsCountries)
            if (Word(lower, country)) return false;

        // "UK" abbreviation
        if (Regex.IsMatch(lower, @"\buk\b")) return false;

        // Non-US ISO code as last segment: "Catford, gb"  "Leverkusen, de"
        var isoMatch = Regex.Match(lower, @",\s*([a-z]{2})\s*$");
        if (isoMatch.Success && NonUsIsoCodes.Contains(isoMatch.Groups[1].Value)) return false;

        // Well-known non-US cities at start of string or as the entire value
        var lowerTrimmed = lower.Trim();
        foreach (var city in NonUsCityPrefixes)
        {
            var c = city.ToLowerInvariant();
            if (lowerTrimmed == c || lowerTrimmed.StartsWith(c + ",") || lowerTrimmed.StartsWith(c + " "))
                return false;
        }

        // Canadian provinces and Indian/German states
        foreach (var p in CanadianProvinces)
            if (Word(lower, p)) return false;
        foreach (var s in IndianStates.Concat(GermanStates))
            if (Word(lower, s)) return false;

        // ── US indicators (checked after all non-US exclusions) ──────────

        if (lower.Contains("united states")) return true;
        if (Word(lower, "usa")) return true;

        // ", us" ISO code style (always lowercase in this dataset): "Houston, us"
        if (Regex.IsMatch(lower, @",\s*us\b")) return true;

        // Standalone "US" (case-sensitive to avoid matching "us" in words like "focus")
        if (Regex.IsMatch(text, @"(^|,\s*)US(\s*$|,|\s)")) return true;
        if (text.Trim() is "U.S." or "U.S.A.") return true;

        // Remote + US context: "Remote (US)", "Remote - US", "US - Remote", "Remote, US"
        if (Regex.IsMatch(lower, @"remote\s*[-–(,]\s*us\b|us\s*[-–)]\s*remote")) return true;

        // US state abbreviations after comma: "Boston, MA"  "Phoenix, AZ Headquarters"
        foreach (Match m in Regex.Matches(text, @",\s*([A-Z]{2})(?=\s|,|$)"))
            if (UsStateAbbr.Contains(m.Groups[1].Value)) return true;

        // US state full names
        foreach (var state in UsStateNames)
            if (Word(lower, state)) return true;

        // US ZIP code
        if (Regex.IsMatch(text, @"\b\d{5}(?:-\d{4})?\b")) return true;

        return null;
    }

    private static bool Word(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
}
