using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.Infrastructure.Services;

public class ChatService(AnthropicClient anthropic, ApplicationDbContext db) : IChatService
{
    private static readonly IReadOnlyList<ToolUnion> Tools =
    [
        new Tool
        {
            Name = "search_jobs",
            Description = "Search active job postings by title, department, seniority, or remote status. Returns top matches.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["q"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Search query for job title or department" }),
                    ["seniority"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Seniority level filter (e.g. Senior, Junior, Lead)" }),
                    ["is_remote"] = JsonSerializer.SerializeToElement(new { type = "boolean", description = "Filter for remote jobs only" }),
                    ["limit"] = JsonSerializer.SerializeToElement(new { type = "integer", description = "Max results to return (default 10)" }),
                }
            }
        },
        new Tool
        {
            Name = "get_companies",
            Description = "Get companies with hiring stats (active jobs, remote jobs, total jobs ever seen, avg job lifetime, repost rate, salary disclosure rate), optionally filtered by name.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["q"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Search query for company name" }),
                    ["limit"] = JsonSerializer.SerializeToElement(new { type = "integer", description = "Max results to return (default 10)" }),
                }
            }
        },
        new Tool
        {
            Name = "get_stats",
            Description = "Get dashboard stats: total active jobs, company count, remote job count, top companies, seniority breakdown, and top departments.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>()
            }
        },
        new Tool
        {
            Name = "get_job_trends",
            Description = "Get time-series snapshot data showing how active job counts, new postings, and removals have changed over time. Can be scoped to a specific company or returned as an aggregate across all companies. Use this to answer questions about hiring trends, momentum, growth, or slowdowns.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["company"] = JsonSerializer.SerializeToElement(new { type = "string", description = "Company name to scope trends to (optional — omit for market-wide trends)" }),
                    ["range"]   = JsonSerializer.SerializeToElement(new { type = "string", description = "Time range: '1w' (last 7 days), '1m' (last month), '3m' (last 3 months). Default '1m'." }),
                }
            }
        }
    ];

    // Keep the last N user/assistant turns to limit input token cost
    private const int MaxHistoryTurns = 10;

    public async Task<string> ChatAsync(List<(string Role, string Content)> history, bool? isUs = null, CancellationToken ct = default)
    {
        // Trim to the most recent turns; always keep at least the latest user message
        var trimmed = history.Count > MaxHistoryTurns * 2
            ? history[^(MaxHistoryTurns * 2)..]
            : history;

        var messages = trimmed.Select(m => new MessageParam
        {
            Role = m.Role == "user" ? Role.User : Role.Assistant,
            Content = m.Content
        }).ToList();

        const string system = "You are a job market analyst for JobIntelligence. Use tools to answer questions about job trends, top companies, required skills, and salary data. Be concise and data-driven.";

        while (true)
        {
            var response = await anthropic.Messages.Create(new MessageCreateParams
            {
                Model = Model.ClaudeSonnet4_6,
                MaxTokens = 1024,
                System = system,
                Messages = messages,
                Tools = Tools
            }, ct);

            if (response.StopReason == "end_turn")
            {
                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out TextBlock? tb) && tb != null)
                        return tb.Text;
                }
                return "";
            }

            // tool_use: collect tool calls, execute, feed results back
            List<ContentBlockParam> assistantContent = [];
            List<ContentBlockParam> toolResults = [];

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? tb) && tb != null)
                {
                    assistantContent.Add(new TextBlockParam { Text = tb.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? tu) && tu != null)
                {
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID = tu.ID,
                        Name = tu.Name,
                        Input = tu.Input
                    });

                    var result = tu.Name switch
                    {
                        "search_jobs"    => await ExecuteSearchJobs(tu.Input, isUs, ct),
                        "get_companies"  => await ExecuteGetCompanies(tu.Input, isUs, ct),
                        "get_stats"      => await ExecuteGetStats(isUs, ct),
                        "get_job_trends" => await ExecuteGetJobTrends(tu.Input, isUs, ct),
                        _                => """{"error":"unknown tool"}"""
                    };

                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = tu.ID,
                        Content = result
                    });
                }
            }

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }
    }

    private async Task<string> ExecuteSearchJobs(IReadOnlyDictionary<string, JsonElement> input, bool? isUs, CancellationToken ct)
    {
        var q = input.TryGetValue("q", out var qProp) ? qProp.GetString() : null;
        var seniority = input.TryGetValue("seniority", out var sProp) ? sProp.GetString() : null;
        bool? isRemote = input.TryGetValue("is_remote", out var rProp)
            ? rProp.ValueKind == JsonValueKind.True ? true : rProp.ValueKind == JsonValueKind.False ? (bool?)false : null
            : null;
        var limit = input.TryGetValue("limit", out var lProp) ? lProp.GetInt32() : 10;

        var query = db.JobPostings.Where(j => j.IsActive && j.Company.IsTechHiring != false).AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(j => EF.Functions.ILike(j.Title, $"%{q}%")
                || EF.Functions.ILike(j.Department ?? "", $"%{q}%"));

        if (!string.IsNullOrEmpty(seniority))
            query = query.Where(j => EF.Functions.ILike(j.SeniorityLevel ?? "", $"%{seniority}%"));

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

        if (isUs == true)
            query = query.Where(j => j.IsUsPosting == true || j.IsUsPosting == null);

        var jobs = await query
            .OrderByDescending(j => j.FirstSeenAt)
            .Take(limit)
            .Select(j => new
            {
                j.Id,
                j.Title,
                j.Department,
                j.SeniorityLevel,
                j.LocationRaw,
                j.IsRemote,
                j.IsHybrid,
                CompanyName = j.Company.CanonicalName,
                j.FirstSeenAt
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(jobs);
    }

    private async Task<string> ExecuteGetCompanies(IReadOnlyDictionary<string, JsonElement> input, bool? isUs, CancellationToken ct)
    {
        var q = input.TryGetValue("q", out var qProp) ? qProp.GetString() : null;
        var limit = input.TryGetValue("limit", out var lProp) ? lProp.GetInt32() : 10;

        var query = db.Companies.Where(c => c.IsTechHiring != false && c.JobPostings.Any(j => j.IsActive)).AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(c => EF.Functions.ILike(c.CanonicalName, $"%{q}%"));

        var companies = await query
            .Select(c => new
            {
                c.Id,
                c.CanonicalName,
                c.Industry,
                c.HeadquartersCity,
                c.HeadquartersCountry,
                c.ActiveJobCount,
                c.RemoteJobCount,
                c.TotalJobsEverSeen,
                c.DuplicateJobCount,
                c.AvgJobLifetimeDays,
                c.AvgRepostCount,
                c.SalaryDisclosureRate
            })
            .OrderByDescending(x => x.ActiveJobCount)
            .Take(limit)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(companies);
    }

    private async Task<string> ExecuteGetStats(bool? isUs, CancellationToken ct)
    {
        var baseQuery = db.JobPostings.Where(j => j.IsActive && j.Company.IsTechHiring != false);
        if (isUs == true)
            baseQuery = baseQuery.Where(j => j.IsUsPosting == true || j.IsUsPosting == null);

        var totalActiveJobs = await baseQuery.CountAsync(ct);
        var totalCompanies = await db.Companies.CountAsync(c => c.IsTechHiring != false && c.JobPostings.Any(j => j.IsActive), ct);
        var remoteJobs = await baseQuery.CountAsync(j => j.IsRemote, ct);

        var topCompanies = await db.Companies
            .Where(c => c.IsTechHiring != false && c.JobPostings.Any(j => j.IsActive))
            .Select(c => new { Name = c.CanonicalName, JobCount = c.JobPostings.Count(j => j.IsActive) })
            .OrderByDescending(x => x.JobCount).Take(5)
            .ToListAsync(ct);

        var bySeniority = await baseQuery
            .Where(j => j.SeniorityLevel != null)
            .GroupBy(j => j.SeniorityLevel!)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var topDepartments = await baseQuery
            .Where(j => j.Department != null)
            .GroupBy(j => j.Department!)
            .Select(g => new { Department = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { totalActiveJobs, totalCompanies, remoteJobs, topCompanies, bySeniority, topDepartments });
    }

    private async Task<string> ExecuteGetJobTrends(IReadOnlyDictionary<string, JsonElement> input, bool? isUs, CancellationToken ct)
    {
        var company = input.TryGetValue("company", out var cProp) ? cProp.GetString() : null;
        var range    = input.TryGetValue("range",   out var rProp) ? rProp.GetString() : "1m";

        var cutoff = range switch
        {
            "1w" => DateTime.UtcNow.AddDays(-7),
            "3m" => DateTime.UtcNow.AddMonths(-3),
            _    => DateTime.UtcNow.AddMonths(-1),
        };
        var trunc = range == "3m" ? "DATE_TRUNC('week', s.snapshot_at)" : "DATE(s.snapshot_at)";

        var companyFilter = string.IsNullOrEmpty(company) ? "" : "AND LOWER(c.canonical_name) LIKE LOWER(@company)";

        var sql = $"""
            SELECT
                {trunc}            AS date,
                SUM(s.new_count)     AS added,
                SUM(s.removed_count) AS removed,
                (ARRAY_AGG(s.active_job_count ORDER BY s.snapshot_at DESC))[1] AS active_jobs
            FROM company_job_snapshots s
            JOIN companies c ON c.id = s.company_id
            WHERE s.snapshot_at >= @cutoff
              AND s.snapshot_at > (
                  SELECT MIN(snapshot_at) FROM company_job_snapshots WHERE company_id = s.company_id
              )
              AND c.is_tech_hiring IS DISTINCT FROM FALSE
              {companyFilter}
            GROUP BY {trunc}
            ORDER BY 1
            """;

        var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("cutoff", cutoff);
        if (!string.IsNullOrEmpty(company))
            cmd.Parameters.AddWithValue("company", $"%{company}%");

        var rows = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new
            {
                date       = reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                added      = reader.GetInt64(1),
                removed    = reader.GetInt64(2),
                activeJobs = reader.GetInt32(3),
            });
        }

        return JsonSerializer.Serialize(new { range, company = company ?? "all", dataPoints = rows });
    }
}
