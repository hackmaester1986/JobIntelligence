using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class SalaryParser
{
    public record ParsedSalary(
        decimal? Min, decimal? Max, string? Currency, string? Period, bool Disclosed);

    private static readonly string PeriodPattern =
        @"(?:\s*(?<period>per\s+year|per\s+annum|annually|\/year|\/yr|per\s+hour|\/hour|\/hr|per\s+month|\/month|\/mo))?";

    private static readonly string HourlyAnchor =
        @"\s*(?<period>per\s+hour|\/\s*hour|\/\s*hr|per\s+hr)";

    // Range anchored by a leading currency symbol: $120k–$150k, £50,000 - £70,000
    private static readonly Regex RangeWithSymbol = new(
        @"(?<sym>[$£€¥])\s*(?<min>\d[\d,.]*)\s*(?<mink>[kK])?\s*(?:[-–—]|to)\s*[$£€¥]?\s*(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?" +
        PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Range anchored by a trailing currency code: 120,000–150,000 USD
    private static readonly Regex RangeWithCode = new(
        @"(?<min>\d[\d,]*)\s*(?<mink>[kK])?\s*(?:[-–—]|to)\s*(?<max>\d[\d,]*)\s*(?<maxk>[kK])?\s*(?<code>USD|GBP|EUR|CAD|AUD)" +
        PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Hourly range anchored by period keyword, no currency required: 27.79 - 35.00/hr
    private static readonly Regex RangeHourly = new(
        @"(?<sym>[$£€¥])?\s*(?<min>\d[\d,.]*)\s*(?:[-–—]|to)\s*(?<sym2>[$£€¥])?\s*(?<max>\d[\d,.]*)" +
        HourlyAnchor,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Single value with currency symbol (no period required): $140,000  $100K  £50k
    private static readonly Regex SingleWithSymbol = new(
        @"(?<sym>[$£€¥])\s*(?<amount>\d[\d,.]*)\s*(?<amountk>[kK])?" +
        PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Single value with trailing currency code: 140,000 USD  100K GBP
    private static readonly Regex SingleWithCode = new(
        @"(?<amount>\d[\d,]*)\s*(?<amountk>[kK])?\s*(?<code>USD|GBP|EUR|CAD|AUD)" +
        PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Single hourly anchored by period keyword, no currency required: 27.79/hr  15/hour
    private static readonly Regex SingleHourly = new(
        @"(?<sym>[$£€¥])?\s*(?<amount>\d[\d,.]*)" + HourlyAnchor,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedSalary Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedSalary(null, null, null, null, false);

        // 1. Range with symbol
        var m = RangeWithSymbol.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleRange(min, max, period))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max,
                    ResolveCurrency(m.Groups["sym"].Value, ""), period, true);
            }
        }

        // 2. Range with code
        m = RangeWithCode.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleRange(min, max, period))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max,
                    m.Groups["code"].Value.ToUpperInvariant(), period, true);
            }
        }

        // 3. Hourly range (no currency required, anchored by /hr keyword)
        m = RangeHourly.Match(text);
        if (m.Success)
        {
            var min = ParseDecimal(m.Groups["min"].Value);
            var max = ParseDecimal(m.Groups["max"].Value);
            if (min.HasValue && max.HasValue && min >= 1 && max >= 1)
            {
                if (min > max) (min, max) = (max, min);
                var sym = m.Groups["sym"].Success ? m.Groups["sym"].Value
                        : m.Groups["sym2"].Success ? m.Groups["sym2"].Value : "";
                return new ParsedSalary(min, max,
                    ResolveCurrency(sym, ""), "hourly", true);
            }
        }

        // 4. Single with symbol
        m = SingleWithSymbol.Match(text);
        if (m.Success)
        {
            var amount = Normalize(m.Groups["amount"].Value, m.Groups["amountk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleSingle(amount, period))
                return new ParsedSalary(amount, null,
                    ResolveCurrency(m.Groups["sym"].Value, ""), period, true);
        }

        // 5. Single with code
        m = SingleWithCode.Match(text);
        if (m.Success)
        {
            var amount = Normalize(m.Groups["amount"].Value, m.Groups["amountk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleSingle(amount, period))
                return new ParsedSalary(amount, null,
                    m.Groups["code"].Value.ToUpperInvariant(), period, true);
        }

        // 6. Single hourly (no currency required, anchored by /hr keyword)
        m = SingleHourly.Match(text);
        if (m.Success)
        {
            var amount = ParseDecimal(m.Groups["amount"].Value);
            if (amount.HasValue && amount >= 1)
            {
                var sym = m.Groups["sym"].Success ? m.Groups["sym"].Value : "";
                return new ParsedSalary(amount, null,
                    ResolveCurrency(sym, ""), "hourly", true);
            }
        }

        return new ParsedSalary(null, null, null, null, false);
    }

    private static decimal? ParseDecimal(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Replace(",", "");
        return decimal.TryParse(cleaned, out var v) ? v : null;
    }

    private static decimal? Normalize(string raw, string kSuffix)
    {
        var v = ParseDecimal(raw);
        if (!v.HasValue) return null;
        if (!string.IsNullOrEmpty(kSuffix) && v < 10_000)
            return v * 1000;
        return v;
    }

    // Hourly: min >= 1 (min wage). Annual/unknown: min >= 1000 to filter "3-5 years"
    private static bool IsPlausibleRange(decimal? min, decimal? max, string? period) =>
        min.HasValue && max.HasValue &&
        (period == "hourly" ? min >= 1 && max >= 1 : min >= 1000 && max >= 1000);

    // Hourly: amount >= 1. Annual/unknown: amount >= 1000
    private static bool IsPlausibleSingle(decimal? amount, string? period) =>
        amount.HasValue &&
        (period == "hourly" ? amount >= 1 : amount >= 1000);

    private static string? ResolveCurrency(string sym, string code)
    {
        if (!string.IsNullOrWhiteSpace(code)) return code.ToUpperInvariant();
        return sym.Trim() switch
        {
            "$" => "USD",
            "£" => "GBP",
            "€" => "EUR",
            "¥" => "JPY",
            _ => null
        };
    }

    private static string? ResolvePeriod(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("hour") || lower.Contains("hr")) return "hourly";
        if (lower.Contains("month") || lower.Contains("mo")) return "monthly";
        return "annual";
    }
}
