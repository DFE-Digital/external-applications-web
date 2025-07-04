using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Middleware
{
    [ExcludeFromCodeCoverage]
    public class HostTemplateMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<HostTemplateMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            if (string.IsNullOrEmpty(context.Session.GetString("TemplateId")))
            {
                var host = context.Request.Host.Host;
                var mappings = configuration.GetSection("Template:HostMappings").Get<Dictionary<string, string>>() ?? new();
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
                }
            }

            await next(context);
        }
    }

    [ExcludeFromCodeCoverage]
    public static class HostTemplateMiddlewareExtensions
    {
        public static IApplicationBuilder UseHostTemplateResolution(this IApplicationBuilder app)
        {
            return app.UseMiddleware<HostTemplateMiddleware>();
        }
    }
}