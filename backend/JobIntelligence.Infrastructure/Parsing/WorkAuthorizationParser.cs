using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class WorkAuthorizationParser
{
    // Explicit "no sponsorship" signals
    private static readonly Regex NoSponsorshipPattern = new(
        @"\b(?:no\s+(?:visa\s+)?sponsorship|not\s+(?:able|eligible)\s+to\s+sponsor|sponsorship\s+(?:is\s+)?not\s+(?:available|provided|offered)|unable\s+to\s+sponsor)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "must be authorized to work in the US/United States"
    private static readonly Regex AuthorizedPattern = new(
        @"\b(?:must\s+be\s+(?:legally\s+)?(?:authorized|eligible|permitted)\s+to\s+work|authorized\s+to\s+work\s+in\s+the\s+(?:us|u\.s\.?|united\s+states))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "US citizen", "US citizen or permanent resident", "green card"
    private static readonly Regex CitizenshipPattern = new(
        @"\b(?:(?:us|u\.s\.?|united\s+states)\s+(?:citizen|national)|permanent\s+resident|green\s+card\s+holder|security\s+clearance\s+required)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "eligible to work in the US without sponsorship"
    private static readonly Regex EligibleWithoutPattern = new(
        @"\beligible\s+to\s+work\s+(?:in\s+the\s+(?:us|u\.s\.?|united\s+states)\s+)?without\s+(?:visa\s+)?sponsorship\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the posting explicitly requires US work authorization / no sponsorship.
    /// Returns null if no signal found.
    /// </summary>
    public static bool? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (NoSponsorshipPattern.IsMatch(text)  ||
            AuthorizedPattern.IsMatch(text)      ||
            CitizenshipPattern.IsMatch(text)     ||
            EligibleWithoutPattern.IsMatch(text))
            return true;

        return null;
    }
}
