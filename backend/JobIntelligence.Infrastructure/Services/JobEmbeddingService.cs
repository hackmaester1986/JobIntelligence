using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgvector;

namespace JobIntelligence.Infrastructure.Services;

public class JobEmbeddingService(
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    IConfiguration configuration,
    ILogger<JobEmbeddingService> logger) : IJobEmbeddingService
{
    private const int OpenAiBatchSize = 1000;
    private const string EmbeddingModel = "text-embedding-3-small";

    public async Task<EmbeddingResult> EmbedJobsAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("OpenAI:ApiKey not configured — skipping embedding");
            return new EmbeddingResult(0, 0, 0, 0);
        }

        // Fetch postings that don't have an embedding yet
        var postings = await db.JobPostings
            .Where(p => p.IsActive && !db.JobEmbeddings.Any(e => e.JobPostingId == p.Id))
            .Include(p => p.Company)
            .Include(p => p.Skills).ThenInclude(s => s.Skill)
            .OrderBy(p => p.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        logger.LogInformation("Job embedding: processing {Count} postings", postings.Count);

        int processed = 0, embedded = 0, skipped = 0, failed = 0;

        foreach (var chunk in postings.Chunk(OpenAiBatchSize))
        {
            if (ct.IsCancellationRequested) break;

            var texts = chunk.Select(p => BuildEmbeddingText(p)).ToList();

            float[][]? vectors;
            try
            {
                vectors = await GetEmbeddingsAsync(texts, apiKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenAI embedding API call failed for batch of {Count}", chunk.Length);
                failed += chunk.Length;
                processed += chunk.Length;
                await Task.Delay(1000, ct);
                continue;
            }

            if (vectors == null || vectors.Length != chunk.Length)
            {
                logger.LogWarning("Unexpected vector count from OpenAI: expected {E}, got {A}", chunk.Length, vectors?.Length ?? 0);
                failed += chunk.Length;
                processed += chunk.Length;
                continue;
            }

            var newEmbeddings = chunk.Select((posting, i) => new JobEmbedding
            {
                JobPostingId  = posting.Id,
                Embedding     = new Vector(vectors[i]),
                EmbeddingText = texts[i],
                EmbeddedAt    = DateTime.UtcNow
            }).ToList();

            db.JobEmbeddings.AddRange(newEmbeddings);
            await db.SaveChangesAsync(ct);

            embedded  += chunk.Length;
            processed += chunk.Length;
            logger.LogDebug("Embedded batch of {Count} postings", chunk.Length);

            await Task.Delay(200, ct);
        }

        logger.LogInformation(
            "Job embedding complete: processed={P} embedded={E} skipped={S} failed={F}",
            processed, embedded, skipped, failed);

        return new EmbeddingResult(processed, embedded, skipped, failed);
    }

    public async Task EmbedNewPostingsAsync(IEnumerable<long> jobPostingIds, CancellationToken ct = default)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey)) return;

        var ids = jobPostingIds.ToList();
        if (ids.Count == 0) return;

        var postings = await db.JobPostings
            .Where(p => ids.Contains(p.Id))
            .Include(p => p.Company)
            .Include(p => p.Skills).ThenInclude(s => s.Skill)
            .ToListAsync(ct);

        int embedded = 0;
        foreach (var chunk in postings.Chunk(OpenAiBatchSize))
        {
            if (ct.IsCancellationRequested) break;

            var texts = chunk.Select(BuildEmbeddingText).ToList();

            float[][]? vectors;
            try
            {
                vectors = await GetEmbeddingsAsync(texts, apiKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenAI embedding failed for batch of {Count} new postings", chunk.Length);
                continue;
            }

            if (vectors == null || vectors.Length != chunk.Length) continue;

            db.JobEmbeddings.AddRange(chunk.Select((posting, i) => new JobEmbedding
            {
                JobPostingId  = posting.Id,
                Embedding     = new Vector(vectors[i]),
                EmbeddingText = texts[i],
                EmbeddedAt    = DateTime.UtcNow
            }));
            await db.SaveChangesAsync(ct);
            embedded += chunk.Length;
        }

        logger.LogInformation("Embedded {Count} new postings", embedded);
    }

    private static string BuildEmbeddingText(JobPosting p)
    {
        var parts = new List<string> { p.Title };

        if (!string.IsNullOrEmpty(p.SeniorityLevel))  parts.Add(p.SeniorityLevel);
        if (!string.IsNullOrEmpty(p.Department))       parts.Add(p.Department);
        if (!string.IsNullOrEmpty(p.Company?.CanonicalName)) parts.Add(p.Company.CanonicalName);
        if (!string.IsNullOrEmpty(p.Company?.Industry))      parts.Add(p.Company.Industry);

        // Location
        var location = string.Join(", ", new[] { p.LocationCity, p.LocationCountry }.Where(x => !string.IsNullOrEmpty(x)));
        if (!string.IsNullOrEmpty(location)) parts.Add(location);

        // Work arrangement
        if (p.IsRemote)       parts.Add("remote");
        else if (p.IsHybrid)  parts.Add("hybrid");
        else                  parts.Add("onsite");

        // Requirements
        if (!string.IsNullOrEmpty(p.EducationLevel))  parts.Add($"{p.EducationLevel} degree");

        if (p.YearsExperienceMin.HasValue || p.YearsExperienceMax.HasValue)
        {
            var exp = p.YearsExperienceMin.HasValue && p.YearsExperienceMax.HasValue
                ? $"{p.YearsExperienceMin}-{p.YearsExperienceMax} years experience"
                : p.YearsExperienceMin.HasValue
                    ? $"{p.YearsExperienceMin}+ years experience"
                    : $"up to {p.YearsExperienceMax} years experience";
            parts.Add(exp);
        }

        // Skills
        var skills = p.Skills?.Select(s => s.Skill?.CanonicalName).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (skills?.Count > 0)
            parts.Add("Skills: " + string.Join(", ", skills));

        return string.Join(" | ", parts);
    }

    private async Task<float[][]?> GetEmbeddingsAsync(List<string> texts, string apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("OpenAI");

        var requestBody = JsonSerializer.Serialize(new
        {
            model = EmbeddingModel,
            input = texts
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI embeddings returned {Status}: {Body}", (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
            return null;
        }

        var result = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(json);
        if (result?.Data == null) return null;

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    private record OpenAiEmbeddingResponse(
        [property: JsonPropertyName("data")] List<OpenAiEmbeddingData>? Data);

    private record OpenAiEmbeddingData(
        [property: JsonPropertyName("index")]     int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
