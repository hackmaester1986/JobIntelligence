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
                    validatedGreenhouse, validatedLever, validatedAshby, null, ct);
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
        List<string> smartRecruitersSlugs, CancellationToken ct = default)
    {
        var existing = await db.Companies.Select(c => c.NormalizedName).ToHashSetAsync(ct);
        var added = new List<string>();
        var failed = new List<string>();
        int skipped = 0;

        var greenhouse = httpClientFactory.CreateClient("Greenhouse");
        var lever = httpClientFactory.CreateClient("Lever");
        var ashby = httpClientFactory.CreateClient("Ashby");
        var smartRecruiters = httpClientFactory.CreateClient("SmartRecruiters");

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
                    await ImportCompanyAsync(name, normalized, null, validated, null, null, null, ct);
                    existing.Add(normalized);
                    added.Add(name);
                    logger.LogInformation("Imported {Company} (Greenhouse)", name);
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
                    await ImportCompanyAsync(name, normalized, null, null, validated, null, null, ct);
                    existing.Add(normalized);
                    added.Add(name);
                    logger.LogInformation("Imported {Company} (Lever)", name);
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
                    await ImportCompanyAsync(name, normalized, null, null, null, validated, null, ct);
                    existing.Add(normalized);
                    added.Add(name);
                    logger.LogInformation("Imported {Company} (Ashby)", name);
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
                    await ImportCompanyAsync(name, normalized, null, null, null, null, validated, ct);
                    existing.Add(normalized);
                    added.Add(name);
                    logger.LogInformation("Imported {Company} (SmartRecruiters)", name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process SmartRecruiters slug {Slug}", slug);
                failed.Add(slug);
            }

            await Task.Delay(200, ct);
        }

        return new DiscoveryResult(added.Count, skipped, failed.Count, added, failed);
    }

    // Shared: insert a validated company row
    private async Task ImportCompanyAsync(
        string canonicalName, string normalizedName, string? domain,
        string? greenhouseToken, string? leverSlug, string? ashbySlug,
        string? smartRecruitersSlug, CancellationToken ct)
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
}
