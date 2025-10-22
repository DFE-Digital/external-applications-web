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
        // Only allow Cypress toggle if BOTH conditions are met:
        // 1. Configuration setting is true
        // 2. Running in GitHub Actions (GITHUB_ACTIONS environment variable is set)
        var configAllowsToggle = configuration.GetValue<bool>("CypressAuthentication:AllowToggle");
        if (!configAllowsToggle)
        {
            return false;
        }

        var isGitHubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        return isGitHubActions;
    }

    private bool IsCypressRequest()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return false;
        if (!IsCypressToggleAllowed()) return false;
        
        var checker = httpContext.RequestServices.GetService<ICustomRequestChecker>();
        return checker != null && checker.IsValidRequest(httpContext);
    }

    private bool ShouldUseTestAuth()
    {
        // Always use test auth if globally enabled
        if (IsTestAuthGloballyEnabled())
        {
            return true;
        }

        // Check if this is a Cypress request (only in GitHub Actions)
        return IsCypressRequest();
    }

    public override Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        // Don't call GetDefaultChallengeSchemeAsync here as it might trigger recursion
        // Instead, inline the logic with the cached result
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
    {
        // Always use Cookies for sign-in
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
    {
        if (ShouldUseTestAuth())
        {
            // Test auth signs out cookies only
            return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        // OIDC sign-out triggers federated sign-out
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }
}


