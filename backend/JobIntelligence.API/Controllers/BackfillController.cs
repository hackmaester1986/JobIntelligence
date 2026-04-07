using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using JobIntelligence.API.Filters;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Parsing;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static JobIntelligence.Infrastructure.Parsing.DescriptionHashHelper;
using static JobIntelligence.Infrastructure.Parsing.SalaryParser;
using static JobIntelligence.Infrastructure.Parsing.SkillMatcher;
using Role = Anthropic.Models.Messages.Role;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("internal/backfill")]
[AdminKey]
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
        "applied scientist", "research engineer", "ai engineer", "data analyst","Java Developer",
        "devops","ml ops","fullstack","python","django","lead developer","android engineer",
        "sw developer","embedded programmer","embedded engineer","sw developer","systems administrator",
        "sys admin","data engineer","data engineering","backend developer","engineering team lead",
        "azure","web developer","robotics","c++","c#","java","frontend","software quality",
        "analytics","analyst","email coder","machine learning","product manager","it tech",
        "it technician","information technology","project engineer","engineering manager","front-end",
        "systems engineer","microsoft","security engineer","software development","product engineer",
        "technical lead","database","integration engineer","ai research","system engineer",
        "engineering team lead","software","data solutions","solutions engineer","back-end",
        "automation engineer","engineering manager","android developer","software qa","qa engineer",
        "data intelligence","devsecops","software test","ci/cd","ms dynamics","senior developer",
        "infrastructure engineer","ux design","oracle fusion","data analytics","data analysis",
        "application engineer","aws customer","ai/ml","system administrator","technical support",
        "azure eng","survey technician","typescript","power bi developer","ux developer","fraud specialist",
        "director technology","director engineering","build engineer","ui engineer","web ui","owt engineer",
        "programmer","genai","ai data","data associate","computer","data integration","technology product",
        ".net","sql","snowflake","network engineer","data management","data entry","head of engineering",
        "- it","product development","automated test","system architect","test engineer","core engineering",
        "cloud operations","team agilist","aws and infra","data process","it security","security analyst",
        "saas","application development","salesforce","technical manager","backup architect","rpa developer",
        "desktop support","director, compliance","sr engineer","jr engineer","quality assurance specialist",
        "analytics engineer","qa technician","manager, engineering","engineer internship","ai intern","coding integrity",
        "end user engineer","data governance","design engineer","it support","data quality","ui developer","production support",
        "data operations","ai research","technology manager","qa test engineer","cyber security","data analysis",
        "cybersecurity","embedded software","bi engineer","security officer","agile","digital technology",
        "product design","network operations","junior infra","ai governance","javascript","application solution",
        "data solutions","spring boot","springboot","ai solutions","cyberformation","terraform","principal engineer",
        "android","ux/ui","ui/ux","associate engineer","solution engineer","qa and test","dev ops","solutions architect",
        "data conversion","aws infra","integrations engineer","space science","site reliability","cyber","ai enablement",
        "principal ai","oracle","cloud apps","rest api","restapi","mulesoft","api integration","angular","llm",
        "generative ai","bigquery","dotnet","azure cloud","aws cloud","web internship","application support engineer",
        "digital transformation","it program","sram engineer","circuit design","assoc developer","mainframe",
        "games support","coder","games tech","fraud detection","test security","engineering project director",
        "senior director, engineering","powershell","linux","principal architect","agentic","databricks",
        "developer experience","fraud operations","fraud strategist","technical operations","lead, ai","risk ux",
        "it helpdesk","web content","appian","blockchain security","ai data","it support","ai program manager",
        "technology lifecycle","Développeur","lead level scripter","tech support","ios developer","mobile development",
        "mobile developer","ai product","forward deployed engineer","tech lead","data center engineer","ai platform",
        "ai agents","vp of engineering","applied ai","information systems","manager of it","it and security",
        "it qa analyst","paas engineer","it infrastructure","ai foundation",

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

            int count = await ClassifyFromJobTitles(db);
            logger.LogInformation("Tech classification complete: {Count} companies classified from job titles", count);
        });

        return Accepted(new { message = "Tech classification started" });
    }

    private static async Task<int> ClassifyFromJobTitles(ApplicationDbContext db)
    {
        var companyTitles = await db.JobPostings
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
                    posting.IsUsPosting = UsLocationClassifier.Classify(posting.LocationRaw);
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

                    // Skills — only run keyword matching on tech-adjacent job titles
                    // to avoid false positives on nursing, finance, etc. job descriptions
                    var isTechTitle = TechTitleKeywords.Any(k => posting.Title.Contains(k, StringComparison.OrdinalIgnoreCase));
                    var matches = isTechTitle ? Match(posting.Description, skillEntries) : [];
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

    [HttpPost("smartrecruiters-descriptions")]
    public IActionResult BackfillSmartRecruitersDescriptions([FromQuery] int batchSize = 200, [FromQuery] int concurrency = 10)
    {
        logger.LogInformation("SmartRecruiters description backfill triggered for batch of {BatchSize} (concurrency={C})", batchSize, concurrency);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var client = httpClientFactory.CreateClient("SmartRecruiters");

            var source = await db.JobSources.FirstAsync(s => s.Name == "smartrecruiters");

            var postings = await db.JobPostings
                .Include(p => p.Company)
                .Where(p => p.SourceId == source.Id && p.IsActive && p.Description == null
                            && p.Company.SmartRecruitersSlug != null)
                .OrderByDescending(p => p.FirstSeenAt)
                .Take(batchSize)
                .ToListAsync();

            logger.LogInformation("SmartRecruiters description backfill: {Count} postings to process", postings.Count);

            // Fetch all details in parallel, then apply updates sequentially
            var semaphore = new System.Threading.SemaphoreSlim(concurrency);
            var fetchTasks = postings.Select(async posting =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var refUrl = $"v1/companies/{posting.Company.SmartRecruitersSlug}/postings/{posting.ExternalId}";
                    var detail = await client.GetFromJsonAsync<SrDetailDto>(refUrl);
                    return new SrFetchResult(posting, detail, null);
                }
                catch (Exception ex)
                {
                    return new SrFetchResult(posting, null, ex);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(fetchTasks);

            int updated = 0, failed = 0;
            foreach (var (posting, detail, error) in results)
            {
                if (error != null)
                {
                    logger.LogWarning(error, "SmartRecruiters: failed to fetch detail for {JobId}", posting.ExternalId);
                    failed++;
                    continue;
                }

                var descriptionHtml = BuildSrDescriptionHtml(detail);
                if (descriptionHtml != null)
                {
                    posting.DescriptionHtml = descriptionHtml;
                    posting.Description = StripSrHtml(descriptionHtml);
                    posting.DescriptionHash = Compute(posting.Description);

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

                    posting.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
                else
                {
                    logger.LogDebug("SmartRecruiters: no description content for {JobId}", posting.ExternalId);
                    failed++;
                }
            }

            await db.SaveChangesAsync();
            logger.LogInformation(
                "SmartRecruiters description backfill complete: updated={U} failed={F}", updated, failed);
        });

        return Accepted(new { message = $"SmartRecruiters description backfill started for batch of {batchSize}" });
    }

    [HttpPost("smartrecruiters-apply-urls")]
    public IActionResult BackfillSmartRecruitersApplyUrls()
    {
        logger.LogInformation("SmartRecruiters apply URL backfill triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var source = await db.JobSources.FirstAsync(s => s.Name == "smartrecruiters");

            // Load slug map once — avoids a JOIN on every batch query
            var slugMap = await db.Companies
                .Where(c => c.SmartRecruitersSlug != null)
                .Select(c => new { c.Id, Slug = c.SmartRecruitersSlug! })
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Slug);

            var eligibleCompanyIds = slugMap.Keys.ToList();

            int updated = 0;
            long lastId = 0;

            while (true)
            {
                var batch = await db.JobPostings
                    .Where(p => p.Id > lastId
                                && p.SourceId == source.Id
                                && eligibleCompanyIds.Contains(p.CompanyId))
                    .OrderBy(p => p.Id)
                    .Take(200)
                    .Select(p => new { p.Id, p.ExternalId, p.Title, p.ApplyUrl, p.CompanyId })
                    .AsNoTracking()
                    .ToListAsync();

                if (batch.Count == 0) break;

                foreach (var item in batch)
                {
                    var slug = slugMap[item.CompanyId];
                    var correctUrl = BuildSrApplyUrl(slug, item.ExternalId, item.Title);
                    if (item.ApplyUrl != correctUrl)
                    {
                        await db.JobPostings
                            .Where(p => p.Id == item.Id)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(p => p.ApplyUrl, correctUrl)
                                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
                        updated++;
                    }
                }

                lastId = batch[^1].Id;
                logger.LogInformation("SmartRecruiters apply URL backfill: {Count} updated so far", updated);
            }

            logger.LogInformation("SmartRecruiters apply URL backfill complete: {Total} updated", updated);
        });

        return Accepted(new { message = "SmartRecruiters apply URL backfill started" });
    }

    private static string BuildSrApplyUrl(string companySlug, string jobId, string jobTitle)
    {
        var titleSlug = string.IsNullOrEmpty(jobTitle)
            ? jobId
            : System.Text.RegularExpressions.Regex.Replace(jobTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"https://jobs.smartrecruiters.com/{companySlug}/{jobId}-{titleSlug}";
    }

    private static string? BuildSrDescriptionHtml(SrDetailDto? detail)
    {
        var sections = detail?.JobAd?.Sections;
        if (sections == null) return null;

        var parts = new List<string>();
        foreach (var text in new[]
        {
            sections.CompanyDescription?.Text,
            sections.JobDescription?.Text,
            sections.Qualifications?.Text,
            sections.AdditionalInformation?.Text
        })
        {
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static string? StripSrHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&nbsp;", " ").Replace("&#xa0;", " ").Trim();
    }

    private record SrFetchResult(JobPosting Posting, SrDetailDto? Detail, Exception? Error)
    {
        public void Deconstruct(out JobPosting posting, out SrDetailDto? detail, out Exception? error)
        {
            posting = Posting;
            detail = Detail;
            error = Error;
        }
    }

    private record SrDetailDto(
        [property: JsonPropertyName("postingUrl")] string? PostingUrl,
        [property: JsonPropertyName("applyUrl")] string? ApplyUrl,
        [property: JsonPropertyName("jobAd")] SrJobAdDto? JobAd);

    private record SrJobAdDto(
        [property: JsonPropertyName("sections")] SrSectionsDto? Sections);

    private record SrSectionsDto(
        [property: JsonPropertyName("companyDescription")] SrSectionDto? CompanyDescription,
        [property: JsonPropertyName("jobDescription")] SrSectionDto? JobDescription,
        [property: JsonPropertyName("qualifications")] SrSectionDto? Qualifications,
        [property: JsonPropertyName("additionalInformation")] SrSectionDto? AdditionalInformation);

    private record SrSectionDto(
        [property: JsonPropertyName("text")] string? Text);

    [HttpPost("extract-skills")]
    public IActionResult ExtractSkills([FromQuery] int batchSize = 100, [FromQuery] int chunkSize = 20)
    {
        logger.LogInformation("Skill extraction triggered: batchSize={B} chunkSize={C}", batchSize, chunkSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var anthropic = scope.ServiceProvider.GetRequiredService<AnthropicClient>();

            // Load existing canonical names AND aliases (lowercased) for dedup
            var taxonomy = await db.SkillTaxonomies
                .Select(s => new { s.CanonicalName, s.Aliases })
                .ToListAsync();

            var existingSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in taxonomy)
            {
                existingSkills.Add(s.CanonicalName);
                foreach (var alias in s.Aliases.RootElement.EnumerateArray())
                {
                    var a = alias.GetString();
                    if (!string.IsNullOrWhiteSpace(a))
                        existingSkills.Add(a);
                }
            }

            // Fetch a larger window from DB, filter to tech titles in memory
            var candidates = await db.JobPostings
                .Where(p => p.IsActive && p.Description != null)
                .OrderBy(_ => EF.Functions.Random())
                .Take(batchSize * 5)
                .Select(p => new { p.Title, p.Description })
                .ToListAsync();

            var techPostings = candidates
                .Where(p => TechTitleKeywords.Any(k => p.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Take(batchSize)
                .ToList();

            logger.LogInformation("Skill extraction: {Count} tech postings to process", techPostings.Count);

            // name → category, accumulated across all chunks
            var extractedSkills = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in techPostings.Chunk(chunkSize))
            {
                var combinedText = string.Join("\n\n---\n\n", chunk.Select((p, i) =>
                    $"Job {i + 1} - {p.Title}:\n{(p.Description!.Length > 2000 ? p.Description[..2000] : p.Description)}"));

                var prompt = $$"""
                    From these {{chunk.Length}} job postings, extract only concrete hard technical skills — things a developer would list on a resume as a specific technology they know.

                    INCLUDE: programming languages, frameworks, libraries, databases, cloud services, DevOps tools, software platforms, protocols, and technical certifications.
                    EXCLUDE: soft skills, general competencies, job functions, business processes, methodologies (Agile, Scrum, etc.), and vague phrases like "scalability", "problem solving", or "communication".

                    Each skill must be a specific named technology or tool (e.g. "React", "PostgreSQL", "Kubernetes", "AWS Lambda", "Terraform", "Python").

                    Return ONLY a JSON array. Each element must have "name" (the skill) and "category" (one of: Language, Framework, Database, Cloud, DevOps, Tool, Platform, Security, Data).

                    Example: [{"name":"React","category":"Framework"},{"name":"PostgreSQL","category":"Database"},{"name":"Terraform","category":"DevOps"}]

                    Job postings:
                    {{combinedText}}
                    """;

                try
                {
                    var response = await anthropic.Messages.Create(new MessageCreateParams
                    {
                        Model = Model.ClaudeHaiku4_5_20251001,
                        MaxTokens = 4096,
                        System = "You are a technical skill extraction assistant. Respond ONLY with a valid JSON array. No explanation, no markdown, no code fences — just the JSON array.",
                        Messages = [new MessageParam { Role = Role.User, Content = prompt }]
                    });

                    string? rawText = null;
                    foreach (var block in response.Content)
                        if (block.TryPickText(out TextBlock? tb) && tb != null)
                            rawText = tb.Text;

                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var json = rawText.Trim();
                        var arrStart = json.IndexOf('[');
                        var arrEnd   = json.LastIndexOf(']');
                        if (arrStart < 0)
                        {
                            logger.LogWarning("Skill extraction: no JSON array in response: {Raw}", json[..Math.Min(json.Length, 200)]);
                            continue;
                        }
                        // If no closing bracket the response was truncated — salvage complete objects
                        json = arrEnd > arrStart
                            ? json[arrStart..(arrEnd + 1)]
                            : json[arrStart..] + "]";

                        var skills = JsonSerializer.Deserialize<List<SkillExtractionItem>>(json);
                        if (skills != null)
                        {
                            foreach (var skill in skills)
                            {
                                if (!string.IsNullOrWhiteSpace(skill.Name) && !extractedSkills.ContainsKey(skill.Name))
                                    extractedSkills[skill.Name] = skill.Category;
                            }
                        }
                    }

                    await Task.Delay(500);
                }
                catch (AnthropicRateLimitException ex)
                {
                    logger.LogWarning(ex, "Skill extraction: rate limited — stopping early");
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skill extraction: failed to process chunk, continuing");
                }
            }

            int added = 0;
            foreach (var (name, category) in extractedSkills)
            {
                if (!existingSkills.Contains(name))
                {
                    db.SkillTaxonomies.Add(new SkillTaxonomy
                    {
                        CanonicalName = name,
                        Category = category,
                        Aliases = JsonDocument.Parse("[]"),
                        CreatedAt = DateTime.UtcNow
                    });
                    existingSkills.Add(name);
                    added++;
                }
            }

            await db.SaveChangesAsync();
            logger.LogInformation(
                "Skill extraction complete: {Extracted} skills extracted across all chunks, {Added} new skills added to taxonomy",
                extractedSkills.Count, added);
        });

        return Accepted(new { message = $"Skill extraction started for batch of {batchSize}" });
    }

    private record SkillExtractionItem(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("category")] string? Category);

    [HttpPost("classify-locations")]
    public IActionResult ClassifyLocations()
    {
        logger.LogInformation("Location classification backfill triggered");

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            int updated = 0;
            long lastId = 0;

            while (true)
            {
                var batch = await db.JobPostings
                    .Where(p => p.Id > lastId && p.IsUsPosting == null)
                    .OrderBy(p => p.Id)
                    .Take(1000)
                    .ToListAsync();

                if (batch.Count == 0) break;

                foreach (var posting in batch)
                {
                    posting.IsUsPosting = UsLocationClassifier.Classify(posting.LocationRaw);
                    updated++;
                }

                await db.SaveChangesAsync();
                lastId = batch[^1].Id;
                logger.LogInformation("Location classification: {Count} rows processed", updated);
            }

            logger.LogInformation("Location classification complete: {Total} rows updated", updated);
        });

        return Accepted(new { message = "Location classification backfill started" });
    }

    [HttpPost("deactivate-excluded-companies")]
    public async Task<IActionResult> DeactivateExcludedCompanyJobs(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await db.JobPostings
            .Where(p => p.IsActive && p.Company.IsTechHiring == false)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsActive, false)
                .SetProperty(p => p.RemovedAt, DateTime.UtcNow)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);

        logger.LogInformation("Deactivated {Count} job postings for excluded companies", updated);
        return Ok(new { deactivated = updated });
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

    [HttpPost("enrich-sizes")]
    public IActionResult EnrichSizes([FromQuery] int batchSize = 50)
    {
        logger.LogInformation("Size enrichment triggered for batch of {BatchSize}", batchSize);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var enricher = scope.ServiceProvider.GetRequiredService<JobIntelligence.Core.Interfaces.ISizeEnrichmentService>();
            try
            {
                var result = await enricher.EnrichAsync(batchSize, CancellationToken.None);
                logger.LogInformation(
                    "Size enrichment complete: processed={P} enriched={E} notFound={N} failed={F}",
                    result.Processed, result.Enriched, result.NotFound, result.Failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Size enrichment failed");
            }
        });

        return Accepted(new { message = $"Size enrichment started for batch of {batchSize}" });
    }

    [HttpPost("import-workday-urls")]
    public async Task<IActionResult> ImportWorkdayUrls(
        [FromBody] List<string> urls,
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        if (urls is not { Count: > 0 })
            return BadRequest(new { error = "No URLs provided" });

        // Parse each URL into a WorkdayEntry
        var entries = new List<JobIntelligence.Core.Interfaces.WorkdayEntry>();
        var unparseable = new List<string>();

        foreach (var raw in urls)
        {
            var url = raw.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !uri.Host.EndsWith(".myworkdayjobs.com", StringComparison.OrdinalIgnoreCase))
            {
                unparseable.Add(url);
                continue;
            }

            // Take the first non-empty path segment as the career site
            var careerSite = uri.AbsolutePath
                .Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(careerSite))
            {
                unparseable.Add(url);
                continue;
            }

            entries.Add(new JobIntelligence.Core.Interfaces.WorkdayEntry(uri.Host.ToLowerInvariant(), careerSite));
        }

        logger.LogInformation(
            "import-workday-urls: parsed {Valid} valid entries, {Bad} unparseable (dryRun={DryRun})",
            entries.Count, unparseable.Count, dryRun);

        using var scope = scopeFactory.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<ICompanyDiscoveryService>();

        var result = await discovery.DiscoverFromSlugsAsync(
            [], [], [], [], entries, dryRun, ct);

        return Ok(new
        {
            parsed = entries.Count,
            unparseable,
            validated = result.ValidatedPerSource.GetValueOrDefault("Workday"),
            skipped = result.Skipped,
            failed = result.Failed,
            dryRun,
            added = dryRun ? (object)"(dry run — nothing imported)" : result.AddedCompanies
        });
    }
}
