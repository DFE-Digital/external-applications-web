using DfE.ExternalApplications.Web.Configuration;
using DfE.ExternalApplications.Web.Tenancy;
using GovUK.Dfe.ExternalApplications.Api.Client.Extensions;
using GovUK.Dfe.ExternalApplications.Api.Client.Settings;
using GovUK.Dfe.CoreLibs.Security.TokenRefresh.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Extensions;

/// <summary>
/// Registers tenant-aware platform bootstrap services for API clients and authentication.
/// </summary>
public static class TenantAwareServiceCollectionExtensions
{
    /// <summary>
    /// Registers tenant-scoped API client settings and token refresh options when platform bootstrap is enabled.
    /// </summary>
    public static IServiceCollection AddTenantAwarePlatformServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var bootstrap = configuration.GetSection(PlatformBootstrapOptions.SectionName).Get<PlatformBootstrapOptions>();
        if (bootstrap is not { Enabled: true })
        {
            return services;
        }

        services.AddScoped<IApiClientSettingsProvider, TenantApiClientSettingsProvider>();
        services.AddScoped<IOptions<TokenRefreshOptions>, TenantTokenRefreshOptionsAccessor>();
        services.AddScoped<ITenantAppConfiguration, TenantAppConfiguration>();

        return services;
    }
}
