using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Builds DfE Sign-In OIDC URLs using the public origin from tenant or host configuration.
/// </summary>
internal static class DfESignInOidcPublicUrls
{
    /// <summary>
    /// Sets post-logout redirect URI using tenant DfE Sign-In settings when available.
    /// </summary>
    public static void ApplyPostLogoutRedirectUri(RedirectContext context, IConfiguration hostConfiguration)
    {
        var section = TenantAwareOpenIdConnectConfigurator.GetTenantSignInSection(context.HttpContext)
            ?? hostConfiguration.GetSection("DfESignIn");

        var signInRedirect = section["RedirectUri"];
        if (string.IsNullOrWhiteSpace(signInRedirect)
            || !Uri.TryCreate(signInRedirect, UriKind.Absolute, out var signInUri))
        {
            return;
        }

        var signedOutPath = section["SignedOutCallbackPath"];
        if (string.IsNullOrWhiteSpace(signedOutPath))
        {
            signedOutPath = "/signout-callback-oidc";
        }

        if (!signedOutPath.StartsWith('/'))
        {
            signedOutPath = "/" + signedOutPath;
        }

        var port = signInUri.IsDefaultPort ? -1 : signInUri.Port;
        var builder = new UriBuilder(signInUri.Scheme, signInUri.Host, port, signedOutPath);
        context.ProtocolMessage.PostLogoutRedirectUri = builder.Uri.AbsoluteUri;
    }
}
