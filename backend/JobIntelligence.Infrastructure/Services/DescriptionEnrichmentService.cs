using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Messages;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class DescriptionEnrichmentService(
    AnthropicClient anthropic,
    ApplicationDbContext db,
    ILogger<DescriptionEnrichmentService> logger) : IDescriptionEnrichmentService
{
    public async Task<DescriptionEnrichmentResult> EnrichCompaniesAsync(int batchSize = 50, CancellationToken ct = default)
    {
        int processed = 0, enriched = 0, notFound = 0, failed = 0;

        var companies = await db.Companies
            .Where(c => c.DescriptionEnrichedAt == null)
            .Where(c => c.JobPostings.Any(j => j.IsActive))
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        logger.LogInformation("Description enrichment: processing {Count} companies", companies.Count);

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var descriptions = await db.JobPostings
                    .Where(j => j.CompanyId == company.Id && j.IsActive && (j.Description != null || j.DescriptionHtml != null))
                    .OrderByDescending(j => j.FirstSeenAt)
                    .Take(3)
                    .Select(j => j.Description ?? j.DescriptionHtml)
                    .ToListAsync(ct);

                if (descriptions.Count == 0)
                {
                    notFound++;
                    company.DescriptionEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    processed++;
                    continue;
                }

                var cleanedDescriptions = descriptions
                    .Where(d => d != null)
                    .Select(d => TruncateAndClean(d!))
                    .ToList();

                var extraction = await ExtractFromClaudeAsync(company.CanonicalName, cleanedDescriptions, ct);

                if (extraction == null)
                {
                    notFound++;
                }
                else
                {
                    bool anyUpdated = false;

                    if (company.Industry == null && extraction.Industry?.IsConfident() == true)
                    {
                        company.Industry = extraction.Industry.Value;
                        anyUpdated = true;
                    }

                    if (company.EmployeeCountRange == null && extraction.EmployeeCountRange?.IsConfident() == true)
                    {
                        company.EmployeeCountRange = extraction.EmployeeCountRange.Value;
                        anyUpdated = true;
                    }

                    if (company.HeadquartersCity == null && extraction.HeadquartersCity?.IsConfident() == true)
                    {
                        company.HeadquartersCity = extraction.HeadquartersCity.Value;
                        anyUpdated = true;
                    }

                    if (company.HeadquartersCountry == null && extraction.HeadquartersCountry?.IsConfident() == true)
                    {
                        company.HeadquartersCountry = extraction.HeadquartersCountry.Value;
                        anyUpdated = true;
                    }

                    if (company.FoundingYear == null && extraction.FoundingYear?.IsConfident() == true)
                    {
                        company.FoundingYear = extraction.FoundingYear.Value;
                        anyUpdated = true;
                    }

                    if (anyUpdated)
                    {
                        company.UpdatedAt = DateTime.UtcNow;
                        enriched++;
                        logger.LogInformation("Enriched {Name} from descriptions", company.CanonicalName);
                    }
                    else
                    {
                        notFound++;
                        logger.LogDebug("No extractable fields for {Name}", company.CanonicalName);
                    }
                }

                company.DescriptionEnrichedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                processed++;

                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to enrich {Name} from descriptions", company.CanonicalName);
                failed++;
                try
                {
                    company.DescriptionEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx, "SaveChangesAsync failed for {Name}", company.CanonicalName);
                }
            }
        }

        logger.LogInformation(
            "Description enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
            processed, enriched, notFound, failed);

        return new DescriptionEnrichmentResult(processed, enriched, notFound, failed);
    }

    private async Task<CompanyExtraction?> ExtractFromClaudeAsync(string companyName, List<string> descriptions, CancellationToken ct)
    {
        var descriptionBlock = string.Join("\n---\n", descriptions.Select(d => $"---\n{d}"));

        var userMessage = $$"""
            Extract company information for "{{companyName}}" strictly from the job posting descriptions below.
            Do NOT use any prior knowledge about this company — only extract what is explicitly stated or strongly implied in the text.

            Respond ONLY with a JSON object using this exact structure (use null for value and confidence when not found):
            {
            "industry": { "value": "primary industry e.g. Software, Healthcare, Fintech", "confidence": "one of: high, medium, low" },
            "employee_count_range": { "value": "one of: 1-50, 50-200, 200-500, 500-1000, 1000-5000, 5000-10000, 10000+", "confidence": "one of: high, medium, low" },
            "headquarters_city": { "value": "city name", "confidence": "one of: high, medium, low" },
            "headquarters_country": { "value": "country name", "confidence": "one of: high, medium, low" },
            "founding_year": { "value": 2010, "confidence": "one of: high, medium, low" }
            }

            Confidence rules:
            - high: explicitly stated in the posting text
            - medium: reasonably inferred from strong signals in the text
            - low: weakly implied by a single or vague reference
            - null: no basis in the text — do not guess

            Job postings:
            {{descriptionBlock}}
            ---
            """;

        var response = await anthropic.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5_20251001,
            MaxTokens = 500,
            System = "You are a data extraction assistant. Extract information strictly from provided text. Respond ONLY with a valid JSON object. No explanation, no markdown, no code fences — just the JSON.",
            Messages =
            [
                new MessageParam { Role = Role.User, Content = userMessage }
            ]
        }, ct);

        string? rawText = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? tb) && tb != null)
            {
                rawText = tb.Text!;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        // Handle both raw JSON and ```json ... ``` code fences
        var json = rawText.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('\n');
            var end = json.LastIndexOf("```");
            if (start >= 0 && end > start)
                json = json[(start + 1)..end].Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<CompanyExtraction>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Claude JSON for {Name}: {Json}", companyName, json);
            return null;
        }
    }

    private static string TruncateAndClean(string text)
    {
        // Strip HTML tags
        var cleaned = Regex.Replace(text, "<[^>]+>", " ");
        // Collapse whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        // Truncate to 3000 chars
        return cleaned.Length > 3000 ? cleaned[..3000] : cleaned;
    }

    private record CompanyExtraction(
        [property: JsonPropertyName("industry")] ExtractionField<string>? Industry,
        [property: JsonPropertyName("employee_count_range")] ExtractionField<string>? EmployeeCountRange,
        [property: JsonPropertyName("headquarters_city")] ExtractionField<string>? HeadquartersCity,
        [property: JsonPropertyName("headquarters_country")] ExtractionField<string>? HeadquartersCountry,
        [property: JsonPropertyName("founding_year")] ExtractionField<int>? FoundingYear);

    private record ExtractionField<T>(
        [property: JsonPropertyName("value")] T? Value,
        [property: JsonPropertyName("confidence")] string? Confidence)
    {
        // Accepts high or medium; rejects low, null, or missing confidence
        public bool IsConfident() =>
            Confidence is "high" or "medium" && Value is not null &&
            (Value is not string s || !string.IsNullOrWhiteSpace(s));
    }
}
