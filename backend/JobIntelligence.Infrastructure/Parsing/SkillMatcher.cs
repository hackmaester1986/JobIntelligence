using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class SkillMatcher
{
    public record SkillMatch(int SkillId, string CanonicalName);

    public record SkillEntry(int Id, string CanonicalName, string[] Aliases);

    public static List<SkillMatch> Match(string? text, IReadOnlyList<SkillEntry> taxonomy)
    {
        if (string.IsNullOrWhiteSpace(text) || taxonomy.Count == 0)
            return [];

        // Strip HTML tags if any slipped through
        var clean = Regex.Replace(text, "<[^>]+>", " ");
        var results = new List<SkillMatch>();

        foreach (var skill in taxonomy)
        {
            var terms = new[] { skill.CanonicalName }.Concat(skill.Aliases)
                .Where(t => !string.IsNullOrWhiteSpace(t));

            foreach (var term in terms)
            {
                if (IsMatch(clean, term))
                {
                    results.Add(new SkillMatch(skill.Id, skill.CanonicalName));
                    break; // matched this skill, move to next
                }
            }
        }

        return results;
    }

    private static bool IsMatch(string text, string term)
    {
        // Word boundary works well for normal words.
        // For short or symbol-containing terms (C#, C++, F#, Go, R) use a stricter
        // look-around that also excludes digits and underscores on either side so that
        // codes like "C#1234" or "job-code-C#2" don't produce false positives.
        var pattern = term.Length >= 3 && !term.Any(c => !char.IsLetterOrDigit(c))
            ? $@"\b{Regex.Escape(term)}\b"
            : $@"(?<![a-zA-Z0-9_]){Regex.Escape(term)}(?![a-zA-Z0-9_])";

        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
