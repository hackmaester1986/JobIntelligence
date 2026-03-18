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
            Description = "Get companies with active job counts, optionally filtered by name.",
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
        }
    ];

    public async Task<string> ChatAsync(List<(string Role, string Content)> history, CancellationToken ct)
    {
        var messages = history.Select(m => new MessageParam
        {
            Role = m.Role == "user" ? Role.User : Role.Assistant,
            Content = m.Content
        }).ToList();

        const string system = "You are a job market analyst for JobIntelligence. Use tools to answer questions about job trends, top companies, required skills, and salary data. Be concise and data-driven.";

        while (true)
        {
            var response = await anthropic.Messages.Create(new MessageCreateParams
            {
                Model = Model.ClaudeOpus4_6,
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
                        "search_jobs"   => await ExecuteSearchJobs(tu.Input, ct),
                        "get_companies" => await ExecuteGetCompanies(tu.Input, ct),
                        "get_stats"     => await ExecuteGetStats(ct),
                        _               => """{"error":"unknown tool"}"""
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

    private async Task<string> ExecuteSearchJobs(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var q = input.TryGetValue("q", out var qProp) ? qProp.GetString() : null;
        var seniority = input.TryGetValue("seniority", out var sProp) ? sProp.GetString() : null;
        bool? isRemote = input.TryGetValue("is_remote", out var rProp)
            ? rProp.ValueKind == JsonValueKind.True ? true : rProp.ValueKind == JsonValueKind.False ? (bool?)false : null
            : null;
        var limit = input.TryGetValue("limit", out var lProp) ? lProp.GetInt32() : 10;

        var query = db.JobPostings.Where(j => j.IsActive).AsQueryable();

        if (!string.IsNullOrEmpty(q))
            query = query.Where(j => EF.Functions.ILike(j.Title, $"%{q}%")
                || EF.Functions.ILike(j.Department ?? "", $"%{q}%"));

        if (!string.IsNullOrEmpty(seniority))
            query = query.Where(j => EF.Functions.ILike(j.SeniorityLevel ?? "", $"%{seniority}%"));

        if (isRemote.HasValue)
            query = query.Where(j => j.IsRemote == isRemote.Value);

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

    private async Task<string> ExecuteGetCompanies(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var q = input.TryGetValue("q", out var qProp) ? qProp.GetString() : null;
        var limit = input.TryGetValue("limit", out var lProp) ? lProp.GetInt32() : 10;

        var query = db.Companies.Where(c => c.JobPostings.Any(j => j.IsActive)).AsQueryable();

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
                ActiveJobCount = c.JobPostings.Count(j => j.IsActive)
            })
            .OrderByDescending(x => x.ActiveJobCount)
            .Take(limit)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(companies);
    }

    private async Task<string> ExecuteGetStats(CancellationToken ct)
    {
        var totalActiveJobs = await db.JobPostings.CountAsync(j => j.IsActive, ct);
        var totalCompanies = await db.Companies.CountAsync(c => c.JobPostings.Any(j => j.IsActive), ct);
        var remoteJobs = await db.JobPostings.CountAsync(j => j.IsActive && j.IsRemote, ct);

        var topCompanies = await db.Companies
            .Where(c => c.JobPostings.Any(j => j.IsActive))
            .Select(c => new { Name = c.CanonicalName, JobCount = c.JobPostings.Count(j => j.IsActive) })
            .OrderByDescending(x => x.JobCount).Take(5)
            .ToListAsync(ct);

        var bySeniority = await db.JobPostings
            .Where(j => j.IsActive && j.SeniorityLevel != null)
            .GroupBy(j => j.SeniorityLevel!)
            .Select(g => new { Level = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var topDepartments = await db.JobPostings
            .Where(j => j.IsActive && j.Department != null)
            .GroupBy(j => j.Department!)
            .Select(g => new { Department = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { totalActiveJobs, totalCompanies, remoteJobs, topCompanies, bySeniority, topDepartments });
    }
}
