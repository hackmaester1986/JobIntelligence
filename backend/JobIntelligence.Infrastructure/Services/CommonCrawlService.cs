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
        ["recruitee"]        = "*.recruitee.com",
    };

    public async Task<CommonCrawlResult> DiscoverSlugsAsync(string? source = null, CancellationToken ct = default)
    {
        if (source != null && !SourceDomains.ContainsKey(source))
            throw new ArgumentException($"Unknown source '{source}'. Valid values: {string.Join(", ", SourceDomains.Keys)}");

        var indexes = await GetRecentIndexesAsync(3, ct);
        logger.LogInformation("Using Common Crawl indexes: {Indexes}", string.Join(", ", indexes));

        var leverSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var greenhouseSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ashbySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var smartRecruitersSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recruiteeSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Keyed by host so we only keep one career site per company host
        var workdayEntries = new Dictionary<string, WorkdayEntry>(StringComparer.OrdinalIgnoreCase);

        bool ShouldCrawl(string name) => source == null || source.Equals(name, StringComparison.OrdinalIgnoreCase);

        // Try each index in order (newest first); stop per-source as soon as one succeeds.
        // This handles the common case where the newest index CDX backend is overloaded.
        foreach (var indexId in indexes)
        {
            bool anySucceeded = false;

            if (ShouldCrawl("greenhouse") && greenhouseSlugs.Count == 0)
            {
                var gh1 = await QueryDomainAsync(indexId, "boards.greenhouse.io", ct);
                var gh2 = await QueryDomainAsync(indexId, "job-boards.greenhouse.io", ct);
                if (gh1.Count > 0 || gh2.Count > 0)
                {
                    foreach (var s in gh1) greenhouseSlugs.Add(s);
                    foreach (var s in gh2) greenhouseSlugs.Add(s);
                    anySucceeded = true;
                    logger.LogInformation("CDX [greenhouse] succeeded on index {Index}: {Count} slugs", indexId, greenhouseSlugs.Count);
                }
                else
                {
                    logger.LogWarning("CDX [greenhouse] got 0 results from index {Index}, will try next", indexId);
                }
            }

            if (ShouldCrawl("lever") && leverSlugs.Count == 0)
            {
                var lv = await QueryDomainAsync(indexId, "jobs.lever.co", ct);
                if (lv.Count > 0) { foreach (var s in lv) leverSlugs.Add(s); anySucceeded = true;
                    logger.LogInformation("CDX [lever] succeeded on index {Index}: {Count} slugs", indexId, leverSlugs.Count); }
                else logger.LogWarning("CDX [lever] got 0 results from index {Index}, will try next", indexId);
            }

            if (ShouldCrawl("ashby") && ashbySlugs.Count == 0)
            {
                var ab = await QueryDomainAsync(indexId, "jobs.ashbyhq.com", ct);
                if (ab.Count > 0) { foreach (var s in ab) ashbySlugs.Add(s); anySucceeded = true;
                    logger.LogInformation("CDX [ashby] succeeded on index {Index}: {Count} slugs", indexId, ashbySlugs.Count); }
                else logger.LogWarning("CDX [ashby] got 0 results from index {Index}, will try next", indexId);
            }

            if (ShouldCrawl("smartrecruiters") && smartRecruitersSlugs.Count == 0)
            {
                var sr1 = await QueryDomainAsync(indexId, "careers.smartrecruiters.com", ct);
                var sr2 = await QueryDomainAsync(indexId, "jobs.smartrecruiters.com", ct);
                if (sr1.Count > 0 || sr2.Count > 0)
                {
                    foreach (var s in sr1) smartRecruitersSlugs.Add(s);
                    foreach (var s in sr2) smartRecruitersSlugs.Add(s);
                    anySucceeded = true;
                    logger.LogInformation("CDX [smartrecruiters] succeeded on index {Index}: {Count} slugs", indexId, smartRecruitersSlugs.Count);
                }
                else logger.LogWarning("CDX [smartrecruiters] got 0 results from index {Index}, will try next", indexId);
            }

            if (ShouldCrawl("workday") && workdayEntries.Count == 0)
            {
                var wd = await QueryWorkdayAsync(indexId, ct);
                if (wd.Count > 0)
                {
                    foreach (var e in wd) workdayEntries.TryAdd(e.Host, e);
                    anySucceeded = true;
                    logger.LogInformation("CDX [workday] succeeded on index {Index}: {Count} tenants", indexId, workdayEntries.Count);
                }
                else logger.LogWarning("CDX [workday] got 0 results from index {Index}, will try next", indexId);
            }

            if (ShouldCrawl("recruitee") && recruiteeSlugs.Count == 0)
            {
                var rc = await QueryRecruiteeAsync(indexId, ct);
                if (rc.Count > 0)
                {
                    foreach (var s in rc) recruiteeSlugs.Add(s);
                    anySucceeded = true;
                    logger.LogInformation("CDX [recruitee] succeeded on index {Index}: {Count} slugs", indexId, recruiteeSlugs.Count);
                }
                else logger.LogWarning("CDX [recruitee] got 0 results from index {Index}, will try next", indexId);
            }

            // If nothing returned results from this index, it's likely overloaded — try next immediately
            if (!anySucceeded)
            {
                logger.LogWarning("CDX index {Index} returned nothing for all requested sources, trying next index", indexId);
                continue;
            }

            // If all requested sources have data, no need to try further indexes
            bool allDone = (!ShouldCrawl("greenhouse") || greenhouseSlugs.Count > 0)
                && (!ShouldCrawl("lever") || leverSlugs.Count > 0)
                && (!ShouldCrawl("ashby") || ashbySlugs.Count > 0)
                && (!ShouldCrawl("smartrecruiters") || smartRecruitersSlugs.Count > 0)
                && (!ShouldCrawl("workday") || workdayEntries.Count > 0)
                && (!ShouldCrawl("recruitee") || recruiteeSlugs.Count > 0);

            if (allDone) break;

            await Task.Delay(10000, ct); // pause between indexes
        }

        logger.LogInformation(
            "Common Crawl discovery complete: {G} Greenhouse, {L} Lever, {A} Ashby, {SR} SmartRecruiters, {WD} Workday, {RC} Recruitee",
            greenhouseSlugs.Count, leverSlugs.Count, ashbySlugs.Count,
            smartRecruitersSlugs.Count, workdayEntries.Count, recruiteeSlugs.Count);

        return new CommonCrawlResult(
            [.. greenhouseSlugs], [.. leverSlugs], [.. ashbySlugs],
            [.. smartRecruitersSlugs], [.. workdayEntries.Values], [.. recruiteeSlugs]);
    }

    private async Task<List<string>> GetRecentIndexesAsync(int count, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("CommonCrawl");
        logger.LogInformation("Fetching Common Crawl collinfo.json");
        var json = await client.GetStringAsync("collinfo.json", ct);
        using var doc = JsonDocument.Parse(json);
        // collinfo.json is sorted newest first
        var indexes = doc.RootElement.EnumerateArray()
            .Take(count)
            .Select(e => e.GetProperty("id").GetString()!)
            .ToList();
        logger.LogInformation("Common Crawl indexes selected: {Indexes}", string.Join(", ", indexes));
        return indexes;
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

    // Recruitee: job boards live on subdomains ({slug}.recruitee.com), so we query the
    // wildcard domain and extract the subdomain as the slug (same pattern as Workday).
    private async Task<List<string>> QueryRecruiteeAsync(string indexId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("CommonCrawl");
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string baseQuery = "url=*.recruitee.com&output=json&fl=url";

        var numPagesText = await FetchWithRetryAsync(client,
            $"{indexId}-index?{baseQuery}&showNumPages=true", "recruitee.com", -1, ct);
        if (numPagesText == null) return [.. slugs];

        int totalPages = 1;
        try
        {
            using var meta = JsonDocument.Parse(numPagesText.Trim());
            if (meta.RootElement.TryGetProperty("pages", out var p))
                totalPages = p.GetInt32();
        }
        catch { }

        logger.LogInformation("CDX [Recruitee] {Index}: {Total} pages", indexId, totalPages);

        for (int page = 0; page < totalPages; page++)
        {
            var responseText = await FetchWithRetryAsync(client,
                $"{indexId}-index?{baseQuery}&page={page}", "recruitee.com", page, ct);
            if (responseText == null) break;

            foreach (var line in responseText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("url", out var urlProp)) continue;
                    var rawUrl = urlProp.GetString();
                    if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) continue;

                    var host = uri.Host.ToLowerInvariant();
                    if (!host.EndsWith(".recruitee.com", StringComparison.OrdinalIgnoreCase)) continue;
                    var slug = host[..host.IndexOf(".recruitee.com", StringComparison.OrdinalIgnoreCase)];
                    if (string.IsNullOrEmpty(slug) || slug == "www" || slug == "api" || slug == "cdn") continue;
                    if (slug.Length < 2 || IgnoredSlugs.Contains(slug)) continue;

                    slugs.Add(slug);
                }
                catch { }
            }

            logger.LogInformation("CDX [Recruitee] {Index}: page {Page}/{Total}, {Count} unique slugs",
                indexId, page + 1, totalPages, slugs.Count);

            if (page < totalPages - 1)
                await Task.Delay(5000, ct);
        }

        return [.. slugs];
    }

    private async Task<string?> FetchWithRetryAsync(
        HttpClient client, string url, string domain, int page, CancellationToken ct)
    {
        int[] backoff = [15000, 30000, 60000];
        logger.LogInformation("CDX fetch: {Url}", url);
        for (int attempt = 0; attempt <= backoff.Length; attempt++)
        {
            try
            {
                var response = await client.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                var status = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(ct);
                if ((status == 429 || status == 500 || status == 502 || status == 503 || status == 504) && attempt < backoff.Length)
                {
                    logger.LogWarning("CDX API {Status} for {Domain} page {Page} (attempt {A}), retrying in {Delay}ms. Body: {Body}",
                        status, domain, page, attempt + 1, backoff[attempt], body[..Math.Min(200, body.Length)]);
                    await Task.Delay(backoff[attempt], ct);
                    continue;
                }

                logger.LogWarning("CDX API returned {Status} for {Domain} page {Page}. Body: {Body}",
                    status, domain, page, body[..Math.Min(500, body.Length)]);
                return null;
            }
            catch (Exception ex) when (attempt < backoff.Length)
            {
                logger.LogWarning(ex, "CDX API request failed for {Domain} page {Page} (attempt {A}), retrying in {Delay}ms",
                    domain, page, attempt + 1, backoff[attempt]);
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
