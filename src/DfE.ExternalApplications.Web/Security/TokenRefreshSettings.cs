namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Configurable thresholds for token refresh and forced logout based on remaining time to expiry
/// </summary>
public sealed class TokenRefreshSettings
{
    /// <summary>
    /// When remaining minutes to expiry are less than or equal to this value, allow refresh
    /// Default: 10
    /// </summary>
    public int RefreshLeadTimeMinutes { get; set; } = 10;

    /// <summary>
    /// When remaining minutes to expiry are less than or equal to this value, downstream policy forces logout
    /// Default: 5
    /// </summary>
    public int ForceLogoutAtMinutesRemaining { get; set; } = 5;
}


