using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

public class CachedInternalUserTokenStore(
    IHttpContextAccessor httpContextAccessor,
    IDistributedCache distributedCache,
    ILogger<CachedInternalUserTokenStore> logger)
    : IInternalUserTokenStore
{
    private const string TokenKey = "__InternalUserToken";
    private const string CacheKeyPrefix = "InternalToken:";
    /// <summary>
    /// Unified expiry buffer - consistent with TokenExpiryService
    /// </summary>
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(5);

    public string? GetToken()
    {
        logger.LogDebug(">>>>>>>>>> Authentication >>> GetToken called");
        
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> HttpContext is null in GetToken");
            return null;
        }

        logger.LogDebug(">>>>>>>>>> Authentication >>> Checking HttpContext.Items for cached token");
        
        // First check HttpContext.Items for this request
        if (ctx.Items.TryGetValue(TokenKey, out var tokenObj) && tokenObj is string requestToken)
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Found token in HttpContext.Items, length: {TokenLength} chars", 
                requestToken.Length);
            
            if (IsTokenValid(requestToken))
            {
                logger.LogDebug(">>>>>>>>>> Authentication >>> HttpContext.Items token is valid, returning it");
                return requestToken;
            }
            
            logger.LogDebug(">>>>>>>>>> Authentication >>> HttpContext.Items token is invalid, removing it");
            ctx.Items.Remove(TokenKey);
        }
        else
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> No token found in HttpContext.Items");
        }

        // Then check distributed cache
        logger.LogDebug(">>>>>>>>>> Authentication >>> Checking distributed cache for token");
        
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> User ID identified as: {UserId}", userId);
            
            var cacheKey = $"{CacheKeyPrefix}{userId}";
            logger.LogDebug(">>>>>>>>>> Authentication >>> Looking up token in distributed cache with key: {CacheKey}", cacheKey);
            
            var cachedTokenJson = distributedCache.GetString(cacheKey);
            
            if (!string.IsNullOrEmpty(cachedTokenJson))
            {
                logger.LogDebug(">>>>>>>>>> Authentication >>> Found cached token JSON in distributed cache, length: {JsonLength} chars", 
                    cachedTokenJson.Length);
                
                try
                {
                    var cachedToken = JsonSerializer.Deserialize<CachedTokenData>(cachedTokenJson);
                    if (cachedToken != null)
                    {
                        logger.LogDebug(">>>>>>>>>> Authentication >>> Successfully deserialized cached token, expires at: {ExpiresAt}", 
                            cachedToken.ExpiresAt);
                        
                        if (IsTokenValid(cachedToken.Token))
                        {
                            logger.LogDebug(">>>>>>>>>> Authentication >>> Cached token is valid, storing in HttpContext.Items and returning it");
                            // Store in HttpContext.Items for subsequent requests in this request
                            ctx.Items[TokenKey] = cachedToken.Token;
                            return cachedToken.Token;
                        }
                        else
                        {
                            logger.LogDebug(">>>>>>>>>> Authentication >>> Cached token is invalid/expired, removing from cache");
                            // Remove expired token from cache
                            distributedCache.Remove(cacheKey);
                        }
                    }
                    else
                    {
                        logger.LogWarning(">>>>>>>>>> Authentication >>> Deserialization returned null for cached token, removing from cache");
                        distributedCache.Remove(cacheKey);
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, ">>>>>>>>>> Authentication >>> Failed to deserialize cached token for user {UserId}, removing from cache", userId);
                    distributedCache.Remove(cacheKey);
                }
            }
            else
            {
                logger.LogDebug(">>>>>>>>>> Authentication >>> No cached token found in distributed cache for user: {UserId}", userId);
            }
        }
        else
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Could not identify user ID from context - User.Identity.IsAuthenticated: {IsAuthenticated}", 
                ctx.User?.Identity?.IsAuthenticated);
        }

        logger.LogDebug(">>>>>>>>>> Authentication >>> No valid token found in any cache location, returning null");
        return null;
    }

    public void SetToken(string token)
    {
        logger.LogDebug(">>>>>>>>>> Authentication >>> SetToken called with token length: {TokenLength} chars", token?.Length ?? 0);
        
        if (string.IsNullOrEmpty(token))
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> SetToken called with null or empty token");
            return;
        }
        
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> HttpContext is null in SetToken");
            return;
        }

        logger.LogDebug(">>>>>>>>>> Authentication >>> Storing token in HttpContext.Items");
        ctx.Items[TokenKey] = token;

        // Store in distributed cache for future requests
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> User ID identified for caching: {UserId}", userId);
            
            var cacheKey = $"{CacheKeyPrefix}{userId}";
            var tokenExpiry = GetTokenExpiry(token);
            var tokenData = new CachedTokenData(token, tokenExpiry);
            
            logger.LogDebug(">>>>>>>>>> Authentication >>> Token expires at: {ExpiryTime}, caching until: {CacheUntil}", 
                tokenExpiry, tokenExpiry.Subtract(ExpiryBuffer));
            
            var tokenJson = JsonSerializer.Serialize(tokenData);
            
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = tokenData.ExpiresAt.Subtract(ExpiryBuffer)
            };
            
            try
            {
                distributedCache.SetString(cacheKey, tokenJson, cacheOptions);
                logger.LogInformation(">>>>>>>>>> Authentication >>> Internal token cached for user {UserId} until {ExpiryTime}", 
                    userId, cacheOptions.AbsoluteExpiration);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to cache token for user {UserId}", userId);
            }
        }
        else
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> Could not identify user ID for token caching");
        }
    }

    public void ClearToken()
    {
        logger.LogDebug(">>>>>>>>>> Authentication >>> ClearToken called");
        
        var ctx = httpContextAccessor.HttpContext;
        if (ctx == null) 
        {
            logger.LogWarning(">>>>>>>>>> Authentication >>> HttpContext is null in ClearToken");
            return;
        }

        // Clear from HttpContext.Items
        logger.LogDebug(">>>>>>>>>> Authentication >>> Clearing token from HttpContext.Items");
        var wasInItems = ctx.Items.ContainsKey(TokenKey);
        ctx.Items.Remove(TokenKey);
        
        if (wasInItems)
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Token removed from HttpContext.Items");
        }
        else
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> No token found in HttpContext.Items to remove");
        }

        // Clear from distributed cache
        var userId = GetUserIdFromContext(ctx);
        if (userId != null)
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Clearing token from distributed cache for user: {UserId}", userId);
            
            var cacheKey = $"{CacheKeyPrefix}{userId}";
            
            try
            {
                distributedCache.Remove(cacheKey);
                logger.LogInformation(">>>>>>>>>> Authentication >>> Cleared internal token cache for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to clear token cache for user {UserId}", userId);
            }
        }
        else
        {
            logger.LogDebug(">>>>>>>>>> Authentication >>> Could not identify user ID for cache clearing");
        }
    }

    public bool IsTokenValid()
    {
        var token = GetToken();
        return !string.IsNullOrEmpty(token) && IsTokenValid(token);
    }

    public DateTime? GetTokenExpiry()
    {
        var token = GetToken();
        return string.IsNullOrEmpty(token) ? null : GetTokenExpiry(token);
    }

    private static bool IsTokenValid(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            // Use unified 5-minute buffer instead of 1-minute
            var isValid = jwt.ValidTo > DateTime.UtcNow.Add(ExpiryBuffer);
            return isValid;
        }
        catch
        {
            return false;
        }
    }

    private static DateTime GetTokenExpiry(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            return jwt.ValidTo;
        }
        catch
        {
            return DateTime.UtcNow.AddHours(1); // Default fallback
        }
    }

    private static string? GetUserIdFromContext(HttpContext context)
    {
        // Try to get user identifier from claims
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            // Try different claim types that might identify the user
            var userId = user.FindFirst("appid")?.Value ??
                        user.FindFirst("azp")?.Value ??
                        user.FindFirst(ClaimTypes.Email)?.Value ??
                        user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            return userId;
        }
        return null;
    }

    private record CachedTokenData(string Token, DateTime ExpiresAt);
} 