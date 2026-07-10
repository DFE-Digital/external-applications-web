using DfE.ExternalApplications.Web.Tenancy;

namespace DfE.ExternalApplications.Web.Middleware;

/// <summary>
/// Resolves the default template id from tenant or host configuration.
/// </summary>
public sealed class HostTemplateMiddleware(
    RequestDelegate next,
    IConfiguration hostConfiguration,
    ITenantRequestContext tenantRequestContext,
    ILogger<HostTemplateMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(context.Session.GetString("TemplateId")))
        {
            var configuration = tenantRequestContext.TenantConfiguration ?? hostConfiguration;
            var host = context.Request.Host.Host;
            var mappings = configuration.GetSection("Template:HostMappings").Get<Dictionary<string, string>>() ?? [];
            string? templateId = null;

            foreach (var kvp in mappings)
            {
                if (host.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    templateId = kvp.Value;
                    break;
                }
            }

            templateId ??= configuration["Template:Id"];
            if (!string.IsNullOrEmpty(templateId))
            {
                context.Session.SetString("TemplateId", templateId);
                logger.LogDebug("Resolved TemplateId {TemplateId} for host {Host}", templateId, host);
            }
        }

        await next(context);
    }
}

public static class HostTemplateMiddlewareExtensions
{
    public static IApplicationBuilder UseHostTemplateResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<HostTemplateMiddleware>();
    }
}
