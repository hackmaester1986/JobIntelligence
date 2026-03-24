using System.Text.Json;
using JobIntelligence.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class CommonCrawlService(
    IHttpClientFactory httpClientFactory,
    ILogger<CommonCrawlService> logger) : ICommonCrawlService
{
    private static readonly HashSet<string> IgnoredSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "privacy", "terms", "about", "careers", "embed", "jobs", "api", "apply",
        "static", "assets", "cdn", "img", "images", "css", "js", "fonts",
        "favicon.ico", "robots.txt", "sitemap.xml", "sitemap", "feed",
        "login", "logout", "signup", "auth", "oauth", "sso",
    };

    private static readonly Dictionary<string, string> SourceDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greenhouse"]       = "boards.greenhouse.io",
        ["lever"]            = "jobs.lever.co",
        ["ashby"]            = "jobs.ashbyhq.com",
        ["smartrecruiters"]  = "careers.smartrecruiters.com",
        ["workday"]          = "*.myworkdayjobs.com",
    };

    public async Task<CommonCrawlResult> DiscoverSlugsAsync(string? source = null, CancellationToken ct = default)
    {
        if (source != null && !SourceDomains.ContainsKey(source))
            throw new ArgumentException($"Unknown source '{source}'. Valid values: {string.Join(", ", SourceDomains.Keys)}");

        var indexes = await GetRecentIndexesAsync(1, ct);
        logger.LogInformation("Using Common Crawl indexes: {Indexes}", string.Join(", ", indexes));

        var leverSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var greenhouseSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ashbySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var smartRecruitersSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Keyed by host so we only keep one career site per company host
        var workdayEntries = new Dictionary<string, WorkdayEntry>(StringComparer.OrdinalIgnoreCase);

        bool ShouldCrawl(string name) => source == null || source.Equals(name, StringComparison.OrdinalIgnoreCase);

        foreach (var indexId in indexes)
        {
            if (ShouldCrawl("greenhouse"))
            {
                var gh = await QueryDomainAsync(indexId, "boards.greenhouse.io", ct);
                foreach (var s in gh) greenhouseSlugs.Add(s);
            }

            if (ShouldCrawl("lever"))
            {
                var lv = await QueryDomainAsync(indexId, "jobs.lever.co", ct);
                foreach (var s in lv) leverSlugs.Add(s);
            }

            if (ShouldCrawl("ashby"))
            {
                var ab = await QueryDomainAsync(indexId, "jobs.ashbyhq.com", ct);
                foreach (var s in ab) ashbySlugs.Add(s);
            }

            if (ShouldCrawl("smartrecruiters"))
            {
                var sr = await QueryDomainAsync(indexId, "careers.smartrecruiters.com", ct);
                foreach (var s in sr) smartRecruitersSlugs.Add(s);
            }

            if (ShouldCrawl("workday"))
            {
                var wd = await QueryWorkdayAsync(indexId, ct);
                foreach (var e in wd)
                    workdayEntries.TryAdd(e.Host, e);
            }

            await Task.Delay(10000, ct); // pause between indexes
        }

        logger.LogInformation(
            "Common Crawl discovery complete: {G} Greenhouse, {L} Lever, {A} Ashby, {SR} SmartRecruiters, {WD} Workday",
            greenhouseSlugs.Count, leverSlugs.Count, ashbySlugs.Count,
            smartRecruitersSlugs.Count, workdayEntries.Count);

        return new CommonCrawlResult(
            [.. greenhouseSlugs], [.. leverSlugs], [.. ashbySlugs],
            [.. smartRecruitersSlugs], [.. workdayEntries.Values]);
    }

    private async Task<List<string>> GetRecentIndexesAsync(int count, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("CommonCrawl");
        var json = await client.GetStringAsync("collinfo.json", ct);
        using var doc = JsonDocument.Parse(json);
        // collinfo.json is sorted newest first
        return [.. doc.RootElement.EnumerateArray()
            .Take(count)
            .Select(e => e.GetProperty("id").GetString()!)];
    }

    private async Task<List<string>> QueryDomainAsync(
        string indexId, string domain, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("CommonCrawl");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resumeKey = null;
        int page = 0;
        const int maxPages = 200;
        const int pageSize = 1000;

        while (page < maxPages)
        {
            var url = $"{indexId}-index?url={domain}/*&output=json&fl=url&limit={pageSize}&showResumeKey=true";
            if (resumeKey != null)
                url += $"&resume={Uri.EscapeDataString(resumeKey)}";

            var responseText = await FetchWithRetryAsync(client, url, domain, page, ct);
            if (responseText == null) break;

            resumeKey = null;
            var lines = responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    // Resume key line looks like: {"resumeKey":"..."}
                    if (doc.RootElement.TryGetProperty("resumeKey", out var rk))
                    {
                        resumeKey = rk.GetString();
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("url", out var urlProp))
                    {
                        var slug = ExtractSlug(urlProp.GetString(), domain);
                        if (slug != null) slugs.Add(slug);
                    }
                }
                catch { /* skip malformed lines */ }
            }

            page++;
            logger.LogInformation("CDX [{Domain}]: page {Page}, {Count} unique slugs", domain, page, slugs.Count);

            if (resumeKey == null) break; // no more pages

            await Task.Delay(5000, ct); // polite delay between CDX pages
        }

        return [.. slugs];
    }

    // Workday: tenants live in subdomains ({tenant}.wd1.myworkdayjobs.com), so we
    // query the wildcard domain and extract the subdomain rather than the path.
    // Uses page-based CDX pagination (showNumPages + &page=N) because showResumeKey
    // is not returned for wildcard queries and results would be truncated to ~1 page.
    private async Task<List<WorkdayEntry>> QueryWorkdayAsync(string indexId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("CommonCrawl");
        var entries = new Dictionary<string, WorkdayEntry>(StringComparer.OrdinalIgnoreCase);
        const string baseQuery = "url=*.myworkdayjobs.com&output=json&fl=url";

        // Step 1: find out how many pages exist for this index
        var numPagesText = await FetchWithRetryAsync(client,
            $"{indexId}-index?{baseQuery}&showNumPages=true", "myworkdayjobs.com", -1, ct);
        if (numPagesText == null) return [.. entries.Values];

        int totalPages = 1;
        try
        {
            using var meta = JsonDocument.Parse(numPagesText.Trim());
            if (meta.RootElement.TryGetProperty("pages", out var p))
                totalPages = p.GetInt32();
        }
        catch { /* default to 1 page */ }

        logger.LogInformation("CDX [Workday] {Index}: {Total} pages", indexId, totalPages);

        // Step 2: fetch each page
        for (int page = 0; page < totalPages; page++)
        {
            var responseText = await FetchWithRetryAsync(client,
                $"{indexId}-index?{baseQuery}&page={page}", "myworkdayjobs.com", page, ct);
            if (responseText == null) break;

            foreach (var line in responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("url", out var urlProp))
                    {
                        var entry = ExtractWorkdayEntry(urlProp.GetString());
                        if (entry != null)
                        {
                            // Prefer entries with a longer career site path — root URLs may give
                            // a short/wrong path while job-detail URLs give the full career site
                            if (!entries.TryGetValue(entry.Host, out var existing) ||
                                entry.CareerSite.Length > existing.CareerSite.Length)
                                entries[entry.Host] = entry;
                        }
                    }
                }
                catch { /* skip malformed lines */ }
            }

            logger.LogInformation("CDX [Workday] {Index}: page {Page}/{Total}, {Count} unique tenants",
                indexId, page + 1, totalPages, entries.Count);

            if (page < totalPages - 1)
                await Task.Delay(5000, ct);
        }

        return [.. entries.Values];
    }

    private static WorkdayEntry? ExtractWorkdayEntry(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();

        // Must be a subdomain of myworkdayjobs.com (e.g. amazon.wd1.myworkdayjobs.com)
        // Reject bare myworkdayjobs.com or www.myworkdayjobs.com
        if (!host.EndsWith(".myworkdayjobs.com", StringComparison.OrdinalIgnoreCase)) return null;

        var subdomain = host[..host.IndexOf(".myworkdayjobs.com", StringComparison.OrdinalIgnoreCase)];
        if (string.IsNullOrEmpty(subdomain) || subdomain == "www") return null;

        // Extract career site path: segments before job-detail segments
        // Strip leading locale segment (e.g. "en-US", "es") — the CXS API path doesn't include it
        // Stop at "job", "details", "jobDetails", or any file segment (e.g. robots.txt)
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var careerSegments = segments
            .SkipWhile(s => LocaleSegment.IsMatch(s))
            .TakeWhile(s =>
                !s.Equals("details", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("job", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("jobDetails", StringComparison.OrdinalIgnoreCase) &&
                !s.Contains('.'));
        var careerSite = string.Join("/", careerSegments);

        // Need at least one path segment to form a valid career site
        if (string.IsNullOrEmpty(careerSite)) return null;

        return new WorkdayEntry(host, careerSite);
    }

    private async Task<string?> FetchWithRetryAsync(
        HttpClient client, string url, string domain, int page, CancellationToken ct)
    {
        int[] backoff = [3000, 6000, 12000];
        for (int attempt = 0; attempt <= backoff.Length; attempt++)
        {
            try
            {
                var response = await client.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                var status = (int)response.StatusCode;
                if ((status == 504 || status == 429) && attempt < backoff.Length)
                {
                    logger.LogWarning("CDX API {Status} for {Domain} page {Page}, retrying in {Delay}ms",
                        status, domain, page, backoff[attempt]);
                    await Task.Delay(backoff[attempt], ct);
                    continue;
                }

                logger.LogWarning("CDX API returned {Status} for {Domain} page {Page}", status, domain, page);
                return null;
            }
            catch (Exception ex) when (attempt < backoff.Length)
            {
                logger.LogWarning(ex, "CDX API request failed for {Domain} page {Page}, retrying in {Delay}ms",
                    domain, page, backoff[attempt]);
                await Task.Delay(backoff[attempt], ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CDX API request failed for {Domain} page {Page} after all retries", domain, page);
                return null;
            }
        }
        return null;
    }

    private static string? ExtractSlug(string? url, string domain)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var domainIdx = url.IndexOf(domain, StringComparison.OrdinalIgnoreCase);
        if (domainIdx < 0) return null;

        var afterDomain = url[(domainIdx + domain.Length)..].TrimStart('/');
        if (string.IsNullOrEmpty(afterDomain)) return null;

        // Take first path segment only
        var slug = afterDomain.Split('/')[0].Split('?')[0].Split('#')[0];

        if (string.IsNullOrEmpty(slug) || slug.Length < 2) return null;
        if (IgnoredSlugs.Contains(slug)) return null;

        // Filter file extensions
        if (slug.Contains('.')) return null;

        // Filter UUIDs
        if (Guid.TryParse(slug, out _)) return null;

        // Filter pure numbers or number-heavy strings (job IDs)
        if (slug.All(c => char.IsDigit(c) || c == '-')) return null;

        return slug.ToLowerInvariant();
    }

    // Matches locale segments like "en", "es", "en-US", "zh-CN", "pt-BR"
    private static readonly System.Text.RegularExpressions.Regex LocaleSegment =
        new(@"^[a-z]{2,3}(-[A-Za-z]{2,4})?$", System.Text.RegularExpressions.RegexOptions.Compiled);
}
