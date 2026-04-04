namespace JobIntelligence.Core.Entities;

public static class CompanyIndustries
{
    public static readonly IReadOnlyList<string> All =
    [
        "Technology",
        "Software/SaaS",
        "Developer Tools",
        "AI/ML",
        "Cloud",
        "Healthcare",
        "Biotech & Pharma",
        "Finance",
        "Fintech",
        "Insurance",
        "Manufacturing",
        "Aerospace & Defense",
        "Automotive",
        "Energy",
        "Utilities",
        "Retail",
        "E-commerce",
        "Food & Beverage",
        "Hospitality",
        "Media",
        "Gaming",
        "Telecommunications",
        "Cybersecurity",
        "Education",
        "Higher Education",
        "Nonprofit",
        "Government",
        "Real Estate",
        "Logistics & Transportation",
        "Consulting",
        "Legal Services",
        "Marketing",
        "Agriculture",
        "Construction",
        "Mining",
        "Other",
    ];

    /// <summary>Comma-separated list for use in LLM prompts.</summary>
    public static readonly string PromptList = string.Join(", ", All);
}
