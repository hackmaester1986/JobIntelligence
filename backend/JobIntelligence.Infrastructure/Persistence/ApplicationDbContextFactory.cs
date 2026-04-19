using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Pgvector.EntityFrameworkCore;

namespace JobIntelligence.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core CLI (migrations, database update).
/// Registers pgvector so the Vector column type is recognized at design time.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Walk up from Infrastructure to find the API project's appsettings
        var basePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../JobIntelligence.API"));

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in appsettings");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
