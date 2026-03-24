using Anthropic;
using Anthropic.Models.Messages;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Parsing;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using static JobIntelligence.Infrastructure.Parsing.DescriptionHashHelper;
using static JobIntelligence.Infrastructure.Parsing.SalaryParser;
using static JobIntelligence.Infrastructure.Parsing.SkillMatcher;
using Role = Anthropic.Models.Messages.Role;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/backfill")]
public class BackfillController(IServiceScopeFactory scopeFactory, ILogger<BackfillController> logger) : ControllerBase
{
    private static readonly HashSet<string> TechTitleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "software engineer", "software developer", "data engineer", "data scientist",
        "machine learning", "ml engineer", "devops", "site reliability", "sre",
        "backend engineer", "frontend engineer", "fullstack", "full stack", "full-stack",
        "mobile engineer", "ios engineer", "android engineer", "platform engineer",
        "infrastructure engineer", "cloud engineer", "security engineer", "product engineer",
        "engineering manager", "staff engineer", "principal engineer", "solutions engineer",
        "applied scientist", "research engineer", "ai engineer", "data analyst",
    };

    [HttpPost("test-haiku")]
    public async Task<IActionResult> TestHaiku()
    {
        using var scope = scopeFactory.CreateScope();
        var anthropic = scope.ServiceProvider.GetRequiredService<AnthropicClient>();

        try
        {
            var response = await anthropic.Messages.Create(new MessageCreateParams
            {
                Model = Model.ClaudeHaiku4_5_20251001,
                MaxTokens = 64,
                Messages = [new MessageParam { Role = Role.User, Content = "Reply with just the word: pong" }]
            });

            string text = "";
            foreach (var block in response.Content)
                if (block.TryPickText(out TextBlock? tb) && tb != null) { text = tb.Text; break; }

            return Ok(new { success = true, response = text });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    [HttpPost("classify-tech")]
    public IActionResult ClassifyTech()
    {
        logger.LogInformation("Tech classification triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var anthropic = scope.ServiceProvider.GetRequiredService<AnthropicClient>();

            // Phase 1: auto-classify from existing job title keywords — ground truth
            int phase1Count = await ClassifyFromJobTitles(db);
            logger.LogInformation("Phase 1 complete: {Count} companies classified from job titles", phase1Count);

            // Phase 2: Haiku classifies remaining companies — sets both industry and is_tech_hiring
            int phase2Count = await ClassifyWithHaiku(db, anthropic, logger);
            logger.LogInformation("Phase 2 complete: {Count} companies classified by Haiku", phase2Count);

            logger.LogInformation("Tech classification complete: {Total} total", phase1Count + phase2Count);
        });

        return Accepted(new { message = "Tech classification started" });
    }

    private static async Task<int> ClassifyFromJobTitles(ApplicationDbContext db)
    {
        var companyTitles = await db.JobPostings
            .Where(p => p.IsActive)
            .Select(p => new { p.CompanyId, p.Title })
            .ToListAsync();

        var techCompanyIds = companyTitles
            .Where(p => TechTitleKeywords.Any(k => p.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.CompanyId)
            .ToHashSet();

        if (techCompanyIds.Count == 0) return 0;

        var companies = await db.Companies
            .Where(c => techCompanyIds.Contains(c.Id) && c.IsTechHiring == null)
            .ToListAsync();

        foreach (var company in companies)
            company.IsTechHiring = true;

        await db.SaveChangesAsync();
        return companies.Count;
    }

    private record HaikuResult(
        [property: JsonPropertyName("i")] int I,
        [property: JsonPropertyName("ind")] string? Ind,
        [property: JsonPropertyName("tech")] bool Tech);

    private static async Task<int> ClassifyWithHaiku(ApplicationDbContext db, AnthropicClient anthropic, ILogger logger)
    {
        // Classify companies not yet determined by job title scan
        var unclassified = await db.Companies
            .Where(c => c.IsTechHiring == null)
            .Select(c => new { c.Id, c.CanonicalName })
            .ToListAsync();

        if (unclassified.Count == 0) return 0;

        const int batchSize = 50;
        int totalClassified = 0;

        for (int i = 0; i < unclassified.Count; i += batchSize)
        {
            var batch = unclassified.Skip(i).Take(batchSize).ToList();
            var list = string.Join("\n", batch.Select((c, idx) => $"{idx}: {c.CanonicalName}"));

            var prompt = $"""
                For each company below, return a JSON array where each element has:
                - "i": 0-based index
                - "ind": short industry label (e.g. "Fintech", "Healthcare", "E-commerce", "Developer Tools", "AI/ML", "Cybersecurity", "Cloud", "Media", "Retail", "Education", "Finance", "Insurance", "Manufacturing", "Government", "Nonprofit", "Other")
                - "tech": true if this company hires software engineers, data scientists, AI/ML engineers, or other software/tech roles; false otherwise

                Return ONLY the JSON array. No explanation, no markdown.

                {list}
                """;

            List<HaikuResult> results;
            try
            {
                var response = await anthropic.Messages.Create(new MessageCreateParams
                {
                    Model = Model.ClaudeHaiku4_5_20251001,
                    MaxTokens = 2048,
                    Messages = [new MessageParam { Role = Role.User, Content = prompt }]
                });

                string text = "";
                foreach (var block in response.Content)
                    if (block.TryPickText(out TextBlock? tb) && tb != null) { text = tb.Text; break; }

                var start = text.IndexOf('[');
                var end = text.LastIndexOf(']');
                var json = start >= 0 && end > start ? text[start..(end + 1)] : "[]";
                results = JsonSerializer.Deserialize<List<HaikuResult>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Haiku batch {Batch} failed", i / batchSize + 1);
                results = [];
            }

            if (results.Count > 0)
            {
                var resultMap = results
                    .Where(r => r.I >= 0 && r.I < batch.Count)
                    .ToDictionary(r => batch[r.I].Id, r => r);

                var toUpdate = await db.Companies
                    .Where(c => resultMap.Keys.Contains(c.Id))
                    .ToListAsync();

                foreach (var company in toUpdate)
                {
                    if (!resultMap.TryGetValue(company.Id, out var r)) continue;
                    company.IsTechHiring = r.Tech;
                    if (!string.IsNullOrWhiteSpace(r.Ind))
                        company.Industry = r.Ind;
                }

                await db.SaveChangesAsync();
                totalClassified += toUpdate.Count;
            }

            logger.LogInformation("Haiku classification: batch {Batch}/{Total}, classified {Count} so far",
                i / batchSize + 1, (int)Math.Ceiling(unclassified.Count / (double)batchSize), totalClassified);

            await Task.Delay(200);
        }

        return totalClassified;
    }
    [HttpPost("enrichment")]
    public IActionResult Enrich()
    {
        logger.LogInformation("Enrichment backfill triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Load skill taxonomy once
            var taxonomy = await db.SkillTaxonomies
                .Select(s => new { s.Id, s.CanonicalName, s.Aliases })
                .ToListAsync();

            var skillEntries = taxonomy.Select(s => new SkillEntry(
                s.Id,
                s.CanonicalName,
                s.Aliases.RootElement.EnumerateArray()
                    .Select(a => a.GetString() ?? "")
                    .Where(a => a.Length > 0)
                    .ToArray()
            )).ToList();

            int updated = 0;
            int batchSize = 500;
            long lastId = 0;

            while (true)
            {
                var batch = await db.JobPostings
                    .Where(p => p.Id > lastId)
                    .OrderBy(p => p.Id)
                    .Take(batchSize)
                    .ToListAsync();

                if (batch.Count == 0) break;

                // Load existing skills for this batch to avoid duplicate inserts
                var batchIds = batch.Select(p => p.Id).ToHashSet();
                var existingSkillSet = (await db.JobSkills
                    .Where(js => batchIds.Contains(js.JobPostingId))
                    .Select(js => new { js.JobPostingId, js.SkillId })
                    .ToListAsync())
                    .Select(x => (x.JobPostingId, x.SkillId))
                    .ToHashSet();

                foreach (var posting in batch)
                {
                    // Location + seniority
                    var loc = LocationParser.Parse(posting.LocationRaw);
                    posting.LocationCity = loc.City;
                    posting.LocationState = loc.State;
                    posting.LocationCountry = loc.Country;
                    posting.IsRemote = loc.IsRemote;
                    posting.IsHybrid = loc.IsHybrid;
                    posting.SeniorityLevel = TitleParser.Parse(posting.Title);

                    // Salary — parse from plain description (strip HTML first)
                    if (!posting.SalaryDisclosed)
                    {
                        var salary = Parse(posting.Description);
                        if (salary.Disclosed)
                        {
                            posting.SalaryMin = salary.Min;
                            posting.SalaryMax = salary.Max;
                            posting.SalaryCurrency = salary.Currency;
                            posting.SalaryPeriod = salary.Period;
                            posting.SalaryDisclosed = true;
                        }
                    }

                    // Skills
                    var matches = Match(posting.Description, skillEntries);
                    foreach (var match in matches)
                    {
                        var key = (posting.Id, match.SkillId);
                        if (!existingSkillSet.Contains(key))
                        {
                            db.JobSkills.Add(new JobSkill
                            {
                                JobPostingId = posting.Id,
                                SkillId = match.SkillId,
                                IsRequired = true,
                                ExtractionMethod = "keyword"
                            });
                            existingSkillSet.Add(key);
                        }
                    }

                    updated++;
                }

                await db.SaveChangesAsync();
                lastId = batch[^1].Id;
                logger.LogInformation("Enrichment backfill: processed {Count} rows", updated);
            }

            logger.LogInformation("Enrichment backfill complete: {Total} rows updated", updated);
        });

        return Accepted(new { message = "Enrichment backfill started" });
    }

    [HttpPost("description-hashes")]
    public IActionResult BackfillDescriptionHashes()
    {
        logger.LogInformation("Description hash backfill triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            int updated = 0;
            long lastId = 0;

            while (true)
            {
                var batch = await db.JobPostings
                    .Where(p => p.Id > lastId && p.DescriptionHash == null && p.Description != null)
                    .OrderBy(p => p.Id)
                    .Take(1000)
                    .ToListAsync();

                if (batch.Count == 0) break;

                foreach (var posting in batch)
                {
                    posting.DescriptionHash = Compute(posting.Description);
                    updated++;
                }

                await db.SaveChangesAsync();
                lastId = batch[^1].Id;
                logger.LogInformation("Description hash backfill: {Count} processed", updated);
            }

            logger.LogInformation("Description hash backfill complete: {Total} rows updated", updated);
        });

        return Accepted(new { message = "Description hash backfill started" });
    }

    [HttpPost("company-stats")]
    public IActionResult BackfillCompanyStats()
    {
        logger.LogInformation("Company stats backfill triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var sql = """
                WITH stats AS (
                    SELECT
                        company_id,
                        COUNT(*) FILTER (WHERE is_active)                                          AS active_job_count,
                        COUNT(*) FILTER (WHERE NOT is_active)                                      AS removed_job_count,
                        COUNT(*) FILTER (WHERE is_active AND is_remote)                            AS remote_job_count,
                        COUNT(*)                                                                    AS total_jobs_ever_seen,
                        AVG(EXTRACT(EPOCH FROM (removed_at - first_seen_at)) / 86400.0)
                            FILTER (WHERE removed_at IS NOT NULL)                                   AS avg_job_lifetime_days,
                        AVG(repost_count::float) FILTER (WHERE is_active)                          AS avg_repost_count,
                        AVG(CASE WHEN salary_disclosed THEN 1.0 ELSE 0.0 END) FILTER (WHERE is_active) AS salary_disclosure_rate
                    FROM job_postings
                    GROUP BY company_id
                ),
                dups AS (
                    SELECT company_id, COALESCE(SUM(cnt), 0) AS duplicate_job_count
                    FROM (
                        SELECT company_id, COUNT(*) AS cnt
                        FROM job_postings
                        WHERE description_hash IS NOT NULL
                        GROUP BY company_id, description_hash
                        HAVING COUNT(*) > 1
                    ) d
                    GROUP BY company_id
                )
                UPDATE companies SET
                    active_job_count       = COALESCE(s.active_job_count, 0),
                    removed_job_count      = COALESCE(s.removed_job_count, 0),
                    remote_job_count       = COALESCE(s.remote_job_count, 0),
                    total_jobs_ever_seen   = COALESCE(s.total_jobs_ever_seen, 0),
                    duplicate_job_count    = COALESCE(d.duplicate_job_count, 0),
                    avg_job_lifetime_days  = s.avg_job_lifetime_days,
                    avg_repost_count       = s.avg_repost_count,
                    salary_disclosure_rate = s.salary_disclosure_rate,
                    stats_computed_at      = NOW()
                FROM stats s
                LEFT JOIN dups d ON d.company_id = s.company_id
                WHERE companies.id = s.company_id
                """;

            var updated = await db.Database.ExecuteSqlRawAsync(sql);
            logger.LogInformation("Company stats backfill complete: {Total} companies updated", updated);
        });

        return Accepted(new { message = "Company stats backfill started" });
    }
}
