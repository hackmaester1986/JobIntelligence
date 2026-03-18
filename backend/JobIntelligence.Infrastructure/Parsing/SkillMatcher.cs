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
        // Use word boundary for terms >= 3 chars, exact case-insensitive for short ones
        var pattern = term.Length >= 3
            ? $@"\b{Regex.Escape(term)}\b"
            : $@"(?<![a-zA-Z]){Regex.Escape(term)}(?![a-zA-Z])";

        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }
}
