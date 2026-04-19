using JobIntelligence.Core.Entities;

namespace JobIntelligence.Core.Interfaces;

public interface IResumeService
{
    Task<Resume> ExtractAndEmbedAsync(string resumeText, CancellationToken ct = default);
    Task<Resume> ExtractAndEmbedFromFileAsync(Stream fileStream, string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<JobMatch>> FindSimilarJobsAsync(long resumeId, int limit = 20, bool? isUs = null, CancellationToken ct = default);
}

public record JobMatch(
    long JobPostingId,
    string Title,
    string? CompanyName,
    string? CompanyLogoUrl,
    string? Industry,
    string? SeniorityLevel,
    string? LocationRaw,
    bool IsRemote,
    bool IsHybrid,
    string? ApplyUrl,
    double Similarity);
