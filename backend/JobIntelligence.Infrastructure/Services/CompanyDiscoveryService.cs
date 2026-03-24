using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class CompanyDiscoveryService(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    ILogger<CompanyDiscoveryService> logger) : ICompanyDiscoveryService
{
    private record RegistryEntry(
        [property: JsonPropertyName("canonicalName")] string CanonicalName,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("greenhouse")] string? Greenhouse,
        [property: JsonPropertyName("lever")] string? Lever,
        [property: JsonPropertyName("ashby")] string? Ashby);

    public async Task<DiscoveryResult> DiscoverAndImportAsync(CancellationToken ct = default)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("company_registry.json"));
        await using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = await JsonSerializer.DeserializeAsync<List<RegistryEntry>>(stream, cancellationToken: ct) ?? [];

        var existing = await db.Companies.Select(c => c.NormalizedName).ToHashSetAsync(ct);
        var added = new List<string>();
        var failed = new List<string>();
        int skipped = 0;

        var greenhouse = httpClientFactory.CreateClient("Greenhouse");
        var lever = httpClientFactory.CreateClient("Lever");
        var ashby = httpClientFactory.CreateClient("Ashby");

        foreach (var entry in entries)
        {
            var normalized = entry.CanonicalName.ToLowerInvariant().Replace(" ", "");
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                string? validatedGreenhouse = entry.Greenhouse != null
                    ? await ValidateGreenhouseToken(greenhouse, entry.Greenhouse, ct) : null;
                string? validatedLever = entry.Lever != null
                    ? await ValidateLeverSlug(lever, entry.Lever, ct) : null;
                string? validatedAshby = entry.Ashby != null
                    ? await ValidateAshbySlug(ashby, entry.Ashby, ct) : null;

                if (validatedGreenhouse == null && validatedLever == null && validatedAshby == null)
                {
                    logger.LogWarning("No valid board found for {Company}, skipping", entry.CanonicalName);
                    failed.Add(entry.CanonicalName);
                    continue;
                }

                await ImportCompanyAsync(entry.CanonicalName, normalized, entry.Domain,
                    validatedGreenhouse, validatedLever, validatedAshby, null, null, null, ct);
                existing.Add(normalized);
                added.Add(entry.CanonicalName);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process {Company}", entry.CanonicalName);
                failed.Add(entry.CanonicalName);
            }

            await Task.Delay(200, ct);
        }

        return new DiscoveryResult(added.Count, skipped, failed.Count, added, failed);
    }

    public async Task<DiscoveryResult> DiscoverFromSlugsAsync(
        List<string> greenhouseSlugs, List<string> leverSlugs, List<string> ashbySlugs,
        List<string> smartRecruitersSlugs, List<WorkdayEntry> workdayEntries, bool dryRun = false, CancellationToken ct = default)
    {
        var existing = await db.Companies.Select(c => c.NormalizedName).ToHashSetAsync(ct);
        var added = new List<string>();
        var failed = new List<string>();
        int skipped = 0;

        var greenhouse = httpClientFactory.CreateClient("Greenhouse");
        var lever = httpClientFactory.CreateClient("Lever");
        var ashby = httpClientFactory.CreateClient("Ashby");
        var smartRecruiters = httpClientFactory.CreateClient("SmartRecruiters");
        var workday = httpClientFactory.CreateClient("Workday");

        foreach (var slug in greenhouseSlugs)
        {
            var normalized = slug.ToLowerInvariant();
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                var validated = await ValidateGreenhouseToken(greenhouse, slug, ct);
                if (validated == null) { failed.Add(slug); }
                else
                {
                    var name = SlugToName(slug);
                    if (!dryRun)
                    {
                        await ImportCompanyAsync(name, normalized, null, validated, null, null, null, null, null, ct);
                        existing.Add(normalized);
                    }
                    added.Add(name);
                    logger.LogInformation("{Action} {Company} (Greenhouse)", dryRun ? "Validated" : "Imported", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process Greenhouse slug {Slug}", slug);
                failed.Add(slug);
            }

            await Task.Delay(200, ct);
        }

        foreach (var slug in leverSlugs)
        {
            var normalized = slug.ToLowerInvariant();
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                var validated = await ValidateLeverSlug(lever, slug, ct);
                if (validated == null) { failed.Add(slug); }
                else
                {
                    var name = SlugToName(slug);
                    if (!dryRun)
                    {
                        await ImportCompanyAsync(name, normalized, null, null, validated, null, null, null, null, ct);
                        existing.Add(normalized);
                    }
                    added.Add(name);
                    logger.LogInformation("{Action} {Company} (Lever)", dryRun ? "Validated" : "Imported", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process Lever slug {Slug}", slug);
                failed.Add(slug);
            }

            await Task.Delay(200, ct);
        }

        foreach (var slug in ashbySlugs)
        {
            var normalized = slug.ToLowerInvariant();
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                var validated = await ValidateAshbySlug(ashby, slug, ct);
                if (validated == null) { failed.Add(slug); }
                else
                {
                    var name = SlugToName(slug);
                    if (!dryRun)
                    {
                        await ImportCompanyAsync(name, normalized, null, null, null, validated, null, null, null, ct);
                        existing.Add(normalized);
                    }
                    added.Add(name);
                    logger.LogInformation("{Action} {Company} (Ashby)", dryRun ? "Validated" : "Imported", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process Ashby slug {Slug}", slug);
                failed.Add(slug);
            }

            await Task.Delay(200, ct);
        }

        foreach (var slug in smartRecruitersSlugs)
        {
            var normalized = slug.ToLowerInvariant();
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                var validated = await ValidateSmartRecruitersSlug(smartRecruiters, slug, ct);
                if (validated == null) { failed.Add(slug); }
                else
                {
                    var name = SlugToName(slug);
                    if (!dryRun)
                    {
                        await ImportCompanyAsync(name, normalized, null, null, null, null, validated, null, null, ct);
                        existing.Add(normalized);
                    }
                    added.Add(name);
                    logger.LogInformation("{Action} {Company} (SmartRecruiters)", dryRun ? "Validated" : "Imported", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process SmartRecruiters slug {Slug}", slug);
                failed.Add(slug);
            }

            await Task.Delay(200, ct);
        }

        foreach (var entry in workdayEntries)
        {
            var normalized = entry.Host.ToLowerInvariant();
            if (existing.Contains(normalized)) { skipped++; continue; }

            try
            {
                var validated = await ValidateWorkdayTenant(workday, entry.Host, entry.CareerSite, ct);
                if (validated == null) { failed.Add(entry.Host); }
                else
                {
                    var name = SlugToName(entry.Host.Split('.')[0]);
                    if (!dryRun)
                    {
                        await ImportCompanyAsync(name, normalized, null, null, null, null, null, validated, entry.CareerSite, ct);
                        existing.Add(normalized);
                    }
                    added.Add(name);
                    logger.LogInformation("{Action} {Company} (Workday)", dryRun ? "Validated" : "Imported", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process Workday host {Host}", entry.Host);
                failed.Add(entry.Host);
            }

            await Task.Delay(200, ct);
        }

        return new DiscoveryResult(added.Count, skipped, failed.Count, added, failed);
    }

    // Shared: insert a validated company row
    private async Task ImportCompanyAsync(
        string canonicalName, string normalizedName, string? domain,
        string? greenhouseToken, string? leverSlug, string? ashbySlug,
        string? smartRecruitersSlug, string? workdayHost, string? workdayCareerSite, CancellationToken ct)
    {
        db.Companies.Add(new Company
        {
            CanonicalName = canonicalName,
            NormalizedName = normalizedName,
            Domain = domain,
            GreenhouseBoardToken = greenhouseToken,
            LeverCompanySlug = leverSlug,
            AshbyBoardSlug = ashbySlug,
            SmartRecruitersSlug = smartRecruitersSlug,
            WorkdayHost = workdayHost,
            WorkdayCareerSite = workdayCareerSite,
            FirstSeenAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    // "cockroach-labs" → "Cockroach Labs"
    private static string SlugToName(string slug) =>
        string.Join(" ", slug.Split('-').Select(w =>
            w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));

    private static async Task<string?> ValidateGreenhouseToken(
        HttpClient client, string token, CancellationToken ct)
    {
        var response = await client.GetAsync($"v1/boards/{token}/jobs", ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.TryGetProperty("jobs", out var jobs) ? jobs.GetArrayLength() : 0;
        return count > 0 ? token : null;
    }

    private static async Task<string?> ValidateLeverSlug(
        HttpClient client, string slug, CancellationToken ct)
    {
        var response = await client.GetAsync($"v0/postings/{slug}?mode=json", ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        return count > 0 ? slug : null;
    }

    private static async Task<string?> ValidateAshbySlug(
        HttpClient client, string slug, CancellationToken ct)
    {
        var response = await client.GetAsync($"posting-api/job-board/{slug}", ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.TryGetProperty("jobs", out var jobs) ? jobs.GetArrayLength() : 0;
        return count > 0 ? slug : null;
    }

    private static async Task<string?> ValidateSmartRecruitersSlug(
        HttpClient client, string slug, CancellationToken ct)
    {
        var response = await client.GetAsync($"v1/companies/{slug}/postings?limit=1", ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var total = doc.RootElement.TryGetProperty("totalFound", out var t) ? t.GetInt32() : 0;
        return total > 0 ? slug : null;
    }

    private async Task<string?> ValidateWorkdayTenant(
        HttpClient client, string host, string careerSite, CancellationToken ct)
    {
        // Validate by hitting the CXS jobs API with limit=1 — if it returns JSON with a "total"
        // field the tenant is live. The root URL returns 404 so we can't use that.
        var tenant = host.Split('.')[0];
        var url = $"https://{host}/wday/cxs/{tenant}/{careerSite}/jobs";
        logger.LogInformation("Validating Workday tenant: POST {Url}", url);
        try
        {
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { appliedFacets = new { }, limit = 1, offset = 0, searchText = "" })
            };
            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Workday validation failed for {Host}: HTTP {Status}", host, (int)response.StatusCode);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var valid = doc.RootElement.TryGetProperty("total", out _);
            if (!valid) logger.LogWarning("Workday validation failed for {Host}: no 'total' in response", host);
            return valid ? host : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Workday validation exception for {Host}: {Message}", host, ex.Message);
            return null;
        }
    }
}
