using GovUK.Dfe.CoreLibs.Security.Configurations;
using GovUK.Dfe.CoreLibs.Security.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Chooses the appropriate authentication strategy per-request.
/// - TestAuth when globally enabled or the request is a valid Cypress request (and toggle allowed)
/// - OIDC otherwise
/// </summary>
public class CompositeAuthenticationSchemeStrategy(
    ILogger<CompositeAuthenticationSchemeStrategy> logger,
    IHttpContextAccessor httpContextAccessor,
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IConfiguration configuration,
    OidcAuthenticationStrategy oidcStrategy,
    TestAuthenticationStrategy testStrategy,
    InternalAuthenticationStrategy internalStrategy,
    [FromKeyedServices("cypress")] ICustomRequestChecker cypressRequestChecker,
    [FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker
    ) : IAuthenticationSchemeStrategy
{
    private bool IsTestEnabled() => testAuthOptions.Value.Enabled;
    private bool AllowToggle() => configuration.GetValue<bool>("CypressAuthentication:AllowToggle");

    private bool IsCypressRequest()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null || !AllowToggle()) return false;
        // Request checker may be null in some DI graphs; treat as not Cypress in that case
        return cypressRequestChecker != null && cypressRequestChecker.IsValidRequest(ctx);
    }

    private bool IsInternalAuthRequest()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) return false;
        // Request checker may be null in some DI graphs; treat as false
        return internalRequestChecker != null && internalRequestChecker.IsValidRequest(ctx);
    }

    private IAuthenticationSchemeStrategy Select()
    {
        var ctx = httpContextAccessor.HttpContext;
        var path = ctx?.Request.Path.ToString() ?? "unknown";
        var isTestEnabled = IsTestEnabled();
        var isCypress = IsCypressRequest();
        var isInternalAuth = IsInternalAuthRequest();

        if (isInternalAuth)
        {
            logger.LogDebug(
                "Selecting InternalAuthenticationStrategy for {Path}.",
                path);
            return internalStrategy;
        }

        if (isTestEnabled || isCypress)
        {
            logger.LogDebug(
                "Selecting TestAuthenticationStrategy for {Path}. TestEnabled: {TestEnabled}, IsCypress: {IsCypress}",
                path, isTestEnabled, isCypress);
            return testStrategy;
        }
        
        logger.LogDebug("Selecting OidcAuthenticationStrategy for {Path}", path);
        return oidcStrategy;
    }

    public string SchemeName => Select().SchemeName;

    public Task<TokenInfo> GetExternalIdpTokenAsync(HttpContext context)
        => Select().GetExternalIdpTokenAsync(context);

    public Task<bool> CanRefreshTokenAsync(HttpContext context)
        => Select().CanRefreshTokenAsync(context);

    public Task<bool> RefreshExternalIdpTokenAsync(HttpContext context)
        => Select().RefreshExternalIdpTokenAsync(context);

    public string? GetUserId(HttpContext context)
        => Select().GetUserId(context);
}


