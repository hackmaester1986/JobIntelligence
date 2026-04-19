using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class EducationParser
{
    // PhD patterns
    private static readonly Regex PhdPattern = new(
        @"\b(?:ph\.?d\.?|doctorate|doctoral\s+degree)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Master's patterns
    private static readonly Regex MasterPattern = new(
        @"\b(?:master'?s?(?:\s+degree)?|m\.?s\.?|m\.?eng\.?|m\.?b\.?a\.?|msc|graduate\s+degree)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Bachelor's patterns
    private static readonly Regex BachelorPattern = new(
        @"\b(?:bachelor'?s?(?:\s+degree)?|b\.?s\.?|b\.?a\.?|b\.?eng\.?|undergraduate\s+degree|4-year\s+degree|four[- ]year\s+degree)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "degree required", "degree in computer science", "degree preferred" — generic degree mention
    private static readonly Regex GenericDegreePattern = new(
        @"\bdegree\s+(?:in\b|required|preferred|or\s+equivalent)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the minimum required education level: "phd", "master", "bachelor", or null if not mentioned.
    /// </summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (PhdPattern.IsMatch(text))     return "phd";
        if (MasterPattern.IsMatch(text))  return "master";
        if (BachelorPattern.IsMatch(text) || GenericDegreePattern.IsMatch(text)) return "bachelor";

        return null;
    }
}
