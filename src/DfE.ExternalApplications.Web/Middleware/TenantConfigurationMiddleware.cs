using System.Net;
using System.Text.Json;
using DfE.ExternalApplications.Web.Configuration;
using DfE.ExternalApplications.Web.Services.Tenant;
using DfE.ExternalApplications.Web.Tenancy;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Middleware;

/// <summary>
/// Resolves the current tenant (header or hostname) and loads its configuration from the platform API.
/// </summary>
public sealed class TenantConfigurationMiddleware(
    RequestDelegate next,
    ITenantIdResolver tenantIdResolver,
    TenantConfigurationLoader tenantConfigurationLoader,
    IOptions<PlatformBootstrapOptions> bootstrapOptions,
    ILogger<TenantConfigurationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantRequestContext tenantRequestContext)
    {
        if (!bootstrapOptions.Value.Enabled)
        {
            await next(context);
            return;
        }

        var tenantId = await tenantIdResolver.ResolveTenantIdAsync(context, context.RequestAborted);
        if (tenantId is null)
        {
            logger.LogWarning(
                "No tenant could be resolved for {Method} {Path} (Host={Host})",
                context.Request.Method,
                context.Request.Path,
                context.Request.Host.Value);

            await WriteErrorAsync(context, "Could not resolve tenant from request.");
            return;
        }

        try
        {
            var tenantConfig = await tenantConfigurationLoader.LoadAsync(tenantId.Value, context.RequestAborted);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(tenantConfig.Configuration.ToList())
                .Build();

            tenantRequestContext.TenantId = tenantConfig.TenantId;
            tenantRequestContext.TenantName = tenantConfig.TenantName;
            tenantRequestContext.TenantConfiguration = configuration;

            context.Items[TenantApiRequestHandler.TenantIdItemKey] = tenantConfig.TenantId;

            using (logger.BeginScope(new Dictionary<string, object>
                   {
                       ["TenantId"] = tenantConfig.TenantId,
                       ["TenantName"] = tenantConfig.TenantName
                   }))
            {
                await next(context);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to load tenant configuration for tenant {TenantId}", tenantId);
            await WriteErrorAsync(context, "Failed to load tenant configuration from platform API.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
    }
}
