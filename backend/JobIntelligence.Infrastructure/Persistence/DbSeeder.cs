using JobIntelligence.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobIntelligence.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        await SeedJobSourcesAsync(db, logger);
        await SeedInitialCompaniesAsync(db, logger);
        await SeedSkillTaxonomyAsync(db, logger);
    }

    private static async Task SeedJobSourcesAsync(ApplicationDbContext db, ILogger logger)
    {
        var desired = new List<JobSource>
        {
            new() { Name = "greenhouse", BaseUrl = "https://boards-api.greenhouse.io/", ApiVersion = "v1", IsActive = true, RateLimitRps = 5 },
            new() { Name = "lever", BaseUrl = "https://api.lever.co/", ApiVersion = "v0", IsActive = true, RateLimitRps = 5 },
            new() { Name = "ashby", BaseUrl = "https://api.ashbyhq.com/", ApiVersion = "v1", IsActive = true, RateLimitRps = 5 },
            new() { Name = "smartrecruiters", BaseUrl = "https://api.smartrecruiters.com/", ApiVersion = "v1", IsActive = true, RateLimitRps = 5 },
            new() { Name = "recruitee", BaseUrl = "https://recruitee.com/", ApiVersion = "v1", IsActive = true, RateLimitRps = 5 }
        };

        var existing = await db.JobSources.Select(s => s.Name).ToHashSetAsync();
        var toAdd = desired.Where(s => !existing.Contains(s.Name)).ToList();
        if (toAdd.Count == 0) return;

        db.JobSources.AddRange(toAdd);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} job sources", toAdd.Count);
    }

    private static async Task SeedInitialCompaniesAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.Companies.AnyAsync()) return;

        var companies = new List<Company>
        {
            // Greenhouse companies
            new() { CanonicalName = "Stripe", NormalizedName = "stripe", GreenhouseBoardToken = "stripe", Industry = "Fintech" },
            new() { CanonicalName = "Airbnb", NormalizedName = "airbnb", GreenhouseBoardToken = "airbnb", Industry = "Travel & Hospitality" },
            new() { CanonicalName = "Figma", NormalizedName = "figma", GreenhouseBoardToken = "figma", Industry = "Design Software" },
            new() { CanonicalName = "Notion", NormalizedName = "notion", GreenhouseBoardToken = "notion", Industry = "Productivity Software" },
            new() { CanonicalName = "Vercel", NormalizedName = "vercel", GreenhouseBoardToken = "vercel", Industry = "Developer Tools" },
            new() { CanonicalName = "Linear", NormalizedName = "linear", GreenhouseBoardToken = "linear", Industry = "Developer Tools" },
            new() { CanonicalName = "Retool", NormalizedName = "retool", GreenhouseBoardToken = "retool", Industry = "Developer Tools" },
            new() { CanonicalName = "Brex", NormalizedName = "brex", GreenhouseBoardToken = "brex", Industry = "Fintech" },
            new() { CanonicalName = "Scale AI", NormalizedName = "scale ai", GreenhouseBoardToken = "scaleai", Industry = "Artificial Intelligence" },
            new() { CanonicalName = "Anthropic", NormalizedName = "anthropic", GreenhouseBoardToken = "anthropic", Industry = "Artificial Intelligence" },

            // Lever companies
            new() { CanonicalName = "Airtable", NormalizedName = "airtable", LeverCompanySlug = "airtable", Industry = "Productivity Software" },
            new() { CanonicalName = "Robinhood", NormalizedName = "robinhood", LeverCompanySlug = "robinhood", Industry = "Fintech" },
            new() { CanonicalName = "Flexport", NormalizedName = "flexport", LeverCompanySlug = "flexport", Industry = "Logistics" },
            new() { CanonicalName = "Reddit", NormalizedName = "reddit", LeverCompanySlug = "reddit", Industry = "Social Media" },
            new() { CanonicalName = "Asana", NormalizedName = "asana", LeverCompanySlug = "asana", Industry = "Productivity Software" },

            // Companies on both
            new() { CanonicalName = "OpenAI", NormalizedName = "openai", GreenhouseBoardToken = "openai", Industry = "Artificial Intelligence" },
            new() { CanonicalName = "Databricks", NormalizedName = "databricks", GreenhouseBoardToken = "databricks", Industry = "Data & Analytics" },
            new() { CanonicalName = "Plaid", NormalizedName = "plaid", GreenhouseBoardToken = "plaid", Industry = "Fintech" },
        };

        db.Companies.AddRange(companies);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} companies", companies.Count);
    }

    private static async Task SeedSkillTaxonomyAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.SkillTaxonomies.AnyAsync()) return;

        static SkillTaxonomy S(string name, string category, params string[] aliases) => new()
        {
            CanonicalName = name,
            Category = category,
            Aliases = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(aliases))
        };

        var skills = new List<SkillTaxonomy>
        {
            // Languages
            S("Python", "language"),
            S("JavaScript", "language", "JS", "ECMAScript"),
            S("TypeScript", "language", "TS"),
            S("Java", "language"),
            S("C#", "language", "CSharp", "C Sharp", "dotnet", ".NET"),
            S("C++", "language", "CPlusPlus", "cpp"),
            S("C", "language"),
            S("Go", "language", "Golang"),
            S("Rust", "language"),
            S("Ruby", "language"),
            S("PHP", "language"),
            S("Swift", "language"),
            S("Kotlin", "language"),
            S("Scala", "language"),
            S("R", "language"),
            S("MATLAB", "language"),
            S("Perl", "language"),
            S("Haskell", "language"),
            S("Elixir", "language"),
            S("Clojure", "language"),
            S("Dart", "language"),
            S("Lua", "language"),
            S("Bash", "language", "Shell", "Shell Scripting"),
            S("PowerShell", "language"),
            S("SQL", "language"),

            // Frontend
            S("React", "frontend", "React.js", "ReactJS"),
            S("Angular", "frontend", "AngularJS"),
            S("Vue.js", "frontend", "Vue", "VueJS"),
            S("Next.js", "frontend", "NextJS"),
            S("Nuxt.js", "frontend", "Nuxt"),
            S("Svelte", "frontend", "SvelteKit"),
            S("HTML", "frontend", "HTML5"),
            S("CSS", "frontend", "CSS3"),
            S("Sass", "frontend", "SCSS"),
            S("Tailwind CSS", "frontend", "Tailwind"),
            S("Bootstrap", "frontend"),
            S("Redux", "frontend", "Redux Toolkit"),
            S("Webpack", "frontend"),
            S("Vite", "frontend"),
            S("Jest", "frontend"),
            S("Cypress", "frontend"),
            S("Playwright", "frontend"),
            S("Storybook", "frontend"),
            S("WebSockets", "frontend", "WebSocket"),

            // Backend
            S("Node.js", "backend", "NodeJS", "Node"),
            S("Express.js", "backend", "Express", "ExpressJS"),
            S("Django", "backend"),
            S("Flask", "backend"),
            S("FastAPI", "backend"),
            S("Spring Boot", "backend", "Spring"),
            S("ASP.NET", "backend", "ASP.NET Core"),
            S("Ruby on Rails", "backend", "Rails"),
            S("Laravel", "backend"),
            S("NestJS", "backend", "Nest.js"),
            S("GraphQL", "backend"),
            S("REST API", "backend", "RESTful", "REST"),
            S("gRPC", "backend"),
            S("Microservices", "backend", "Micro-services"),

            // Databases
            S("PostgreSQL", "database", "Postgres"),
            S("MySQL", "database"),
            S("MongoDB", "database", "Mongo"),
            S("Redis", "database"),
            S("Elasticsearch", "database", "Elastic"),
            S("DynamoDB", "database"),
            S("Cassandra", "database", "Apache Cassandra"),
            S("SQLite", "database"),
            S("SQL Server", "database", "MSSQL", "Microsoft SQL Server"),
            S("Oracle", "database", "Oracle DB"),
            S("Neo4j", "database"),
            S("InfluxDB", "database"),
            S("Snowflake", "database"),
            S("BigQuery", "database", "Google BigQuery"),
            S("Redshift", "database", "Amazon Redshift"),

            // Cloud
            S("AWS", "cloud", "Amazon Web Services"),
            S("Azure", "cloud", "Microsoft Azure"),
            S("GCP", "cloud", "Google Cloud", "Google Cloud Platform"),
            S("Kubernetes", "cloud", "K8s"),
            S("Docker", "cloud"),
            S("Terraform", "cloud"),
            S("Helm", "cloud"),
            S("CloudFormation", "cloud", "AWS CloudFormation"),
            S("Lambda", "cloud", "AWS Lambda"),
            S("S3", "cloud", "AWS S3"),
            S("EC2", "cloud", "AWS EC2"),
            S("EKS", "cloud", "AWS EKS"),
            S("GKE", "cloud", "Google Kubernetes Engine"),
            S("AKS", "cloud", "Azure Kubernetes Service"),
            S("Serverless", "cloud"),
            S("CDN", "cloud"),

            // DevOps
            S("CI/CD", "devops", "Continuous Integration", "Continuous Deployment"),
            S("Jenkins", "devops"),
            S("GitHub Actions", "devops"),
            S("GitLab CI", "devops", "GitLab CI/CD"),
            S("CircleCI", "devops"),
            S("ArgoCD", "devops", "Argo CD"),
            S("Ansible", "devops"),
            S("Linux", "devops", "Unix"),
            S("Prometheus", "devops"),
            S("Grafana", "devops"),
            S("Datadog", "devops"),
            S("New Relic", "devops"),
            S("Splunk", "devops"),
            S("OpenTelemetry", "devops", "OTel"),
            S("Nginx", "devops"),
            S("Apache", "devops", "Apache HTTP Server"),

            // Data & Analytics
            S("Apache Spark", "data", "Spark", "PySpark"),
            S("Apache Kafka", "data", "Kafka"),
            S("Apache Airflow", "data", "Airflow"),
            S("dbt", "data", "data build tool"),
            S("Pandas", "data"),
            S("NumPy", "data", "Numpy"),
            S("Tableau", "data"),
            S("Power BI", "data", "PowerBI"),
            S("Looker", "data"),
            S("Databricks", "data"),
            S("Hadoop", "data", "Apache Hadoop"),
            S("Flink", "data", "Apache Flink"),
            S("ETL", "data", "ELT"),
            S("dbt Core", "data"),
            S("Fivetran", "data"),
            S("Stitch", "data"),

            // ML & AI
            S("TensorFlow", "ml"),
            S("PyTorch", "ml"),
            S("scikit-learn", "ml", "sklearn"),
            S("Keras", "ml"),
            S("Hugging Face", "ml"),
            S("MLflow", "ml"),
            S("LangChain", "ml"),
            S("OpenAI API", "ml", "OpenAI"),
            S("NLP", "ml", "Natural Language Processing"),
            S("Computer Vision", "ml", "CV"),
            S("Deep Learning", "ml"),
            S("Machine Learning", "ml", "ML"),
            S("LLM", "ml", "Large Language Model"),
            S("RAG", "ml", "Retrieval Augmented Generation"),
            S("Fine-tuning", "ml"),
            S("Feature Engineering", "ml"),
            S("A/B Testing", "ml", "AB Testing"),
            S("Statistical Analysis", "ml"),

            // Mobile
            S("iOS", "mobile"),
            S("Android", "mobile"),
            S("React Native", "mobile"),
            S("Flutter", "mobile"),
            S("SwiftUI", "mobile", "Swift UI"),
            S("Jetpack Compose", "mobile"),
            S("Xcode", "mobile"),

            // Security
            S("OWASP", "security"),
            S("OAuth", "security", "OAuth2"),
            S("JWT", "security", "JSON Web Token"),
            S("SAML", "security"),
            S("SSL/TLS", "security", "TLS", "SSL"),
            S("Zero Trust", "security"),
            S("IAM", "security", "Identity and Access Management"),
            S("SIEM", "security"),
            S("SOC 2", "security", "SOC2"),
            S("Penetration Testing", "security", "Pen Testing"),

            // Tools & Practices
            S("Git", "tools", "Version Control"),
            S("GitHub", "tools"),
            S("GitLab", "tools"),
            S("Jira", "tools"),
            S("Confluence", "tools"),
            S("Figma", "tools"),
            S("Postman", "tools"),
            S("Agile", "practice"),
            S("Scrum", "practice"),
            S("Kanban", "practice"),
            S("TDD", "practice", "Test Driven Development"),
            S("BDD", "practice", "Behavior Driven Development"),
            S("DDD", "practice", "Domain Driven Design"),
            S("Event-Driven Architecture", "practice", "Event Driven"),
            S("CQRS", "practice"),
            S("System Design", "practice"),
            S("Code Review", "practice"),
            S("Documentation", "practice"),
        };

        db.SkillTaxonomies.AddRange(skills);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} skills", skills.Count);
    }
}
