namespace JobIntelligence.Core.Entities;

public static class JobSeniorities
{
    public static readonly IReadOnlyList<string> All =
    [
        "intern",
        "junior",
        "senior",
        "lead",
        "staff",
        "principal",
        "manager",
        "director",
        "vp",
        "executive",
    ];

    /// <summary>
    /// Normalizes free-text seniority labels (e.g. from SmartRecruiters) to a canonical value.
    /// Returns null for unrecognized or non-applicable values so they are stored as null.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return raw.Trim().ToLowerInvariant() switch
        {
            // Already canonical
            "intern"      => "intern",
            "junior"      => "junior",
            "senior"      => "senior",
            "lead"        => "lead",
            "staff"       => "staff",
            "principal"   => "principal",
            "manager"     => "manager",
            "director"    => "director",
            "vp"          => "vp",
            "executive"   => "executive",

            // SmartRecruiters / LinkedIn labels
            "internship"        => "intern",
            "entry level"       => "junior",
            "associate"         => "junior",
            "mid-senior level"  => "senior",
            "vice president"    => "vp",
            "not applicable"    => null,

            _ => null
        };
    }
}
