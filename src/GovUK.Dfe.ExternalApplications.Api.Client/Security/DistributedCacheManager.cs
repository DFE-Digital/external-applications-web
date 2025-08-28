using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Implementation of cache manager using distributed cache and HTTP context
/// Ensures atomic cache operations and consistency
/// </summary>
public class DistributedCacheManager(
    IDistributedCache distributedCache,
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    ILogger<DistributedCacheManager> logger) : ICacheManager
{

    public async Task ClearAllTokenCachesAsync(string userId)
    {
        logger.LogInformation(">>>>>>>>>> CacheManager >>> Clearing all token caches for user: {UserId}", userId);
        
        try
        {
            // Clear OBO token from store
            tokenStore.ClearToken();
            logger.LogDebug(">>>>>>>>>> CacheManager >>> Cleared OBO token store");

            // Clear distributed cache entries
            var cacheKeys = new[]
            {
                $"obo_token_{userId}",
                $"token_expiry_{userId}",
                $"user_tokens_{userId}"
            };

            foreach (var key in cacheKeys)
            {
                await distributedCache.RemoveAsync(key);
                logger.LogDebug(">>>>>>>>>> CacheManager >>> Cleared cache key: {Key}", key);
            }

            // Clear request-scoped cache
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                var itemsToRemove = httpContext.Items.Keys
                    .Where(k => k.ToString()?.Contains("token", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    httpContext.Items.Remove(item);
                    logger.LogDebug(">>>>>>>>>> CacheManager >>> Cleared request item: {Item}", item);
                }
            }

            logger.LogInformation(">>>>>>>>>> CacheManager >>> Successfully cleared all token caches for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> CacheManager >>> Error clearing token caches for user: {UserId}", userId);
            throw;
        }
    }

    public async Task SetLogoutFlagAsync(string userId, TimeSpan duration)
    {
        logger.LogInformation(">>>>>>>>>> CacheManager >>> Setting logout flag for user: {UserId}, Duration: {Duration}", 
            userId, duration);
        
        try
        {
            var key = $"logout_forced_{userId}";
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = duration
            };

            await distributedCache.SetStringAsync(key, "true", options);
            
            // Also set in request scope for immediate effect
            SetRequestScopedFlag("RequireLogout", true);
            
            logger.LogInformation(">>>>>>>>>> CacheManager >>> Logout flag set for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> CacheManager >>> Error setting logout flag for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> IsLogoutFlagSetAsync(string userId)
    {
        try
        {
            // Check request scope first
            if (HasRequestScopedFlag("RequireLogout"))
            {
                logger.LogDebug(">>>>>>>>>> CacheManager >>> Logout flag found in request scope for user: {UserId}", userId);
                return true;
            }

            // Check distributed cache
            var key = $"logout_forced_{userId}";
            var value = await distributedCache.GetStringAsync(key);
            var isSet = !string.IsNullOrEmpty(value);
            
            logger.LogDebug(">>>>>>>>>> CacheManager >>> Logout flag in distributed cache for user {UserId}: {IsSet}", 
                userId, isSet);
            
            return isSet;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> CacheManager >>> Error checking logout flag for user: {UserId}", userId);
            return false;
        }
    }

    public async Task ClearLogoutFlagAsync(string userId)
    {
        logger.LogInformation(">>>>>>>>>> CacheManager >>> Clearing logout flag for user: {UserId}", userId);
        
        try
        {
            var key = $"logout_forced_{userId}";
            await distributedCache.RemoveAsync(key);
            
            // Clear request scope as well
            var httpContext = httpContextAccessor.HttpContext;
            httpContext?.Items.Remove("RequireLogout");
            
            logger.LogInformation(">>>>>>>>>> CacheManager >>> Logout flag cleared for user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> CacheManager >>> Error clearing logout flag for user: {UserId}", userId);
            throw;
        }
    }

    public void SetRequestScopedFlag(string key, object value)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[key] = value;
            logger.LogDebug(">>>>>>>>>> CacheManager >>> Set request-scoped flag: {Key} = {Value}", key, value);
        }
    }

    public T? GetRequestScopedFlag<T>(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(key, out var value) == true && value is T typedValue)
        {
            logger.LogDebug(">>>>>>>>>> CacheManager >>> Got request-scoped flag: {Key} = {Value}", key, typedValue);
            return typedValue;
        }
        
        return default;
    }

    public bool HasRequestScopedFlag(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var exists = httpContext?.Items.ContainsKey(key) == true;
        logger.LogDebug(">>>>>>>>>> CacheManager >>> Request-scoped flag {Key} exists: {Exists}", key, exists);
        return exists;
    }
}
