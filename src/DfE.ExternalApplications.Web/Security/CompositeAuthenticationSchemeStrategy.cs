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
/// Priority: Internal Auth > Test Auth > Entra SSO (when enabled) > DfE Sign-In OIDC
/// </summary>
public class CompositeAuthenticationSchemeStrategy(
    ILogger<CompositeAuthenticationSchemeStrategy> logger,
    IHttpContextAccessor httpContextAccessor,
    IOptions<TestAuthenticationOptions> testAuthOptions,
    IOptions<EntraSsoOptions> entraSsoOptions,
    IConfiguration configuration,
    OidcAuthenticationStrategy oidcStrategy,
    TestAuthenticationStrategy testStrategy,
    InternalAuthenticationStrategy internalStrategy,
    EntraSsoAuthenticationStrategy entraSsoStrategy,
    [FromKeyedServices("internal")] ICustomRequestChecker internalRequestChecker
    ) : IAuthenticationSchemeStrategy
{
    private bool IsTestEnabled() => testAuthOptions.Value.Enabled;

    private bool IsEntraSsoEnabled() => entraSsoOptions.Value.Enabled;

    private bool IsInternalAuthRequest()
    {
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) return false;
        return internalRequestChecker != null && internalRequestChecker.IsValidRequest(ctx);
    }

    private IAuthenticationSchemeStrategy Select()
    {
        var ctx = httpContextAccessor.HttpContext;
        var path = ctx?.Request.Path.ToString() ?? "unknown";

        if (IsInternalAuthRequest())
        {
            logger.LogDebug("Selecting InternalAuthenticationStrategy for {Path}.", path);
            return internalStrategy;
        }

        if (IsTestEnabled())
        {
            logger.LogDebug("Selecting TestAuthenticationStrategy for {Path}.", path);
            return testStrategy;
        }

        if (IsEntraSsoEnabled())
        {
            logger.LogDebug("Selecting EntraSsoAuthenticationStrategy for {Path}.", path);
            return entraSsoStrategy;
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
