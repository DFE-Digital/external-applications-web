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
/// Selects the active authentication scheme per request with forwarder pattern.
/// Priority order:
/// 1. If X-Service-Email header present: Uses Internal Service Auth (header-based forwarder)
/// 2. If TestAuthentication.Enabled is true: Uses Test scheme for all
/// 3. If AllowToggle is true AND request is from Cypress: Uses Test scheme
/// 4. Otherwise: Uses OIDC (Cookies + OIDC challenge/sign-out)
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

        var checker = httpContext.RequestServices.GetKeyedService<ICustomRequestChecker>("cypress");
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

    private bool IsInternalServiceRequest()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return false;
        
        // Only engage Internal Service Auth if X-Service-Email header is present
        return httpContext.Request.Headers.ContainsKey("x-service-email");
    }

    public override Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        // PRIORITY 1: Internal Service Authentication (check header first - fastest)
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        // PRIORITY 2: Test Authentication (if enabled or Cypress)
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        // PRIORITY 3: Default to OIDC (Cookies)
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        // Internal Service requests don't challenge - they authenticate directly
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        // Test Auth uses its own challenge
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        // Regular users: OIDC challenge (login page)
        return GetSchemeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        // Don't call GetDefaultChallengeSchemeAsync here as it might trigger recursion
        // Instead, inline the logic with the cached result
        
        // Internal Service requests
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        // Test Auth
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        // Regular users: OIDC
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


