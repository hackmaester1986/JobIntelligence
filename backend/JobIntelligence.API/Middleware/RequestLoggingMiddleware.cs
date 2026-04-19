using JobIntelligence.Core.Entities;
using JobIntelligence.Infrastructure.Persistence;

namespace JobIntelligence.API.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> IgnoredPrefixes =
    [
        "/health", "/_", "/favicon", "/assets", "/styles", "/main.", "/polyfills.", "/runtime."
    ];

    private static readonly string[] BotUserAgentTokens =
    [
        "bot", "crawler", "spider", "k6", "postman", "curl", "wget", "python-requests", "go-http-client"
    ];

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        await next(context);

        var path = context.Request.Path.Value ?? "/";

        if (IgnoredPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return;

        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (BotUserAgentTokens.Any(t => userAgent.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return;

        var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.PageVisits.Add(new PageVisit
                {
                    IpAddress = ip,
                    Path = path,
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    VisitedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch { /* don't let logging errors affect the request */ }
        });
    }
}
