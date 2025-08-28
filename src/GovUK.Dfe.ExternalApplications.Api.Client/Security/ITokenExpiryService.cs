using System;
using System.Threading.Tasks;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Backward-compatible interface for existing consumers
/// Internally delegates to the new TokenStateManager architecture
/// </summary>
public interface ITokenExpiryService
{
    /// <summary>
    /// Checks if any user token (External IDP or OBO) is expired or will expire within the safety buffer
    /// </summary>
    /// <returns>True if any token is effectively expired (within buffer time)</returns>
    bool IsAnyTokenExpired();

    /// <summary>
    /// Gets the earliest expiry time among all user tokens
    /// </summary>
    /// <returns>The earliest expiry time, or null if no tokens are available</returns>
    DateTime? GetEarliestTokenExpiry();

    /// <summary>
    /// Gets detailed expiry information for all tokens
    /// </summary>
    /// <returns>Token expiry details</returns>
    TokenExpiryInfo GetTokenExpiryInfo();

    /// <summary>
    /// Gets detailed expiry information for all tokens (async version)
    /// </summary>
    /// <returns>Token expiry details</returns>
    Task<TokenExpiryInfo> GetTokenExpiryInfoAsync();

    /// <summary>
    /// Forces immediate logout by clearing all tokens and authentication context
    /// </summary>
    void ForceLogout();

    /// <summary>
    /// Forces immediate logout by clearing all tokens and authentication context (async version)
    /// </summary>
    Task ForceLogoutAsync();

    /// <summary>
    /// Checks if logout has been required due to token expiry
    /// </summary>
    /// <returns>True if the application should perform logout/redirect</returns>
    bool IsLogoutRequired();
}

/// <summary>
/// Contains detailed information about token expiry status
/// </summary>
public class TokenExpiryInfo
{
    public DateTime? ExternalIdpTokenExpiry { get; set; }
    public DateTime? OboTokenExpiry { get; set; }
    public DateTime? EarliestExpiry { get; set; }
    public TimeSpan? TimeUntilEarliestExpiry { get; set; }
    public bool IsAnyTokenExpired { get; set; }
    public bool IsExternalIdpTokenExpired { get; set; }
    public bool IsOboTokenExpired { get; set; }
    public bool IsOboTokenMissing { get; set; }
    public bool CanProceedWithExchange { get; set; }
    public string? ExpiryReason { get; set; }
}
