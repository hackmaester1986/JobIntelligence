using System.Text.Json;

namespace JobIntelligence.Core.Entities;

public class DashboardSnapshot
{
    public int Id { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
    public bool IsUs { get; set; }
    public long TotalActiveJobs { get; set; }
    public long TotalCompanies { get; set; }
    public long RemoteJobs { get; set; }
    public long HybridJobs { get; set; }
    public long OnsiteJobs { get; set; }
    public long ActiveToday { get; set; }
    public JsonDocument TopCompanies { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument JobsBySeniority { get; set; } = JsonDocument.Parse("[]");
    public JsonDocument TopDepartments { get; set; } = JsonDocument.Parse("[]");
}
