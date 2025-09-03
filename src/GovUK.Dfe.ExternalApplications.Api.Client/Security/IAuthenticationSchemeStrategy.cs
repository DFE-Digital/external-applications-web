using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Strategy pattern for handling different authentication schemes (OIDC, TestAuth, etc.)
/// Open/Closed principle: Easy to add new authentication schemes
/// </summary>
public interface IAuthenticationSchemeStrategy
{
    /// <summary>
    /// The name of the authentication scheme this strategy handles
    /// </summary>
    string SchemeName { get; }

    /// <summary>
    /// Gets the External IDP token for this authentication scheme
    /// </summary>
    Task<TokenInfo> GetExternalIdpTokenAsync(HttpContext context);

    /// <summary>
    /// Determines if this scheme supports token refresh
    /// </summary>
    Task<bool> CanRefreshTokenAsync(HttpContext context);

    /// <summary>
    /// Attempts to refresh the External IDP token if supported
    /// </summary>
    Task<bool> RefreshExternalIdpTokenAsync(HttpContext context);

    /// <summary>
    /// Gets the user identifier for this authentication scheme
    /// </summary>
    string? GetUserId(HttpContext context);
}
