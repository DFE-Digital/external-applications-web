using DfE.ExternalApplications.Web.Configuration;
using DfE.ExternalApplications.Web.Services.Platform;

namespace DfE.ExternalApplications.Web.Services.Tenant;

/// <inheritdoc />
public sealed class TenantIdResolver(
    PlatformConfigurationApiClient apiClient,
    ILogger<TenantIdResolver> logger) : ITenantIdResolver
{
    public const string TenantIdHeader = "X-Tenant-ID";

    public async Task<Guid?> ResolveTenantIdAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (httpContext.Request.Headers.TryGetValue(TenantIdHeader, out var headerValue) &&
            Guid.TryParse(headerValue, out var tenantFromHeader))
        {
            return tenantFromHeader;
        }

        if (httpContext.Request.Query.TryGetValue("tenantId", out var queryValue) &&
            Guid.TryParse(queryValue, out var tenantFromQuery))
        {
            return tenantFromQuery;
        }

        var host = httpContext.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            var resolution = await apiClient.ResolveTenantByHostnameAsync(host, cancellationToken);
            logger.LogDebug(
                "Resolved tenant {TenantId} ({TenantName}) from hostname {Hostname}",
                resolution.TenantId,
                resolution.TenantName,
                resolution.Hostname);

            return resolution.TenantId;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not resolve tenant for hostname {Hostname}", host);
            return null;
        }
    }
}
