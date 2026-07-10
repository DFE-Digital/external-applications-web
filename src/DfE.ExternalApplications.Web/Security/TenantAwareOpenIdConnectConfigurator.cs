using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Applies DfE Sign-In settings from the current tenant configuration to OIDC options at runtime.
/// </summary>
public static class TenantAwareOpenIdConnectConfigurator
{
    /// <summary>
    /// Overlays tenant-specific DfE Sign-In settings onto the active OIDC options instance.
    /// </summary>
    public static void ApplyTenantSettings(HttpContext httpContext, OpenIdConnectOptions options)
    {
        var tenantContext = httpContext.RequestServices.GetService<Tenancy.ITenantRequestContext>();
        var section = tenantContext?.TenantConfiguration?.GetSection("DfESignIn");
        if (section?.Exists() != true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(section["Authority"]))
        {
            options.Authority = section["Authority"];
        }

        if (!string.IsNullOrWhiteSpace(section["ClientId"]))
        {
            options.ClientId = section["ClientId"];
        }

        if (!string.IsNullOrWhiteSpace(section["ClientSecret"]))
        {
            options.ClientSecret = section["ClientSecret"];
        }

        var scopes = section.GetSection("Scopes").Get<string[]>();
        if (scopes?.Length > 0)
        {
            options.Scope.Clear();
            foreach (var scope in scopes)
            {
                options.Scope.Add(scope);
            }
        }

        if (!string.IsNullOrWhiteSpace(section["RedirectUri"]))
        {
            options.CallbackPath = ExtractCallbackPath(section["RedirectUri"]);
        }
    }

    /// <summary>
    /// Returns the tenant DfE Sign-In configuration section when platform bootstrap is active.
    /// </summary>
    public static IConfigurationSection? GetTenantSignInSection(HttpContext httpContext)
    {
        var tenantContext = httpContext.RequestServices.GetService<Tenancy.ITenantRequestContext>();
        var section = tenantContext?.TenantConfiguration?.GetSection("DfESignIn");
        return section?.Exists() == true ? section : null;
    }

    private static PathString ExtractCallbackPath(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return new PathString("/signin-oidc");
        }

        return new PathString(uri.AbsolutePath);
    }
}
