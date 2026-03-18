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

    public async Task<CommonCrawlResult> DiscoverSlugsAsync(CancellationToken ct = default)
    {
        var indexes = await GetRecentIndexesAsync(8, ct);
        logger.LogInformation("Using Common Crawl indexes: {Indexes}", string.Join(", ", indexes));

        var leverSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var greenhouseSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ashbySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workableSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var smartRecruitersSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bambooHrSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indexId in indexes)
        {
            var gh = await QueryDomainAsync(indexId, "boards.greenhouse.io", ct);
            foreach (var s in gh) greenhouseSlugs.Add(s);

            var lv = await QueryDomainAsync(indexId, "jobs.lever.co", ct);
            foreach (var s in lv) leverSlugs.Add(s);

            var ab = await QueryDomainAsync(indexId, "jobs.ashbyhq.com", ct);
            foreach (var s in ab) ashbySlugs.Add(s);

            var wk = await QueryDomainAsync(indexId, "apply.workable.com", ct);
            foreach (var s in wk) workableSlugs.Add(s);

            var sr = await QueryDomainAsync(indexId, "careers.smartrecruiters.com", ct);
            foreach (var s in sr) smartRecruitersSlugs.Add(s);

            var bh = await QueryDomainAsync(indexId, "bamboohr.com", ct);
            foreach (var s in bh) bambooHrSlugs.Add(s);

            await Task.Delay(2000, ct); // pause between indexes
        }

        logger.LogInformation(
            "Common Crawl discovery complete: {G} Greenhouse, {L} Lever, {A} Ashby, {W} Workable, {SR} SmartRecruiters, {BH} BambooHR",
            greenhouseSlugs.Count, leverSlugs.Count, ashbySlugs.Count,
            workableSlugs.Count, smartRecruitersSlugs.Count, bambooHrSlugs.Count);

        return new CommonCrawlResult(
            [.. greenhouseSlugs], [.. leverSlugs], [.. ashbySlugs],
            [.. workableSlugs], [.. smartRecruitersSlugs], [.. bambooHrSlugs]);
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

            await Task.Delay(1500, ct); // polite delay between CDX pages
        }

        return [.. slugs];
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
}
