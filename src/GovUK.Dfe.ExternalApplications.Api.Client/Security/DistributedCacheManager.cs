using System;
using System.Diagnostics.CodeAnalysis;
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
[ExcludeFromCodeCoverage]
public class DistributedCacheManager(
    IDistributedCache distributedCache,
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    ILogger<DistributedCacheManager> logger) : ICacheManager
{

    public async Task ClearAllTokenCachesAsync(string userId)
    {
        try
        {
            // Clear OBO token from store
            tokenStore.ClearToken();

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
                }
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public async Task SetLogoutFlagAsync(string userId, TimeSpan duration)
    {
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
        }
        catch (Exception ex)
        {
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
                return true;
            }

            // Check distributed cache
            var key = $"logout_forced_{userId}";
            var value = await distributedCache.GetStringAsync(key);
            var isSet = !string.IsNullOrEmpty(value);
            
            return isSet;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task ClearLogoutFlagAsync(string userId)
    {
        try
        {
            var key = $"logout_forced_{userId}";
            await distributedCache.RemoveAsync(key);
            
            // Clear request scope as well
            var httpContext = httpContextAccessor.HttpContext;
            httpContext?.Items.Remove("RequireLogout");
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    public void SetRequestScopedFlag(string key, object value)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[key] = value;
        }
    }

    public T? GetRequestScopedFlag<T>(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(key, out var value) == true && value is T typedValue)
        {
            return typedValue;
        }
        
        return default;
    }

    public bool HasRequestScopedFlag(string key)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var exists = httpContext?.Items.ContainsKey(key) == true;
        return exists;
    }
}
