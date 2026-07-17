using DfE.ExternalApplications.Web.Security;
using DfE.ExternalApplications.Web.Services;

namespace DfE.ExternalApplications.Web.Middleware;

/// <summary>
/// Ensures an authenticated user has a valid session template before entering application routes.
/// Auto-selects when exactly one template is available; otherwise redirects to the chooser.
/// </summary>
public sealed class TemplateSelectionMiddleware(
    RequestDelegate next,
    ILogger<TemplateSelectionMiddleware> logger)
{
    private static readonly PathString TemplatesPath = new("/templates");
    private static readonly PathString DashboardPath = new("/applications/dashboard");

    public async Task InvokeAsync(HttpContext context, ITemplateSelectionService templateSelectionService)
    {
        if (!ShouldEnforce(context))
        {
            await next(context);
            return;
        }

        try
        {
            var templates = await templateSelectionService.GetSelectableTemplatesAsync(context.RequestAborted);

            if (templateSelectionService.HasValidSelection(context, templates))
            {
                await next(context);
                return;
            }

            if (templates.Count == 1)
            {
                templateSelectionService.SelectTemplate(context, templates[0].TemplateId);
                logger.LogDebug(
                    "Auto-selected sole accessible template {TemplateId}",
                    templates[0].TemplateId);

                if (IsRoot(context.Request.Path))
                {
                    context.Response.Redirect(DashboardPath.Value!);
                    return;
                }

                await next(context);
                return;
            }

            // 0 or many templates — send the user to the chooser (empty state or pick list).
            var returnUrl = context.Request.Path + context.Request.QueryString;
            var target = templates.Count == 0
                ? TemplatesPath.Value!
                : $"{TemplatesPath.Value}?returnUrl={Uri.EscapeDataString(returnUrl)}";

            context.Response.Redirect(target);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Template selection gate failed; redirecting to template chooser");
            context.Response.Redirect(TemplatesPath.Value!);
        }
    }

    private static bool ShouldEnforce(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var path = context.Request.Path;
        if (AuthenticationPathExclusions.ShouldSkip(path))
        {
            return false;
        }

        if (path.StartsWithSegments(TemplatesPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/Health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/assets", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Enforce for landing and application areas; admin can open /templates and /admin freely.
        return IsRoot(path) ||
               path.StartsWithSegments("/applications", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoot(PathString path)
        => !path.HasValue || path.Value is "/" or "";
}

/// <summary>
/// Extension methods for <see cref="TemplateSelectionMiddleware"/>.
/// </summary>
public static class TemplateSelectionMiddlewareExtensions
{
    /// <summary>
    /// Adds template selection gating after authentication.
    /// </summary>
    public static IApplicationBuilder UseTemplateSelection(this IApplicationBuilder app)
        => app.UseMiddleware<TemplateSelectionMiddleware>();
}
