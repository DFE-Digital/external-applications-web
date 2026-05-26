using DfE.ExternalApplications.Web.Services.Tenant;

namespace DfE.ExternalApplications.Web.Middleware;

/// <summary>
/// Adds <c>X-Tenant-ID</c> to outbound API requests when platform tenant resolution is enabled.
/// </summary>
public sealed class TenantApiRequestHandler : DelegatingHandler
{
    public const string TenantIdItemKey = "Platform.TenantId";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantApiRequestHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(TenantIdItemKey, out var tenantIdObj) == true &&
            tenantIdObj is Guid tenantId)
        {
            request.Headers.Remove(TenantIdResolver.TenantIdHeader);
            request.Headers.Add(TenantIdResolver.TenantIdHeader, tenantId.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
