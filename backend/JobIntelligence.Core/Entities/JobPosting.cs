using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class JobPosting
{
    public long Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public long CompanyId { get; set; }

    // Core fields
    public string Title { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Team { get; set; }
    public string? SeniorityLevel { get; set; }
    public string? EmploymentType { get; set; }

    // Location
    public string? LocationRaw { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationState { get; set; }
    public string? LocationCountry { get; set; }
    public bool IsRemote { get; set; }
    public bool IsHybrid { get; set; }

    // Compensation
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryCurrency { get; set; }
    public string? SalaryPeriod { get; set; }
    public bool SalaryDisclosed { get; set; }

    // Content
    public string? Description { get; set; }
    public string? DescriptionHtml { get; set; }
    public string? DescriptionHash { get; set; }
    public string? ApplyUrl { get; set; }
    public string? ApplyUrlDomain { get; set; }

    // Lifecycle
    public DateTime? PostedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public short RepostCount { get; set; }
    public long? PreviousPostingId { get; set; }

    // ML authenticity
    public decimal? AuthenticityScore { get; set; }
    public string? AuthenticityLabel { get; set; }
    public DateTime? AuthenticityScoreAt { get; set; }
    public string? SageMakerModelVersion { get; set; }

    // Raw data
    public JsonDocument? RawData { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public JobSource Source { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public ICollection<JobSkill> Skills { get; set; } = new List<JobSkill>();
    public ICollection<JobSnapshot> Snapshots { get; set; } = new List<JobSnapshot>();
    public ICollection<MlPrediction> MlPredictions { get; set; } = new List<MlPrediction>();
}
