using DfE.ExternalApplications.Web.Authentication;
using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.EntraSso;
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
/// 3. If EntraSso.Enabled is true: Uses Entra SSO scheme
/// 4. Otherwise: Uses DfE Sign-In OIDC (Cookies + OIDC challenge/sign-out)
/// </summary>
public class DynamicAuthenticationSchemeProvider(
    IOptions<AuthenticationOptions> options,
    IHttpContextAccessor httpContextAccessor,
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IOptions<EntraSsoOptions> entraSsoOptions,
    IConfiguration configuration)
    : AuthenticationSchemeProvider(options)
{
    private bool IsTestAuthGloballyEnabled()
    {
        return testAuthOptions.Value.Enabled;
    }

    private bool ShouldUseTestAuth()
    {
        if (IsTestAuthGloballyEnabled())
        {
            return true;
        }

        return false;
    }

    private bool IsEntraSsoEnabled()
    {
        return entraSsoOptions.Value.Enabled;
    }

    private bool IsInternalServiceRequest()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return false;
        
        return httpContext.Request.Headers.ContainsKey("x-service-email");
    }

    private string GetDefaultIdpScheme()
    {
        return IsEntraSsoEnabled()
            ? EntraSsoDefaults.AuthenticationScheme
            : OpenIdConnectDefaults.AuthenticationScheme;
    }

    public override Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        return GetSchemeAsync(GetDefaultIdpScheme());
    }

    public override Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        if (IsInternalServiceRequest())
        {
            return GetSchemeAsync(InternalServiceAuthenticationHandler.SchemeName);
        }
        
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(TestAuthenticationHandler.SchemeName);
        }
        
        return GetSchemeAsync(GetDefaultIdpScheme());
    }

    public override Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
    {
        return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public override Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
    {
        if (ShouldUseTestAuth())
        {
            return GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return GetSchemeAsync(GetDefaultIdpScheme());
    }
}
