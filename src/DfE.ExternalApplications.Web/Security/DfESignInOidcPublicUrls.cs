using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Builds DfE Sign-In OIDC URLs using the public origin from <c>DfESignIn:RedirectUri</c> so
/// <c>post_logout_redirect_uri</c> matches DSI registration when the app runs behind a reverse proxy
/// (internal container host/scheme differ from the public URL).
/// </summary>
internal static class DfESignInOidcPublicUrls
{
    /// <summary>
    /// Sets <see cref="Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectMessage.PostLogoutRedirectUri"/>
    /// to an absolute URL using the same scheme/host as sign-in <c>RedirectUri</c>, plus the configured
    /// <c>SignedOutCallbackPath</c> (default <c>/signout-callback-oidc</c>).
    /// </summary>
    public static void ApplyPostLogoutRedirectUri(RedirectContext context, IConfiguration configuration)
    {
        var signInRedirect = configuration["DfESignIn:RedirectUri"];
        if (string.IsNullOrWhiteSpace(signInRedirect)
            || !Uri.TryCreate(signInRedirect, UriKind.Absolute, out var signInUri))
        {
            return;
        }

        var signedOutPath = configuration["DfESignIn:SignedOutCallbackPath"];
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
