using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Tenancy;

/// <summary>
/// Exposes merged host + tenant configuration for the current HTTP request.
/// </summary>
public interface ITenantAppConfiguration
{
    /// <summary>
    /// Returns a configuration value preferring tenant settings over host settings.
    /// </summary>
    string? this[string key] { get; }

    /// <summary>
    /// Returns a configuration section preferring tenant settings over host settings.
    /// </summary>
    IConfigurationSection GetSection(string key);
}

/// <inheritdoc />
public sealed class TenantAppConfiguration(
    ITenantRequestContext tenantRequestContext,
    IConfiguration hostConfiguration) : ITenantAppConfiguration
{
    /// <inheritdoc />
    public string? this[string key] =>
        tenantRequestContext.GetTenantOrHostValue(hostConfiguration, key);

    /// <inheritdoc />
    public IConfigurationSection GetSection(string key) =>
        tenantRequestContext.GetTenantOrHostSection(hostConfiguration, key);
}
