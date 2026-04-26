namespace JobIntelligence.Core.Entities;

public class Company
{
    public long Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string? GreenhouseBoardToken { get; set; }
    public string? LeverCompanySlug { get; set; }
    public string? AshbyBoardSlug { get; set; }
    public string? WorkableSlug { get; set; }
    public string? SmartRecruitersSlug { get; set; }
    public string? RecruiteeSlug { get; set; }
    public string? WorkdayHost { get; set; }
    public string? WorkdayCareerSite { get; set; }
    public string? Industry { get; set; }
    public bool? IsTechHiring { get; set; }
    public string? EmployeeCountRange { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeadquartersCity { get; set; }
    public string? HeadquartersCountry { get; set; }
    public string? WikidataId { get; set; }
    public DateTime? WikidataEnrichedAt { get; set; }
    public DateTime? DescriptionEnrichedAt { get; set; }
    public DateTime? WebEnrichedAt { get; set; }
    public DateTime? SizeEnrichedAt { get; set; }
    public int? FoundingYear { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Hiring stats (denormalized, recomputed after each collection run)
    public int ActiveJobCount { get; set; }
    public int RemovedJobCount { get; set; }
    public int RemoteJobCount { get; set; }
    public int TotalJobsEverSeen { get; set; }
    public int DuplicateJobCount { get; set; }
    public double? AvgJobLifetimeDays { get; set; }
    public double? AvgRepostCount { get; set; }
    public double? SalaryDisclosureRate { get; set; }
    public DateTime? StatsComputedAt { get; set; }

    public ICollection<JobPosting> JobPostings { get; set; } = new List<JobPosting>();
}
