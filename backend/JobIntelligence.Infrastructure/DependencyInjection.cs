using Anthropic;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Collectors;
using JobIntelligence.Infrastructure.Persistence;
using JobIntelligence.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobIntelligence.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddHttpClient("Greenhouse", client =>
        {
            client.BaseAddress = new Uri("https://boards-api.greenhouse.io/");
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
        });

        services.AddHttpClient("Lever", client =>
        {
            client.BaseAddress = new Uri("https://api.lever.co/");
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
        });

        services.AddHttpClient("Ashby", client =>
        {
            client.BaseAddress = new Uri("https://api.ashbyhq.com/");
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
        });

        services.AddScoped<IJobCollector, GreenhouseCollector>();
        services.AddScoped<IJobCollector, LeverCollector>();
        services.AddScoped<IJobCollector, AshbyCollector>();
        services.AddScoped<IJobCollector, SmartRecruitersCollector>();
        services.AddScoped<IJobCollector, WorkdayCollector>();
        services.AddScoped<ICollectionOrchestrator, CollectionOrchestrator>();
        services.AddScoped<ICompanyDiscoveryService, CompanyDiscoveryService>();

        services.AddHttpClient("SmartRecruiters", client =>
        {
            client.BaseAddress = new Uri("https://api.smartrecruiters.com/");
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
        });

        services.AddHttpClient("Workday", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient("WorkdayJobs", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("CommonCrawl", client =>
        {
            client.BaseAddress = new Uri("https://index.commoncrawl.org/");
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0 (research; contact via github)");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddScoped<ICommonCrawlService, CommonCrawlService>();

        services.AddHttpClient("Wikidata", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "JobIntelligence/1.0 (company enrichment; contact via github)");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IWikidataEnrichmentService, WikidataEnrichmentService>();
        services.AddScoped<IDescriptionEnrichmentService, DescriptionEnrichmentService>();
        services.AddScoped<IWebEnrichmentService, WebEnrichmentService>();
        services.AddScoped<ICompanyStatsService, CompanyStatsService>();

        var apiKey = configuration["Anthropic:ApiKey"] ?? throw new InvalidOperationException("Anthropic:ApiKey not configured");
        services.AddSingleton(new AnthropicClient { ApiKey = apiKey });
        services.AddScoped<IChatService, ChatService>();

        return services;
    }
}
