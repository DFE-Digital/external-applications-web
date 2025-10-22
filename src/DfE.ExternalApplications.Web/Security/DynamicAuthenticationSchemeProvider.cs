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
    // Cache the authentication mode decision per request to prevent infinite recursion
    // If GetDefaultForbidSchemeAsync is called during a forbid flow, and it tries to check IsCypressRequest,
    // which might trigger another auth check, we get a stack overflow
    private const string AuthModeCacheKey = "__AuthMode_Cached__";

    private bool IsTestAuthGloballyEnabled()
    {
        return testAuthOptions.Value.Enabled;
    }

    private bool IsCypressToggleAllowed()
    {
        return configuration.GetValue<bool>("CypressAuthentication:AllowToggle");
    }

    /// <summary>
    /// Determines if the current request should use Test authentication.
    /// This is cached per request to prevent infinite recursion during authentication flows.
    /// </summary>
    private bool ShouldUseTestAuth()
    {
        // Always use test auth if globally enabled
        if (IsTestAuthGloballyEnabled())
        {
            return true;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return false;
        }

        // Check cache first to prevent re-entry during authentication flow
        if (httpContext.Items.TryGetValue(AuthModeCacheKey, out var cachedMode))
        {
            return (bool)cachedMode!;
        }

        // Determine if this is a Cypress request (only if toggle is allowed)
        bool isCypressRequest = false;
        if (IsCypressToggleAllowed())
        {
            try
            {
                var checker = httpContext.RequestServices.GetService<ICustomRequestChecker>();
                isCypressRequest = checker != null && checker.IsValidRequest(httpContext);
            }
            catch
            {
                // If we can't check (e.g., during service resolution or auth flow), default to false
                // This prevents cascading failures during authentication
                isCypressRequest = false;
            }
        }

        // Cache the result for this request
        httpContext.Items[AuthModeCacheKey] = isCypressRequest;
        return isCypressRequest;
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


