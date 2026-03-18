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
    public string? Industry { get; set; }
    public bool? IsTechHiring { get; set; }
    public string? EmployeeCountRange { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeadquartersCity { get; set; }
    public string? HeadquartersCountry { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<JobPosting> JobPostings { get; set; } = new List<JobPosting>();
}
