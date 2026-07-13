using DfE.ExternalApplications.Web.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Configuration;

/// <summary>
/// Resolves <see cref="IOptions{TOptions}"/> from host configuration with a per-request tenant overlay.
/// Uses <see cref="IHttpContextAccessor"/> so values remain correct when resolved outside a request DI scope.
/// </summary>
/// <typeparam name="TOptions">The options type to bind.</typeparam>
public sealed class TenantOptionsAccessor<TOptions>(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration hostConfiguration,
    string sectionName) : IOptions<TOptions>
    where TOptions : class, new()
{
    /// <inheritdoc />
    public TOptions Value
    {
        get
        {
            var options = new TOptions();
            hostConfiguration.GetSection(sectionName).Bind(options);

            var tenantContext = httpContextAccessor.HttpContext?.RequestServices
                .GetService<ITenantRequestContext>();
            var tenantSection = tenantContext?.TenantConfiguration?.GetSection(sectionName);
            if (tenantSection?.Exists() == true)
            {
                tenantSection.Bind(options);
            }

            return options;
        }
    }
}
