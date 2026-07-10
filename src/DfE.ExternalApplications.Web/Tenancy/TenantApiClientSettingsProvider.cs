using GovUK.Dfe.ExternalApplications.Api.Client.Settings;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Tenancy;

/// <summary>
/// Resolves API client settings from the current tenant configuration loaded by platform bootstrap.
/// </summary>
public sealed class TenantApiClientSettingsProvider(
    ITenantRequestContext tenantRequestContext,
    IConfiguration hostConfiguration) : IApiClientSettingsProvider
{
    /// <inheritdoc />
    public ApiClientSettings GetSettings()
    {
        var tenantConfiguration = tenantRequestContext.TenantConfiguration
            ?? throw new InvalidOperationException(
                "Tenant configuration is not available. Ensure platform tenant middleware ran for this request.");

        var settings = new ApiClientSettings();
        tenantConfiguration.GetSection("ExternalApplicationsApiClient").Bind(settings);

        if (tenantRequestContext.TenantId.HasValue)
        {
            settings.TenantId = tenantRequestContext.TenantId;
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            hostConfiguration.GetSection("ExternalApplicationsApiClient:BaseUrl").Bind(settings);
        }

        return settings;
    }
}
