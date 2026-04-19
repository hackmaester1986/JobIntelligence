using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobIntelligence.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<JobSource> JobSources => Set<JobSource>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<SkillTaxonomy> SkillTaxonomies => Set<SkillTaxonomy>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<JobSnapshot> JobSnapshots => Set<JobSnapshot>();
    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();
    public DbSet<MlPrediction> MlPredictions => Set<MlPrediction>();
    public DbSet<CompanyJobSnapshot> CompanyJobSnapshots => Set<CompanyJobSnapshot>();
    public DbSet<DashboardSnapshot> DashboardSnapshots => Set<DashboardSnapshot>();
    public DbSet<PageVisit> PageVisits => Set<PageVisit>();
    public DbSet<ChatLog> ChatLogs => Set<ChatLog>();
    public DbSet<JobEmbedding> JobEmbeddings => Set<JobEmbedding>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<ResumeEmbedding> ResumeEmbeddings => Set<ResumeEmbedding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
