using DfE.ExternalApplications.Web.Authentication;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Selects the active authentication scheme per request.
/// - If TestAuthentication.Enabled is true, uses Test scheme for all.
/// - Else if AllowToggle is true AND request is from Cypress, uses Test scheme.
/// - Otherwise uses OIDC (Cookies + OIDC challenge/sign-out).
/// </summary>
public class DynamicAuthenticationSchemeProvider(
    IOptions<AuthenticationOptions> options,
    IHttpContextAccessor httpContextAccessor,
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IConfiguration configuration)
    : AuthenticationSchemeProvider(options)
{
    private bool IsTestAuthGloballyEnabled()
    {
        return testAuthOptions.Value.Enabled;
    }

    private bool IsCypressToggleAllowed()
    {
        return configuration.GetValue<bool>("CypressAuthentication:AllowToggle");
    }

    private bool IsCypressRequest()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return false;
        if (!IsCypressToggleAllowed()) return false;
        var checker = httpContext.RequestServices.GetService<ICustomRequestChecker>();
        return checker != null && checker.IsValidRequest(httpContext);
    }

    public override Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        if (IsTestAuthGloballyEnabled() || IsCypressRequest())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        if (IsTestAuthGloballyEnabled() || IsCypressRequest())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        // Match challenge behaviour
        return GetDefaultChallengeSchemeAsync();
    }

    public override Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
    {
        // Always use Cookies for sign-in
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
    {
        if (IsTestAuthGloballyEnabled() || IsCypressRequest())
        {
            // Test auth signs out cookies only
            return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        // OIDC sign-out triggers federated sign-out
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }
}


