using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class ExperienceParser
{
    public record ParsedExperience(int? Min, int? Max);

    // "3-5 years", "3 to 5 years", "3–5 years of experience"
    private static readonly Regex RangePattern = new(
        @"(\d+)\s*(?:-|–|to)\s*(\d+)\s*\+?\s*years?(?:\s+of)?(?:\s+(?:relevant\s+)?(?:professional\s+)?(?:work\s+)?experience)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "5+ years", "at least 5 years", "minimum of 5 years", "5 or more years"
    private static readonly Regex MinPattern = new(
        @"(?:at\s+least|minimum\s+(?:of\s+)?|(?:\d+)\s*\+)\s*(\d+)\s*\+?\s*years?(?:\s+of)?(?:\s+(?:relevant\s+)?(?:professional\s+)?(?:work\s+)?experience)?|(\d+)\s*\+\s*years?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "5 years of experience" (single number, no range or plus)
    private static readonly Regex SinglePattern = new(
        @"(\d+)\s*years?\s+(?:of\s+)?(?:relevant\s+)?(?:professional\s+)?(?:work\s+)?experience",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedExperience Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedExperience(null, null);

        // Range first — most specific
        var rangeMatch = RangePattern.Match(text);
        if (rangeMatch.Success)
        {
            var lo = int.Parse(rangeMatch.Groups[1].Value);
            var hi = int.Parse(rangeMatch.Groups[2].Value);
            if (lo <= hi && hi <= 40)
                return new ParsedExperience(lo, hi);
        }

        // "5+" or "at least 5" — open-ended minimum
        var minMatch = MinPattern.Match(text);
        if (minMatch.Success)
        {
            var raw = minMatch.Groups[1].Success ? minMatch.Groups[1].Value : minMatch.Groups[2].Value;
            if (int.TryParse(raw, out var years) && years <= 40)
                return new ParsedExperience(years, null);
        }

        // Single number
        var singleMatch = SinglePattern.Match(text);
        if (singleMatch.Success)
        {
            if (int.TryParse(singleMatch.Groups[1].Value, out var years) && years <= 40)
                return new ParsedExperience(years, years);
        }

        return new ParsedExperience(null, null);
    }
}
