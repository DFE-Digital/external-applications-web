using System;
using System.Threading.Tasks;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Single responsibility: Atomic cache operations for all token-related caches
/// Ensures consistency across all cache types
/// </summary>
public interface ICacheManager
{
    /// <summary>
    /// Atomically clears all token-related caches for a user
    /// </summary>
    Task ClearAllTokenCachesAsync(string userId);

    /// <summary>
    /// Sets a logout flag for a user with expiration
    /// </summary>
    Task SetLogoutFlagAsync(string userId, TimeSpan duration);

    /// <summary>
    /// Checks if logout flag is set for a user
    /// </summary>
    Task<bool> IsLogoutFlagSetAsync(string userId);

    /// <summary>
    /// Clears the logout flag for a user
    /// </summary>
    Task ClearLogoutFlagAsync(string userId);

    /// <summary>
    /// Sets a request-scoped flag to prevent duplicate operations within the same request
    /// </summary>
    void SetRequestScopedFlag(string key, object value);

    /// <summary>
    /// Gets a request-scoped flag value
    /// </summary>
    T? GetRequestScopedFlag<T>(string key);

    /// <summary>
    /// Checks if a request-scoped flag exists
    /// </summary>
    bool HasRequestScopedFlag(string key);

    /// <summary>
    /// Gets the last-activity timestamp for the specified user, if available
    /// </summary>
    Task<DateTime?> GetLastActivityAsync(string userId);

    /// <summary>
    /// Sets the last-activity timestamp for the specified user with an optional sliding TTL
    /// </summary>
    Task SetLastActivityAsync(string userId, DateTime timestamp, TimeSpan? ttl = null);
}
