using System.Text.RegularExpressions;

namespace JobIntelligence.Infrastructure.Parsing;

public static class SalaryParser
{
    public record ParsedSalary(
        decimal? Min, decimal? Max, string? Currency, string? Period, bool Disclosed);

    // Period keywords that may appear after (or around) a number
    private static readonly string PeriodPattern =
        @"(?:\s*(?:[-–]\s*)?(?<period>per\s+year|per\s+annum|annually|annual|a\s+year|\/year|\/yr|per\s+hour|\/hour|\/hr|per\s+hr|hourly(?:\s+rate)?|\bhour\b|per\s+month|\/month|\/mo|weekly))?";

    // Hourly anchor used by range/single hourly regexes (allows space between / and keyword)
    private static readonly string HourlyAnchor =
        @"\s*(?<period>per\s+hour|\/\s*hour|\/\s*hr|per\s+hr|hourly(?:\s+rate)?|\bhour\b)";

    private const string RangeSep = @"(?:[-–—]|to|and)";

    // $120k–$150k, £50,000 - £70,000, between $85k and $122k
    private static readonly Regex RangeWithSymbol = new(
        @"(?<sym>[$£€¥])\s*(?<min>\d[\d,.]*)\s*(?<mink>[kK])?\s*" + RangeSep + @"\s*[$£€¥]?\s*(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 120,000–150,000 USD  100k-200k GBP
    private static readonly Regex RangeWithCode = new(
        @"(?<min>\d[\d,.]*)\s*(?<mink>[kK])?\s*" + RangeSep + @"\s*(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?\s*(?<code>USD|GBP|EUR|CAD|AUD)" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // USD 70,000 - USD 72,000 - yearly  (SmartRecruiters format)
    private static readonly Regex RangeWithCodePrefix = new(
        @"(?<code>USD|GBP|EUR|CAD|AUD)\s+(?<min>\d[\d,.]*)\s*(?<mink>[kK])?\s*" + RangeSep + @"\s*(?:(?:USD|GBP|EUR|CAD|AUD)\s+)?(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 27.79 - 35.00/hr  $22 to $30/hour
    private static readonly Regex RangeHourly = new(
        @"(?<sym>[$£€¥])?\s*(?<min>\d[\d,.]*)\s*" + RangeSep + @"\s*(?<sym2>[$£€¥])?\s*(?<max>\d[\d,.]*)" + HourlyAnchor,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Min $17.00 - Max $17.00  Min 18.45 - Max 18.45
    private static readonly Regex MinMaxLabel = new(
        @"[Mm]in(?:imum)?\s*[$£€¥]?\s*(?<min>\d[\d,.]*)\s*(?<mink>[kK])?[\s\-–]*[Mm]ax(?:imum)?\s*[$£€¥]?\s*(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Compensation Min: $70,840 ... Compensation Max: $118,030
    private static readonly Regex CompMinMax = new(
        @"[Cc]ompensation\s+[Mm]in[:\s]+[$£€¥]?\s*(?<min>\d[\d,.]*)\s*(?<mink>[kK])?.*?[Cc]ompensation\s+[Mm]ax[:\s]+[$£€¥]?\s*(?<max>\d[\d,.]*)\s*(?<maxk>[kK])?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    // $140,000  $100K  £50k
    private static readonly Regex SingleWithSymbol = new(
        @"(?<sym>[$£€¥])\s*(?<amount>\d[\d,.]*)\s*(?<amountk>[kK])?" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 140,000 USD  100K GBP
    private static readonly Regex SingleWithCode = new(
        @"(?<amount>\d[\d,.]*)\s*(?<amountk>[kK])?\s*(?<code>USD|GBP|EUR|CAD|AUD)" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // USD 17 - hourly  up to USD 40 - hourly
    private static readonly Regex SingleWithCodePrefix = new(
        @"(?:up\s+to\s+)?(?<code>USD|GBP|EUR|CAD|AUD)\s+(?<amount>\d[\d,.]*)\s*(?<amountk>[kK])?" + PeriodPattern,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 27.79/hr  15/hour  $18.00 / hour
    private static readonly Regex SingleHourly = new(
        @"(?<sym>[$£€¥])?\s*(?<amount>\d[\d,.]*)" + HourlyAnchor,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Detects hourly context words anywhere in the text (before the number)
    private static readonly Regex HourlyContextPattern = new(
        @"\bhourly\b|\bper\s+hour\b|\brate\s+per\s+hour\b|\/hr\b|\/hour\b|\bhour(?:ly)?\s+rate\b|\bhourly\s+range\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedSalary Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedSalary(null, null, null, null, false);

        bool hourlyContext = HourlyContextPattern.IsMatch(text);

        // 1. Range with symbol
        var m = RangeWithSymbol.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value) ?? (hourlyContext && IsHourlyAmount(min) ? "hourly" : null);
            if (IsPlausibleRange(min, max, period, hasCurrency: true))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max, ResolveCurrency(m.Groups["sym"].Value, ""), period, true);
            }
        }

        // 2. Range with trailing code
        m = RangeWithCode.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value) ?? (hourlyContext && IsHourlyAmount(min) ? "hourly" : null);
            if (IsPlausibleRange(min, max, period, hasCurrency: true))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max, m.Groups["code"].Value.ToUpperInvariant(), period, true);
            }
        }

        // 3. Range with leading code prefix (USD X - USD Y - period)
        m = RangeWithCodePrefix.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleRange(min, max, period, hasCurrency: true))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max, m.Groups["code"].Value.ToUpperInvariant(), period, true);
            }
        }

        // 4. Hourly range anchored by /hr keyword
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
                return new ParsedSalary(min, max, ResolveCurrency(sym, ""), "hourly", true);
            }
        }

        // 5. Min/Max label format
        m = MinMaxLabel.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = hourlyContext && IsHourlyAmount(min) ? "hourly" : null;
            if (IsPlausibleRange(min, max, period))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max, null, period, true);
            }
        }

        // 6. Compensation Min/Max label
        m = CompMinMax.Match(text);
        if (m.Success)
        {
            var min = Normalize(m.Groups["min"].Value, m.Groups["mink"].Value);
            var max = Normalize(m.Groups["max"].Value, m.Groups["maxk"].Value);
            var period = hourlyContext && IsHourlyAmount(min) ? "hourly" : null;
            if (IsPlausibleRange(min, max, period))
            {
                if (min > max) (min, max) = (max, min);
                return new ParsedSalary(min, max, null, period, true);
            }
        }

        // 7. Single with symbol
        m = SingleWithSymbol.Match(text);
        if (m.Success)
        {
            var amount = Normalize(m.Groups["amount"].Value, m.Groups["amountk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value) ?? (hourlyContext && IsHourlyAmount(amount) ? "hourly" : null);
            if (IsPlausibleSingle(amount, period, hasCurrency: true))
                return new ParsedSalary(amount, null, ResolveCurrency(m.Groups["sym"].Value, ""), period, true);
        }

        // 8. Single with trailing code
        m = SingleWithCode.Match(text);
        if (m.Success)
        {
            var amount = Normalize(m.Groups["amount"].Value, m.Groups["amountk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value) ?? (hourlyContext && IsHourlyAmount(amount) ? "hourly" : null);
            if (IsPlausibleSingle(amount, period, hasCurrency: true))
                return new ParsedSalary(amount, null, m.Groups["code"].Value.ToUpperInvariant(), period, true);
        }

        // 9. Single with leading code prefix (USD 17 - hourly)
        m = SingleWithCodePrefix.Match(text);
        if (m.Success)
        {
            var amount = Normalize(m.Groups["amount"].Value, m.Groups["amountk"].Value);
            var period = ResolvePeriod(m.Groups["period"].Value);
            if (IsPlausibleSingle(amount, period, hasCurrency: true))
                return new ParsedSalary(amount, null, m.Groups["code"].Value.ToUpperInvariant(), period, true);
        }

        // 10. Single hourly anchored by /hr keyword
        m = SingleHourly.Match(text);
        if (m.Success)
        {
            var amount = ParseDecimal(m.Groups["amount"].Value);
            if (amount.HasValue && amount >= 1)
            {
                var sym = m.Groups["sym"].Success ? m.Groups["sym"].Value : "";
                return new ParsedSalary(amount, null, ResolveCurrency(sym, ""), "hourly", true);
            }
        }

        return new ParsedSalary(null, null, null, null, false);
    }

    private static bool IsHourlyAmount(decimal? amount) => amount.HasValue && amount >= 1 && amount < 1000;

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

    // hasCurrency = true when a $ symbol or currency code anchors the match — any amount >= 1 is plausible
    private static bool IsPlausibleRange(decimal? min, decimal? max, string? period, bool hasCurrency = false) =>
        min.HasValue && max.HasValue && min >= 1 && max >= 1 &&
        (hasCurrency || period == "hourly" || (min >= 1000 && max >= 1000));

    private static bool IsPlausibleSingle(decimal? amount, string? period, bool hasCurrency = false) =>
        amount.HasValue && amount >= 1 &&
        (hasCurrency || period == "hourly" || amount >= 1000);

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
        var lower = raw.ToLowerInvariant().Trim();
        if (lower.Contains("hour") || lower.Contains("hr")) return "hourly";
        if (lower.Contains("month") || lower.Contains("mo")) return "monthly";
        if (lower.Contains("week")) return "weekly";
        return "annual";
    }
}
