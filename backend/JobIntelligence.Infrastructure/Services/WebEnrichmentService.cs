using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Services;

public class WebEnrichmentService(
    AnthropicClient anthropic,
    ApplicationDbContext db,
    ILogger<WebEnrichmentService> logger) : IWebEnrichmentService
{
    public async Task<WebEnrichmentResult> EnrichCompaniesAsync(int batchSize = 20, CancellationToken ct = default)
    {
        int processed = 0, enriched = 0, notFound = 0, failed = 0;

        var companies = await db.Companies
            .Where(c => c.WebEnrichedAt == null)
            .Where(c => c.JobPostings.Any(j => j.IsActive))
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        logger.LogInformation("Web enrichment: processing {Count} companies", companies.Count);

        foreach (var company in companies)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var neededFields = BuildNeededFields(company);
                if (neededFields.Count == 0)
                {
                    company.WebEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    processed++;
                    notFound++;
                    logger.LogDebug("Skipping {Name} — all fields already populated", company.CanonicalName);
                    continue;
                }

                var extraction = await SearchWithClaudeAsync(company.CanonicalName, neededFields, ct);

                if (extraction == null)
                {
                    notFound++;
                }
                else
                {
                    bool anyUpdated = false;

                    if (extraction.CanonicalName?.IsConfident() == true &&
                        extraction.CanonicalName.Confidence == "high" &&
                        IsSlugDerivedName(company.CanonicalName))
                    {
                        company.CanonicalName = extraction.CanonicalName.Value!;
                        anyUpdated = true;
                    }

                    if (company.Industry == null && extraction.Industry?.IsConfident() == true)
                    {
                        company.Industry = Trunc100(extraction.Industry.Value);
                        anyUpdated = true;
                    }

                    if (company.EmployeeCountRange == null && extraction.EmployeeCountRange?.IsConfident() == true)
                    {
                        company.EmployeeCountRange = extraction.EmployeeCountRange.Value;
                        anyUpdated = true;
                    }

                    if (company.HeadquartersCity == null && extraction.HeadquartersCity?.IsConfident() == true)
                    {
                        company.HeadquartersCity = Trunc100(extraction.HeadquartersCity.Value);
                        anyUpdated = true;
                    }

                    if (company.HeadquartersCountry == null && extraction.HeadquartersCountry?.IsConfident() == true)
                    {
                        company.HeadquartersCountry = Trunc100(extraction.HeadquartersCountry.Value);
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
                        logger.LogInformation("Web enriched {Name}", company.CanonicalName);
                    }
                    else
                    {
                        notFound++;
                        logger.LogDebug("No extractable fields for {Name}", company.CanonicalName);
                    }
                }

                company.WebEnrichedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                processed++;

                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AnthropicRateLimitException ex)
            {
                // Do NOT stamp WebEnrichedAt — let this company be retried next run
                logger.LogWarning(ex, "Rate limited on {Name}, skipping without marking enriched", company.CanonicalName);
                failed++;
                break; // no point continuing this batch
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to web enrich {Name}", company.CanonicalName);
                failed++;
                try
                {
                    company.WebEnrichedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception saveEx)
                {
                    logger.LogError(saveEx, "SaveChangesAsync failed for {Name}", company.CanonicalName);
                }
            }
        }

        logger.LogInformation(
            "Web enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
            processed, enriched, notFound, failed);

        return new WebEnrichmentResult(processed, enriched, notFound, failed);
    }

    private static readonly Dictionary<string, string> FieldSchemas = new()
    {
        ["canonical_name"]       = "\"canonical_name\": { \"value\": \"official display name of the company\", \"confidence\": \"one of: high, medium, low\" }",
        ["industry"]             = $"\"industry\": {{ \"value\": \"one of: {CompanyIndustries.PromptList}\", \"confidence\": \"one of: high, medium, low\" }}",
        ["employee_count_range"] = "\"employee_count_range\": { \"value\": \"one of: 1-50, 50-200, 200-500, 500-1000, 1000-5000, 5000-10000, 10000+\", \"confidence\": \"one of: high, medium, low\" }",
        ["headquarters_city"]    = "\"headquarters_city\": { \"value\": \"city name\", \"confidence\": \"one of: high, medium, low\" }",
        ["headquarters_country"] = "\"headquarters_country\": { \"value\": \"country name\", \"confidence\": \"one of: high, medium, low\" }",
        ["founding_year"]        = "\"founding_year\": { \"value\": 2010, \"confidence\": \"one of: high, medium, low\" }",
    };

    private static List<string> BuildNeededFields(Core.Entities.Company company)
    {
        var fields = new List<string>();
        if (IsSlugDerivedName(company.CanonicalName))  fields.Add("canonical_name");
        if (company.Industry == null)                  fields.Add("industry");
        if (company.EmployeeCountRange == null)        fields.Add("employee_count_range");
        if (company.HeadquartersCity == null)          fields.Add("headquarters_city");
        if (company.HeadquartersCountry == null)       fields.Add("headquarters_country");
        if (company.FoundingYear == null)              fields.Add("founding_year");
        return fields;
    }

    private async Task<CompanyExtraction?> SearchWithClaudeAsync(string companyName, List<string> neededFields, CancellationToken ct)
    {
        var schema = "{\n" + string.Join(",\n", neededFields.Select(f => FieldSchemas[f])) + "\n}";

        var userMessage = $"""
            Using your training knowledge, provide information about the company "{companyName}".

            Return ONLY a JSON object using this exact structure (use null for value and confidence when not found):
            {schema}

            Confidence rules:
            - high: well-known fact you are confident about
            - medium: likely correct but not certain
            - low: uncertain or possibly conflicting information
            - null: company not recognized or information unknown

            If there are multiple companies with this name, use context clues (it's a tech or professional services company that uses an ATS for hiring) to pick the most likely one.
            """;

        var response = await anthropic.Messages.Create(new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5_20251001,
            MaxTokens = 600,
            System = "You are a company research assistant. Answer from your training knowledge. Respond ONLY with a valid JSON object. No explanation, no markdown, no code fences — just the JSON.",
            Messages = [new MessageParam { Role = Role.User, Content = userMessage }],
        }, ct);

        // Take the last text block — intermediate blocks may contain "Let me search..." preamble
        string? rawText = null;
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? tb) && tb != null)
                rawText = tb.Text;
        }

        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        // Extract JSON object regardless of surrounding prose or code fences
        var json = rawText.Trim();
        var braceStart = json.IndexOf('{');
        var braceEnd = json.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            json = json[braceStart..(braceEnd + 1)];

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

    private static string? Trunc100(string? value) =>
        value is null ? null : value.Length > 100 ? value[..100] : value;

    private static bool IsSlugDerivedName(string name) =>
        !name.Contains(' ') && (name == name.ToLowerInvariant() || name.Contains('-') || name.Contains('_'));

    private record CompanyExtraction(
        [property: JsonPropertyName("canonical_name")] ExtractionField<string>? CanonicalName,
        [property: JsonPropertyName("industry")] ExtractionField<string>? Industry,
        [property: JsonPropertyName("employee_count_range")] ExtractionField<string>? EmployeeCountRange,
        [property: JsonPropertyName("headquarters_city")] ExtractionField<string>? HeadquartersCity,
        [property: JsonPropertyName("headquarters_country")] ExtractionField<string>? HeadquartersCountry,
        [property: JsonPropertyName("founding_year")] ExtractionField<int?>? FoundingYear);

    private record ExtractionField<T>(
        [property: JsonPropertyName("value")] T? Value,
        [property: JsonPropertyName("confidence")] string? Confidence)
    {
        public bool IsConfident() =>
            Confidence is "high" or "medium" && Value is not null &&
            (Value is not string s || !string.IsNullOrWhiteSpace(s));
    }
}
