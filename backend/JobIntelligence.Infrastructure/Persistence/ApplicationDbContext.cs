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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
