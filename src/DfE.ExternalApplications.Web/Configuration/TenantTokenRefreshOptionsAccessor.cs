using DfE.ExternalApplications.Web.Tenancy;
using GovUK.Dfe.CoreLibs.Security.TokenRefresh.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Configuration;

/// <summary>
/// Resolves token refresh options from tenant configuration for the current request.
/// </summary>
public sealed class TenantTokenRefreshOptionsAccessor(
    ITenantRequestContext tenantRequestContext,
    IConfiguration hostConfiguration) : IOptions<TokenRefreshOptions>
{
    /// <inheritdoc />
    public TokenRefreshOptions Value => Build();

    private TokenRefreshOptions Build()
    {
        var config = tenantRequestContext.TenantConfiguration ?? hostConfiguration;
        var options = new TokenRefreshOptions();
        config.GetSection("TokenRefresh").Bind(options);

        var oidcSection = config.GetSection("DfESignIn");
        if (oidcSection.Exists())
        {
            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                options.ClientId = oidcSection["ClientId"];
            }

            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                options.ClientSecret = oidcSection["ClientSecret"];
            }

            var authority = oidcSection["Authority"];
            if (!string.IsNullOrWhiteSpace(authority))
            {
                var authorityUri = authority.TrimEnd('/');
                options.TokenEndpoint ??= $"{authorityUri}/token";
                options.IntrospectionEndpoint ??= $"{authorityUri}/token/introspection";
            }
        }

        options.Validate();
        return options;
    }
}
