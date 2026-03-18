using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class TitleParser
{
    private static readonly (Regex Pattern, string Level)[] Rules =
    [
        (new Regex(@"\b(vp|vice president)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "vp"),
        (new Regex(@"\b(chief|cto|ceo|cpo|coo)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "executive"),
        (new Regex(@"\bdirector\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "director"),
        (new Regex(@"\b(engineering manager|em\b|manager)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "manager"),
        (new Regex(@"\bprincipal\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "principal"),
        (new Regex(@"\bstaff\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "staff"),
        (new Regex(@"\b(sr\.?|senior)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "senior"),
        (new Regex(@"\b(lead|tech lead|team lead)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "lead"),
        (new Regex(@"\b(jr\.?|junior|associate|entry.?level|new grad|university grad)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "junior"),
        (new Regex(@"\bintern(ship)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "intern"),
    ];

    public static string? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        foreach (var (pattern, level) in Rules)
            if (pattern.IsMatch(title))
                return level;

        return null;
    }
}
