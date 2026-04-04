using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JobIntelligence.API.Filters;

/// <summary>
/// Requires a valid X-Admin-Key header matching the configured AdminKey value.
/// Apply to internal controllers/actions that should not be publicly accessible.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminKeyAttribute : Attribute, IResourceFilter
{
    private const string HeaderName = "X-Admin-Key";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = config["AdminKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            // Fail closed: if no key is configured, deny all requests
            context.Result = new StatusCodeResult(503);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedKey)
            || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
