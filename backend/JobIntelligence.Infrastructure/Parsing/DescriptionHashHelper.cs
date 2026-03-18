using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static partial class DescriptionHashHelper
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string? Compute(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = WhitespaceRegex().Replace(text.ToLowerInvariant(), " ").Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
