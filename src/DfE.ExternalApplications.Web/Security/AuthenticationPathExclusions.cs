namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Paths that must not trigger permission loading, status-code rewrites,
/// or other authenticated middleware side effects.
/// </summary>
internal static class AuthenticationPathExclusions
{
    private static readonly string[] Paths =
    [
        "/signin-oidc",
        "/signout-callback-oidc",
        "/signin-entra",
        "/signout-callback-entra",
        "/Logout",
        "/health",
        "/assets",
        "/css",
        "/js",
        "/lib",
        "/favicon",
        "/govuk-frontend",
        "/_framework",
        "/_content"
    ];

    private static readonly string[] StaticFileExtensions =
    [
        ".js",
        ".css",
        ".map",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".svg",
        ".ico",
        ".woff",
        ".woff2",
        ".ttf",
        ".eot",
        ".json"
    ];

    /// <summary>
    /// Returns true when the request path is an authentication callback, logout,
    /// health check, or static asset that should skip permission middleware.
    /// </summary>
    public static bool ShouldSkip(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var pathValue = path.Value!;

        foreach (var excluded in Paths)
        {
            if (pathValue.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var extension in StaticFileExtensions)
        {
            if (pathValue.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
