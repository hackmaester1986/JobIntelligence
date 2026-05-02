using System.Text.Json;
using System.Text.Json.Serialization;
using JobIntelligence.API.Filters;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/crawler")]
[AdminKey]
public class CrawlerController(IServiceScopeFactory scopeFactory, ILogger<CrawlerController> logger) : ControllerBase
{
    private static readonly string[] ValidSources = ["greenhouse", "lever", "ashby", "smartrecruiters", "workday", "recruitee", "rippling"];

    public record SlugDiscoveryRequest(
        List<string>? AshbyUrls,
        List<string>? AshbySlugs,
        List<string>? GreenhouseUrls,
        List<string>? GreenhouseSlugs,
        List<string>? LeverUrls,
        List<string>? LeverSlugs,
        List<string>? SmartRecruitersUrls,
        List<string>? SmartRecruitersSlugs);

    [HttpPost("common-crawl")]
    public IActionResult DiscoverViaCommonCrawl([FromQuery] string? source, [FromQuery] bool dryRun = false)
    {
        if (source != null && !ValidSources.Contains(source, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Unknown source '{source}'.", validSources = ValidSources });

        var label = source ?? "all sources";
        logger.LogInformation("Common Crawl discovery triggered for {Source} (dryRun={DryRun})", label, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var crawler = scope.ServiceProvider.GetRequiredService<ICommonCrawlService>();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();

            try
            {
                // Step 1: Discover slugs from Common Crawl index
                var crawlResult = await crawler.DiscoverSlugsAsync(source, CancellationToken.None);
                logger.LogInformation(
                    "Common Crawl found {G} Greenhouse, {L} Lever, {A} Ashby, {SR} SmartRecruiters, {WD} Workday, {RC} Recruitee, {RP} Rippling slugs",
                    crawlResult.GreenhouseSlugs.Count, crawlResult.LeverSlugs.Count, crawlResult.AshbySlugs.Count,
                    crawlResult.SmartRecruitersSlugs.Count, crawlResult.WorkdayEntries.Count, crawlResult.RecruiteeSlugs.Count,
                    crawlResult.RipplingSlugs.Count);

                // Step 2: Validate each slug against live APIs and optionally import
                var result = await discovery.DiscoverFromSlugsAsync(
                    crawlResult.GreenhouseSlugs, crawlResult.LeverSlugs, crawlResult.AshbySlugs,
                    crawlResult.SmartRecruitersSlugs, crawlResult.WorkdayEntries,
                    crawlResult.RecruiteeSlugs, dryRun, CancellationToken.None,
                    crawlResult.RipplingSlugs);

                var sources = new[] { "Greenhouse", "Lever", "Ashby", "SmartRecruiters", "Workday", "Recruitee", "Rippling" };
                foreach (var s in sources)
                {
                    var v = result.ValidatedPerSource.GetValueOrDefault(s);
                    var f = result.FailedPerSource.GetValueOrDefault(s);
                    logger.LogInformation("  {Source}: validated={V} failed={F}", s, v, f);
                }
                logger.LogInformation(
                    "Common Crawl discovery complete (dryRun={DryRun}): validated={A} skipped={S} failed={F}",
                    dryRun, result.Added, result.Skipped, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Common Crawl discovery failed");
            }
        });

        return Accepted(new { message = $"Common Crawl discovery started for {label}", dryRun });
    }

    [HttpPost("url-import")]
    public IActionResult ImportFromUrls([FromBody] List<string> urls, [FromQuery] bool dryRun = false)
    {
        if (urls == null || urls.Count == 0)
            return BadRequest(new { error = "No URLs provided." });

        var categorized = CategorizeUrls(urls);
        var total = categorized.Values.Sum(v => v is List<string> l ? l.Count : ((List<WorkdayEntry>)v).Count);

        if (total == 0)
            return BadRequest(new { error = "No recognizable ATS URLs found. Supported: Greenhouse, Lever, Ashby, SmartRecruiters, Workday, Recruitee, Rippling." });

        var preview = categorized.ToDictionary(
            kv => kv.Key,
            kv => kv.Value is List<WorkdayEntry> wd
                ? (object)wd.Select(e => $"{e.Host}/{e.CareerSite}").ToList()
                : kv.Value);

        logger.LogInformation("URL import triggered: {Total} URLs categorized across {Sources} sources (dryRun={D})",
            total, categorized.Count, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            try
            {
                var gh = categorized.GetValueOrDefault("greenhouse") as List<string> ?? [];
                var lv = categorized.GetValueOrDefault("lever") as List<string> ?? [];
                var ab = categorized.GetValueOrDefault("ashby") as List<string> ?? [];
                var sr = categorized.GetValueOrDefault("smartrecruiters") as List<string> ?? [];
                var wd = categorized.GetValueOrDefault("workday") as List<WorkdayEntry> ?? [];
                var rc = categorized.GetValueOrDefault("recruitee") as List<string> ?? [];
                var rp = categorized.GetValueOrDefault("rippling") as List<string> ?? [];

                var result = await discovery.DiscoverFromSlugsAsync(gh, lv, ab, sr, wd, rc, dryRun, CancellationToken.None, rp);

                logger.LogInformation("URL import complete (dryRun={D}): added={A} skipped={S} failed={F}",
                    dryRun, result.Added, result.Skipped, result.Failed);
                foreach (var kvp in result.ValidatedPerSource)
                    logger.LogInformation("  {Source}: validated={V} failed={F}", kvp.Key, kvp.Value,
                        result.FailedPerSource.GetValueOrDefault(kvp.Key));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "URL import failed");
            }
        });

        return Accepted(new { message = $"URL import started: {total} URLs", dryRun, categorized = preview });
    }

    private static Dictionary<string, object> CategorizeUrls(List<string> urls)
    {
        var greenhouse      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lever           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ashby           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var smartRecruiters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workday         = new Dictionary<string, WorkdayEntry>(StringComparer.OrdinalIgnoreCase);
        var recruitee       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rippling        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in urls)
        {
            var trimmed = raw.Trim();
            // Allow bare slugs passed alongside URLs — treat as unresolvable, skip
            if (!Uri.TryCreate(trimmed.StartsWith("http") ? trimmed : "https://" + trimmed,
                    UriKind.Absolute, out var uri))
                continue;

            var host = uri.Host.ToLowerInvariant();
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (host is "boards.greenhouse.io" or "job-boards.greenhouse.io")
            {
                var slug = segments.FirstOrDefault();
                if (!string.IsNullOrEmpty(slug)) greenhouse.Add(slug.ToLowerInvariant());
            }
            else if (host == "jobs.lever.co")
            {
                var slug = segments.FirstOrDefault();
                if (!string.IsNullOrEmpty(slug)) lever.Add(slug.ToLowerInvariant());
            }
            else if (host == "jobs.ashbyhq.com")
            {
                var slug = segments.FirstOrDefault();
                if (!string.IsNullOrEmpty(slug)) ashby.Add(slug.ToLowerInvariant());
            }
            else if (host is "careers.smartrecruiters.com" or "jobs.smartrecruiters.com")
            {
                var slug = segments.FirstOrDefault();
                if (!string.IsNullOrEmpty(slug)) smartRecruiters.Add(slug.ToLowerInvariant());
            }
            else if (host.EndsWith(".myworkdayjobs.com"))
            {
                var entry = ExtractWorkdayEntryFromUrl(uri);
                if (entry != null) workday.TryAdd(entry.Host, entry);
            }
            else if (host.EndsWith(".recruitee.com"))
            {
                var slug = host[..host.IndexOf(".recruitee.com")];
                if (!string.IsNullOrEmpty(slug) && slug != "www") recruitee.Add(slug.ToLowerInvariant());
            }
            else if (host == "ats.rippling.com")
            {
                // Skip locale segments like en-us, en-gb
                var slug = segments.FirstOrDefault(s =>
                    !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-z]{2}(-[a-z]{2})?$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                if (!string.IsNullOrEmpty(slug) && slug != "jobs") rippling.Add(slug.ToLowerInvariant());
            }
        }

        var result = new Dictionary<string, object>();
        if (greenhouse.Count > 0)      result["greenhouse"]      = greenhouse.ToList();
        if (lever.Count > 0)           result["lever"]           = lever.ToList();
        if (ashby.Count > 0)           result["ashby"]           = ashby.ToList();
        if (smartRecruiters.Count > 0) result["smartrecruiters"] = smartRecruiters.ToList();
        if (workday.Count > 0)         result["workday"]         = workday.Values.ToList();
        if (recruitee.Count > 0)       result["recruitee"]       = recruitee.ToList();
        if (rippling.Count > 0)        result["rippling"]        = rippling.ToList();
        return result;
    }

    private static WorkdayEntry? ExtractWorkdayEntryFromUrl(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var subdomain = host[..host.IndexOf(".myworkdayjobs.com")];
        if (string.IsNullOrEmpty(subdomain) || subdomain == "www") return null;

        var localePattern = new System.Text.RegularExpressions.Regex(@"^[a-z]{2,3}(-[A-Za-z]{2,4})?$");
        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var careerSegments = segments
            .SkipWhile(s => localePattern.IsMatch(s))
            .TakeWhile(s =>
                !s.Equals("details", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("job", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("jobDetails", StringComparison.OrdinalIgnoreCase) &&
                !s.Contains('.'));
        var careerSite = string.Join("/", careerSegments);

        return string.IsNullOrEmpty(careerSite) ? null : new WorkdayEntry(host, careerSite);
    }

    [HttpPost("slugs")]
    public IActionResult DiscoverFromSlugs([FromBody] SlugDiscoveryRequest request, [FromQuery] bool dryRun = false)
    {
        var ashby = ParseSlugs(request.AshbyUrls,   request.AshbySlugs);
        var gh    = ParseSlugs(request.GreenhouseUrls, request.GreenhouseSlugs);
        var lever = ParseSlugs(request.LeverUrls,   request.LeverSlugs);
        var sr    = ParseSlugs(request.SmartRecruitersUrls, request.SmartRecruitersSlugs);

        var total = ashby.Count + gh.Count + lever.Count + sr.Count;
        if (total == 0)
            return BadRequest(new { error = "No slugs or URLs provided." });

        logger.LogInformation(
            "Slug discovery triggered: greenhouse={G} lever={L} ashby={A} smartrecruiters={SR} dryRun={D}",
            gh.Count, lever.Count, ashby.Count, sr.Count, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            try
            {
                var result = await discovery.DiscoverFromSlugsAsync(gh, lever, ashby, sr, [], [], dryRun, CancellationToken.None);
                logger.LogInformation(
                    "Slug discovery complete (dryRun={D}): added={A} skipped={S} failed={F}",
                    dryRun, result.Added, result.Skipped, result.Failed);
                foreach (var kvp in result.ValidatedPerSource)
                    logger.LogInformation("  {Source}: validated={V} failed={F}", kvp.Key, kvp.Value,
                        result.FailedPerSource.GetValueOrDefault(kvp.Key));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Slug discovery failed");
            }
        });

        return Accepted(new { message = $"Slug discovery started: {total} slugs", dryRun, ashby, greenhouse = gh, lever, smartRecruiters = sr });
    }

    private static List<string> ParseSlugs(List<string>? urls, List<string>? slugs)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in urls ?? [])
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) continue;
            var slug = uri.AbsolutePath.Trim('/').Split('/')[0];
            if (!string.IsNullOrWhiteSpace(slug))
                result.Add(slug);
        }

        foreach (var slug in slugs ?? [])
            if (!string.IsNullOrWhiteSpace(slug))
                result.Add(slug.Trim());

        return [.. result];
    }

    private static readonly string[] RoleKeywords =
    [
        "software engineer", "product manager", "data engineer", "devops", "backend engineer",
        "frontend engineer", "machine learning", "fullstack", "platform engineer", "site reliability",
        "data scientist", "security engineer", "mobile engineer", "cloud engineer", "engineering manager",
        "software developer", "qa engineer", "infrastructure engineer", "artificial intelligence",
        "cloud architect", "technical program manager", "solutions architect", "ai engineer"
    ];

    private static readonly string[] NicheRoleKeywords =
    [
        "staff engineer", "principal engineer", "founding engineer", "distinguished engineer",
        "embedded systems", "firmware engineer", "hardware engineer", "fpga engineer",
        "technical writer", "developer advocate", "solutions engineer", "integration engineer",
        "quantitative researcher", "quant developer", "trading systems", "algorithmic trading",
        "medical devices", "clinical data", "bioinformatics", "computational biology",
        "graphics engineer", "rendering engineer", "game engine", "simulation engineer"
    ];

    private static readonly string[] SeniorityKeywords =
    [
        "staff engineer", "principal engineer", "vp engineering", "director of engineering",
        "ux designer", "product designer", "data analyst", "business analyst",
        "sales engineer", "customer success", "growth marketing", "technical recruiter",
        "chief of staff", "head of product", "general counsel"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> KeywordSets = new Dictionary<string, string[]>
    {
        ["roles"]     = RoleKeywords,
        ["niche"]     = NicheRoleKeywords,
        ["seniority"] = SeniorityKeywords,
    };

    private static readonly string[] WorkdayInstances =
    [
        "wd1.myworkdayjobs.com",
        "wd3.myworkdayjobs.com",
        "wd5.myworkdayjobs.com",
        "wd12.myworkdayjobs.com",
    ];

    [HttpPost("brave-search")]
    public async Task<IActionResult> DiscoverViaBraveSearch(
        [FromQuery] string? source,
        [FromQuery] bool dryRun = false,
        [FromQuery] string keywords = "roles",
        [FromQuery] int maxOffset = 1,
        CancellationToken ct = default)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse"]      = "boards.greenhouse.io",
            ["lever"]           = "jobs.lever.co",
            ["ashby"]           = "jobs.ashbyhq.com",
            ["smartrecruiters"] = "jobs.smartrecruiters.com",
            ["rippling"]        = "ats.rippling.com",
            ["recruitee"]       = "recruitee.com",
            ["workday"]         = "myworkdayjobs.com",
        };

        if (source != null && !targets.ContainsKey(source))
            return BadRequest(new { error = $"Unknown source '{source}'. Valid: {string.Join(", ", targets.Keys)}" });

        if (!KeywordSets.TryGetValue(keywords, out var activeKeywords))
            return BadRequest(new { error = $"Unknown keywords '{keywords}'. Valid: {string.Join(", ", KeywordSets.Keys)}" });

        maxOffset = Math.Clamp(maxOffset, 0, 9);

        using var scope = scopeFactory.CreateScope();
        var config      = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var discovery   = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        var apiKey = config["BraveSearch:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            return StatusCode(500, new { error = "BraveSearch:ApiKey not configured" });

        var client = httpFactory.CreateClient("BraveSearch");

        var activeSources = source != null
            ? targets.Where(kv => kv.Key == source).ToList()
            : targets.ToList();

        var existingGreenhouse      = await db.Companies.Where(c => c.GreenhouseBoardToken != null).Select(c => c.GreenhouseBoardToken!).ToHashSetAsync(ct);
        var existingLever           = await db.Companies.Where(c => c.LeverCompanySlug != null).Select(c => c.LeverCompanySlug!).ToHashSetAsync(ct);
        var existingAshby           = await db.Companies.Where(c => c.AshbyBoardSlug != null).Select(c => c.AshbyBoardSlug!).ToHashSetAsync(ct);
        var existingSmartRecruiters = await db.Companies.Where(c => c.SmartRecruitersSlug != null).Select(c => c.SmartRecruitersSlug!).ToHashSetAsync(ct);
        var existingRippling        = await db.Companies.Where(c => c.RipplingSlug != null).Select(c => c.RipplingSlug!).ToHashSetAsync(ct);
        var existingRecruitee       = await db.Companies.Where(c => c.RecruiteeSlug != null).Select(c => c.RecruiteeSlug!).ToHashSetAsync(ct);
        var existingWorkday         = await db.Companies.Where(c => c.WorkdayHost != null).Select(c => c.WorkdayHost!).ToHashSetAsync(ct);

        var summary = new Dictionary<string, object>();

        foreach (var (sourceName, domain) in activeSources)
        {
            var discovered        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredWorkday = new Dictionary<string, WorkdayEntry>(StringComparer.OrdinalIgnoreCase);

            var searchDomains = sourceName == "workday" ? WorkdayInstances : [domain];

            foreach (var searchDomain in searchDomains)
            foreach (var keyword in activeKeywords)
            {
                // Brave caps offset at 9 (10 pages max); use offset 0 and 1 per keyword for variety
                for (int offset = 0; offset <= maxOffset; offset++)
                {
                    var q = Uri.EscapeDataString($"site:{searchDomain} {keyword}");
                    var url = $"https://api.search.brave.com/res/v1/web/search?q={q}&count=20&offset={offset}&search_lang=en";

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("X-Subscription-Token", apiKey);
                    request.Headers.Add("Accept", "application/json");

                    try
                    {
                        var response = await client.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode)
                        {
                            logger.LogWarning("BraveSearch {Status} for {Source}/{Keyword}", (int)response.StatusCode, sourceName, keyword);
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync(ct);
                        var result = JsonSerializer.Deserialize<BraveSearchResponse>(json);

                        foreach (var hit in result?.Web?.Results ?? [])
                        {
                            if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var uri)) continue;

                            if (sourceName == "recruitee")
                            {
                                var host = uri.Host.ToLowerInvariant();
                                if (host.EndsWith(".recruitee.com"))
                                {
                                    var slug = host[..host.IndexOf(".recruitee.com")];
                                    if (!string.IsNullOrEmpty(slug) && slug != "www")
                                        discovered.Add(slug);
                                }
                            }
                            else if (sourceName == "workday")
                            {
                                var entry = ExtractWorkdayEntryFromUrl(uri);
                                if (entry != null)
                                    discoveredWorkday.TryAdd(entry.Host, entry);
                            }
                            else
                            {
                                var segments = uri.AbsolutePath.Trim('/').Split('/')
                                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                                // Rippling URLs may start with a locale segment (en-us, en-gb, etc.) — skip it
                                var slug = segments.FirstOrDefault(s =>
                                    !System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-z]{2}(-[a-z]{2})?$",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                                if (!string.IsNullOrWhiteSpace(slug))
                                    discovered.Add(slug);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "BraveSearch request failed for {Source}/{Keyword}", sourceName, keyword);
                    }

                    await Task.Delay(300, ct); // stay under rate limit
                }
            }

            if (sourceName == "workday")
            {
                var newEntries = discoveredWorkday.Values
                    .Where(e => !existingWorkday.Contains(e.Host)).ToList();
                logger.LogInformation("BraveSearch [{Source}]: discovered={D} new={N} dryRun={DR}",
                    sourceName, discoveredWorkday.Count, newEntries.Count, dryRun);

                if (newEntries.Count == 0)
                {
                    summary[sourceName] = new { discovered = discoveredWorkday.Count, new_ = 0 };
                    continue;
                }

                var discoverResult = await discovery.DiscoverFromSlugsAsync(
                    [], [], [], [], newEntries, [], dryRun, ct);

                summary[sourceName] = new
                {
                    discovered = discoveredWorkday.Count,
                    new_       = newEntries.Count,
                    validated  = discoverResult.ValidatedPerSource.GetValueOrDefault("Workday"),
                    added      = dryRun ? (object)"(dry run)" : discoverResult.Added,
                    skipped    = discoverResult.Skipped,
                    failed     = discoverResult.Failed,
                };
                continue;
            }

            var existingForSource = sourceName switch
            {
                "greenhouse"      => existingGreenhouse,
                "lever"           => existingLever,
                "ashby"           => existingAshby,
                "smartrecruiters" => existingSmartRecruiters,
                "rippling"        => existingRippling,
                "recruitee"       => existingRecruitee,
                _                 => []
            };

            var newSlugs = discovered.Where(s => !existingForSource.Contains(s)).ToList();
            logger.LogInformation("BraveSearch [{Source}]: discovered={D} new={N} dryRun={DR}",
                sourceName, discovered.Count, newSlugs.Count, dryRun);

            if (newSlugs.Count == 0)
            {
                summary[sourceName] = new { discovered = discovered.Count, new_ = 0 };
                continue;
            }

            var discoverResult2 = await discovery.DiscoverFromSlugsAsync(
                sourceName == "greenhouse"      ? newSlugs : [],
                sourceName == "lever"           ? newSlugs : [],
                sourceName == "ashby"           ? newSlugs : [],
                sourceName == "smartrecruiters" ? newSlugs : [],
                [],
                sourceName == "recruitee"       ? newSlugs : [],
                dryRun, ct,
                sourceName == "rippling"        ? newSlugs : null);

            summary[sourceName] = new
            {
                discovered = discovered.Count,
                new_       = newSlugs.Count,
                validated  = discoverResult2.ValidatedPerSource.GetValueOrDefault(ToDisplayName(sourceName)),
                added      = dryRun ? (object)"(dry run)" : discoverResult2.Added,
                skipped    = discoverResult2.Skipped,
                failed     = discoverResult2.Failed,
            };
        }

        return Ok(new { dryRun, summary });
    }

    private static string ToDisplayName(string source) => source switch
    {
        "greenhouse"      => "Greenhouse",
        "lever"           => "Lever",
        "ashby"           => "Ashby",
        "smartrecruiters" => "SmartRecruiters",
        "rippling"        => "Rippling",
        "recruitee"       => "Recruitee",
        "workday"         => "Workday",
        _                 => source
    };

    private record BraveSearchResponse(
        [property: JsonPropertyName("web")] BraveWebResults? Web);

    private record BraveWebResults(
        [property: JsonPropertyName("results")] List<BraveSearchResult>? Results);

    private record BraveSearchResult(
        [property: JsonPropertyName("url")]         string Url,
        [property: JsonPropertyName("title")]       string? Title,
        [property: JsonPropertyName("description")] string? Description);

    [HttpPost("yc-discover")]
    public IActionResult DiscoverFromYC(
        [FromQuery] bool activeOnly = true,
        [FromQuery] bool dryRun = false)
    {
        logger.LogInformation("YC discovery triggered (activeOnly={A} dryRun={D})", activeOnly, dryRun);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var discovery   = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();
            var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var client      = httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
            client.Timeout = TimeSpan.FromSeconds(15);

            // Load normalized company names to skip companies already in the DB
            var knownNames = await db.Companies
                .Where(c => c.NormalizedName != null)
                .Select(c => c.NormalizedName!)
                .ToHashSetAsync();

            var slugsToTry = new List<string>();
            int page = 1;

            while (true)
            {
                try
                {
                    var url = $"https://api.ycombinator.com/v0.1/companies?page={page}&per_page=100";
                    var json = await client.GetStringAsync(url);
                    var response = JsonSerializer.Deserialize<YcResponse>(json);

                    if (response?.Companies == null || response.Companies.Count == 0)
                    {
                        page++;
                        continue;
                    }

                    foreach (var company in response.Companies)
                    {
                        if (string.IsNullOrWhiteSpace(company.Slug)) continue;
                        if (activeOnly && !string.Equals(company.Status, "Active", StringComparison.OrdinalIgnoreCase)) continue;
                        var normalized = company.Slug.ToLowerInvariant().Replace("-", "").Replace("_", "");
                        if (knownNames.Contains(normalized)) continue;
                        slugsToTry.Add(company.Slug);
                    }

                    logger.LogInformation("YC discovery: fetched page {Page}/{Total} ({Count} candidates so far)",
                        page, response.TotalPages, slugsToTry.Count);

                    if (page >= response.TotalPages) break;
                    page++;
                    await Task.Delay(200);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "YC discovery: failed fetching page {Page}", page);
                    break;
                }
            }

            logger.LogInformation("YC discovery: {Count} slugs to validate against ATS APIs", slugsToTry.Count);

            if (slugsToTry.Count == 0)
            {
                logger.LogInformation("YC discovery: nothing new to discover");
                return;
            }

            // Try each slug against all 4 ATS sources simultaneously
           var result = await discovery.DiscoverFromSlugsAsync(
                slugsToTry, slugsToTry, slugsToTry, slugsToTry, [], [], dryRun, CancellationToken.None);

            foreach (var kvp in result.ValidatedPerSource)
                logger.LogInformation("YC discovery [{Source}]: validated={V} failed={F}",
                    kvp.Key, kvp.Value, result.FailedPerSource.GetValueOrDefault(kvp.Key));
            
            logger.LogInformation(
                "YC discovery complete (dryRun={D}): added={A} skipped={S} failed={F}",
                dryRun, result.Added, result.Skipped, result.Failed);
        });

        return Accepted(new { message = "YC discovery started", activeOnly, dryRun });
    }

    private record YcResponse(
        [property: JsonPropertyName("companies")]   List<YcCompany> Companies,
        [property: JsonPropertyName("page")]        int Page,
        [property: JsonPropertyName("totalPages")]  int TotalPages);

    private record YcCompany(
        [property: JsonPropertyName("slug")]        string? Slug,
        [property: JsonPropertyName("status")]      string? Status,
        [property: JsonPropertyName("teamSize")]    int? TeamSize);

    [HttpPost("wikidata-enrich")]
    public IActionResult EnrichFromWikidata([FromQuery] int batchSize = 100)
    {
        logger.LogInformation("Wikidata enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IWikidataEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Wikidata enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Wikidata enrichment failed");
            }
        });

        return Accepted(new { message = $"Wikidata enrichment started for batch of {batchSize}" });
    }

    [HttpPost("web-enrich")]
    public IActionResult EnrichFromWeb([FromQuery] int batchSize = 20)
    {
        logger.LogInformation("Web enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IWebEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Web enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Web enrichment failed");
            }
        });

        return Accepted(new { message = $"Web enrichment started for batch of {batchSize}" });
    }

    [HttpPost("description-enrich")]
    public IActionResult EnrichFromDescriptions([FromQuery] int batchSize = 50)
    {
        logger.LogInformation("Description enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<IDescriptionEnrichmentService>();

            try
            {
                var result = await enricher.EnrichCompaniesAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Description enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Description enrichment failed");
            }
        });

        return Accepted(new { message = $"Description enrichment started for batch of {batchSize}" });
    }
}
