using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Anthropic;
using Anthropic.Models.Messages;
using JobIntelligence.Core.Entities;
using UglyToad.PdfPig;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace JobIntelligence.Infrastructure.Services;

public class ResumeService(
    AnthropicClient anthropic,
    IHttpClientFactory httpClientFactory,
    ApplicationDbContext db,
    IConfiguration configuration,
    ILogger<ResumeService> logger) : IResumeService
{
    private const string EmbeddingModel = "text-embedding-3-small";

    public async Task<Resume> ExtractAndEmbedFromFileAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string text;

        try
        {
            text = ext switch
            {
                ".pdf"  => ExtractPdfText(fileStream),
                ".docx" => ExtractDocxText(fileStream),
                ".txt"  => await new StreamReader(fileStream).ReadToEndAsync(ct),
                _       => throw new NotSupportedException($"Unsupported file type: {ext}")
            };
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract text from {FileName}", fileName);
            throw new InvalidOperationException($"Could not read file: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("No text could be extracted from the file.");

        return await ExtractAndEmbedAsync(text, ct);
    }

    public async Task<Resume> ExtractAndEmbedAsync(string resumeText, CancellationToken ct = default)
    {
        var extracted = await ExtractResumeDataAsync(resumeText, ct);

        var resume = new Resume
        {
            RawText           = resumeText,
            Name              = extracted.Name,
            Email             = extracted.Email,
            Location          = extracted.Location,
            YearsOfExperience = extracted.YearsOfExperience,
            EducationLevel    = extracted.EducationLevel,
            EducationField    = extracted.EducationField,
            Skills            = extracted.Skills ?? [],
            RecentJobTitles   = extracted.RecentJobTitles ?? [],
            Industries        = extracted.Industries ?? [],
            CreatedAt         = DateTime.UtcNow
        };

        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        await EmbedResumeAsync(resume, ct);

        return resume;
    }

    public async Task<IReadOnlyList<JobMatch>> FindSimilarJobsAsync(
        long resumeId, int limit = 20, bool? isUs = null, CancellationToken ct = default)
    {
        var resumeEmbedding = await db.ResumeEmbeddings
            .Where(e => e.ResumeId == resumeId)
            .FirstOrDefaultAsync(ct);

        if (resumeEmbedding == null)
        {
            logger.LogWarning("No embedding found for resume {ResumeId}", resumeId);
            return [];
        }

        var queryVector = resumeEmbedding.Embedding;

        var query = db.JobEmbeddings
            .Where(e => e.JobPosting.IsActive && e.JobPosting.Company.IsTechHiring != false);

        if (isUs == true)
            query = query.Where(e => e.JobPosting.IsUsPosting == true || e.JobPosting.IsUsPosting == null);
        else if (isUs == false)
            query = query.Where(e => e.JobPosting.IsUsPosting == false || e.JobPosting.IsUsPosting == null);

        var results = await query
            .OrderBy(e => e.Embedding.CosineDistance(queryVector))
            .Take(limit)
            .Select(e => new
            {
                e.JobPostingId,
                Distance       = e.Embedding.CosineDistance(queryVector),
                e.JobPosting.Title,
                CompanyName    = e.JobPosting.Company.CanonicalName,
                CompanyLogoUrl = e.JobPosting.Company.LogoUrl,
                Industry       = e.JobPosting.Company.Industry,
                e.JobPosting.SeniorityLevel,
                e.JobPosting.LocationRaw,
                e.JobPosting.IsRemote,
                e.JobPosting.IsHybrid,
                e.JobPosting.ApplyUrl,
            })
            .ToListAsync(ct);

        return results
            .Select(r => new JobMatch(
                r.JobPostingId,
                r.Title,
                r.CompanyName,
                r.CompanyLogoUrl,
                r.Industry,
                r.SeniorityLevel,
                r.LocationRaw,
                r.IsRemote,
                r.IsHybrid,
                r.ApplyUrl,
                Math.Round(1.0 - r.Distance, 4)))
            .ToList();
    }

    private async Task EmbedResumeAsync(Resume resume, CancellationToken ct)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            logger.LogWarning("OpenAI:ApiKey not configured — skipping resume embedding");
            return;
        }

        var embeddingText = BuildEmbeddingText(resume);
        var vectors = await GetEmbeddingsAsync([embeddingText], apiKey, ct);

        if (vectors == null || vectors.Length == 0)
        {
            logger.LogError("Failed to get embedding for resume {ResumeId}", resume.Id);
            return;
        }

        db.ResumeEmbeddings.Add(new ResumeEmbedding
        {
            ResumeId      = resume.Id,
            Embedding     = new Vector(vectors[0]),
            EmbeddingText = embeddingText,
            EmbeddedAt    = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Embedded resume {ResumeId}", resume.Id);
    }

    private async Task<ExtractedResume> ExtractResumeDataAsync(string resumeText, CancellationToken ct)
    {
        var truncated = resumeText.Length > 8000 ? resumeText[..8000] : resumeText;

        var prompt = $"""
            Extract structured information from the following resume. Return ONLY a JSON object with these fields:
            - name: string or null
            - email: string or null
            - location: string or null (city, state or country)
            - years_of_experience: integer or null (total years of professional experience)
            - education_level: one of "phd", "master", "bachelor", "associate", or null
            - education_field: string or null (e.g. "Computer Science", "Electrical Engineering")
            - skills: array of strings (technical skills, tools, languages, frameworks — be specific)
            - recent_job_titles: array of strings (last 3 job titles, most recent first)
            - industries: array of strings (industries the person has worked in)

            Resume:
            {truncated}
            """;

        try
        {
            var response = await anthropic.Messages.Create(new MessageCreateParams
            {
                Model     = Model.ClaudeHaiku4_5_20251001,
                MaxTokens = 1024,
                System    = "You are a resume parser. Return only valid JSON with no markdown fences or explanation.",
                Messages  = [new MessageParam { Role = Role.User, Content = prompt }]
            });

            string? rawText = null;
            foreach (var block in response.Content)
                if (block.TryPickText(out TextBlock? tb) && tb != null)
                    rawText = tb.Text;

            var cleaned = (rawText ?? "{}").Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n').Skip(1).SkipLast(1).Aggregate((a, b) => a + "\n" + b);

            return JsonSerializer.Deserialize<ExtractedResume>(cleaned, JsonOptions) ?? new ExtractedResume();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to extract resume data via Claude");
            return new ExtractedResume();
        }
    }

    private async Task<float[][]?> GetEmbeddingsAsync(List<string> texts, string apiKey, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("OpenAI");

        var requestBody = JsonSerializer.Serialize(new { model = EmbeddingModel, input = texts });
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

    private static string BuildEmbeddingText(Resume r)
    {
        var parts = new List<string>();

        if (r.RecentJobTitles.Length > 0)
            parts.Add(string.Join(", ", r.RecentJobTitles));

        if (!string.IsNullOrEmpty(r.EducationLevel))
            parts.Add($"{r.EducationLevel} degree");

        if (!string.IsNullOrEmpty(r.EducationField))
            parts.Add(r.EducationField);

        if (!string.IsNullOrEmpty(r.Location))
            parts.Add(r.Location);

        if (r.YearsOfExperience.HasValue)
            parts.Add($"{r.YearsOfExperience} years experience");

        if (r.Industries.Length > 0)
            parts.Add(string.Join(", ", r.Industries));

        if (r.Skills.Length > 0)
            parts.Add("Skills: " + string.Join(", ", r.Skills));

        return string.Join(" | ", parts);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private record ExtractedResume
    {
        [JsonPropertyName("name")]               public string? Name { get; init; }
        [JsonPropertyName("email")]              public string? Email { get; init; }
        [JsonPropertyName("location")]           public string? Location { get; init; }
        [JsonPropertyName("years_of_experience")] public int? YearsOfExperience { get; init; }
        [JsonPropertyName("education_level")]    public string? EducationLevel { get; init; }
        [JsonPropertyName("education_field")]    public string? EducationField { get; init; }
        [JsonPropertyName("skills")]             public string[]? Skills { get; init; }
        [JsonPropertyName("recent_job_titles")]  public string[]? RecentJobTitles { get; init; }
        [JsonPropertyName("industries")]         public string[]? Industries { get; init; }
    }

private static string ExtractPdfText(Stream stream)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(stream);
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    private static string ExtractDocxText(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("Not a valid DOCX file.");
        using var xmlStream = entry.Open();
        var xdoc = XDocument.Load(xmlStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var parts = xdoc.Descendants(w + "t")
            .Select(e => e.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v));
        return string.Join(" ", parts);
    }

    private record OpenAiEmbeddingResponse(
        [property: JsonPropertyName("data")] List<OpenAiEmbeddingData>? Data);

    private record OpenAiEmbeddingData(
        [property: JsonPropertyName("index")]     int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
