using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Central manager for all token states and transitions
/// Single responsibility: Orchestrate token state management across all authentication schemes
/// </summary>
public class TokenStateManager(
    IHttpContextAccessor httpContextAccessor,
    IInternalUserTokenStore tokenStore,
    ICacheManager cacheManager,
    IEnumerable<IAuthenticationSchemeStrategy> authStrategies,
    ILogger<TokenStateManager> logger) : ITokenStateManager
{
    public async Task<TokenState> GetCurrentTokenStateAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> HttpContext is null");
            return new TokenState { IsAuthenticated = false };
        }

        // For OIDC, we need to use the authentication scheme, not the AuthenticationType
        var authScheme = GetActualAuthenticationScheme(httpContext);
        
        var state = new TokenState
        {
            CheckTime = DateTime.UtcNow,
            IsAuthenticated = httpContext.User?.Identity?.IsAuthenticated == true,
            AuthenticationScheme = authScheme
        };

        if (!state.IsAuthenticated)
        {
            logger.LogDebug(">>>>>>>>>> TokenState >>> User is not authenticated");
            return state;
        }

        logger.LogInformation(">>>>>>>>>> TokenState >>> Checking token state for authenticated user, Scheme: {Scheme}", 
            state.AuthenticationScheme);

        // Get the appropriate authentication strategy
        var strategy = GetAuthenticationStrategy(state.AuthenticationScheme);
        if (strategy == null)
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> No authentication strategy found for scheme: {Scheme}", 
                state.AuthenticationScheme);
            state.LogoutReason = $"No authentication strategy for scheme: {state.AuthenticationScheme}";
            return state;
        }

        // Get user ID
        state.UserId = strategy.GetUserId(httpContext);
        if (string.IsNullOrEmpty(state.UserId))
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> No user ID found");
            state.LogoutReason = "No user ID found";
            return state;
        }

        // Check if logout is already flagged
        if (await cacheManager.IsLogoutFlagSetAsync(state.UserId))
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Logout flag is set for user: {UserId} - checking if this is fresh authentication", state.UserId);
            
            // Check if this is a fresh authentication by looking at External IDP token issue time
            var externalIdpToken = await strategy.GetExternalIdpTokenAsync(httpContext);
            if (externalIdpToken.IsPresent && !string.IsNullOrEmpty(externalIdpToken.Value))
            {
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jsonToken = handler.ReadJwtToken(externalIdpToken.Value);
                    
                    // Check if token was issued recently (within last 2 minutes)
                    // Handle case where IssuedAt might be default value
                    var issuedAt = jsonToken.IssuedAt;
                    var timeSinceIssue = DateTime.UtcNow - issuedAt;
                    
                    // If IssuedAt is default value (0001-01-01), check ValidFrom instead
                    if (issuedAt == DateTime.MinValue)
                    {
                        issuedAt = jsonToken.ValidFrom;
                        timeSinceIssue = DateTime.UtcNow - issuedAt;
                        logger.LogInformation(">>>>>>>>>> TokenState >>> Using ValidFrom instead of IssuedAt: {ValidFrom}", 
                            issuedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                    }
                    
                    var isRecentAuthentication = timeSinceIssue <= TimeSpan.FromMinutes(2);
                    
                    logger.LogInformation(">>>>>>>>>> TokenState >>> External IDP token issued at: {IssuedAt}, {TimeSince} ago, IsRecent: {IsRecent}", 
                        issuedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"), 
                        timeSinceIssue.ToString(@"mm\:ss"), 
                        isRecentAuthentication);
                    
                    if (isRecentAuthentication)
                    {
                        logger.LogWarning(">>>>>>>>>> TokenState >>> FRESH AUTHENTICATION DETECTED - clearing logout flag for user: {UserId}", state.UserId);
                        
                        // Clear the logout flag since this is fresh authentication
                        await cacheManager.ClearLogoutFlagAsync(state.UserId);
                        
                        // Clear OBO token cache to force fresh exchange
                        tokenStore.ClearToken();
                        
                        // Set re-authentication detection flag for this request
                        cacheManager.SetRequestScopedFlag("ReAuthenticationDetected", true);
                        
                        logger.LogInformation(">>>>>>>>>> TokenState >>> Logout flag cleared - proceeding with fresh token state");
                        
                        // Continue with normal token processing
                    }
                    else
                    {
                        logger.LogInformation(">>>>>>>>>> TokenState >>> Authentication is not recent - checking if this is a re-authentication scenario");
                        
                        // ALTERNATIVE: If user is authenticated but logout flag is set, this might be a re-authentication
                        // Clear the flag and try once more (to handle cases where token timestamps are unreliable)
                        logger.LogWarning(">>>>>>>>>> TokenState >>> User is authenticated despite logout flag - clearing flag to allow re-authentication attempt");
                        
                        await cacheManager.ClearLogoutFlagAsync(state.UserId);
                        tokenStore.ClearToken();
                        
                        // Set flag to prevent infinite loops
                        cacheManager.SetRequestScopedFlag("ReAuthenticationDetected", true);
                        
                        logger.LogInformation(">>>>>>>>>> TokenState >>> Logout flag cleared for re-authentication - proceeding with fresh token state");
                        
                        // Continue with normal token processing
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, ">>>>>>>>>> TokenState >>> Error checking External IDP token issue time - logout flag remains active");
                    state.LogoutReason = "Logout flag set (token check error)";
                    return state;
                }
            }
            else
            {
                logger.LogInformation(">>>>>>>>>> TokenState >>> No External IDP token found - logout flag remains active");
                state.LogoutReason = "Logout flag set (no External IDP token)";
                return state;
            }
        }

        // Check for re-authentication detection (prevents multiple calls in same request)
        bool isReAuthentication = cacheManager.HasRequestScopedFlag("ReAuthenticationDetected");
        if (isReAuthentication)
        {
            logger.LogInformation(">>>>>>>>>> TokenState >>> Re-authentication detected (cached) - allowing fresh exchange");
        }

        // Get External IDP token
        state.ExternalIdpToken = await strategy.GetExternalIdpTokenAsync(httpContext);
        
        // Get OBO token
        state.OboToken = GetOboToken();

        // Check refresh capability
        if (isReAuthentication)
        {
            // During re-authentication, allow fresh token exchange even if strategy says no refresh
            state.CanRefresh = true;
            logger.LogInformation(">>>>>>>>>> TokenState >>> Re-authentication: Overriding CanRefresh to allow fresh exchange");
        }
        else
        {
            state.CanRefresh = await strategy.CanRefreshTokenAsync(httpContext);
        }

        // Determine logout reason if needed
        if (state.IsAnyTokenExpired)
        {
            if (state.CanRefresh)
            {
                state.LogoutReason = "Tokens expired but refresh possible";
            }
            else
            {
                state.LogoutReason = state.ExternalIdpToken.IsExpired ? 
                    "External IDP token expired and cannot refresh" : 
                    "OBO token expired and External IDP cannot refresh";
            }
        }

        logger.LogInformation(">>>>>>>>>> TokenState >>> Token State Summary - " +
            "ExternalIDP: Valid={ExternalValid}, Expires={ExternalExpiry}, " +
            "OBO: Valid={OboValid}, Expires={OboExpiry}, " +
            "CanRefresh={CanRefresh}, ShouldLogout={ShouldLogout}, " +
            "Reason={Reason}",
            state.ExternalIdpToken.IsValid, state.ExternalIdpToken.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            state.OboToken.IsValid, state.OboToken.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            state.CanRefresh, state.ShouldLogout, state.LogoutReason);

        return state;
    }

    public async Task<bool> ForceCompleteLogoutAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Cannot force logout - HttpContext is null");
            return false;
        }

        // Use mapped authentication scheme for consistency
        var mappedScheme = GetActualAuthenticationScheme(httpContext);
        var strategy = GetAuthenticationStrategy(mappedScheme);
        var userId = strategy?.GetUserId(httpContext);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Cannot force logout - No user ID found");
            return false;
        }

        logger.LogWarning(">>>>>>>>>> TokenState >>> FORCING COMPLETE LOGOUT for user: {UserId}", userId);

        try
        {
            // Set logout flag first
            await cacheManager.SetLogoutFlagAsync(userId, TimeSpan.FromMinutes(10));

            // Clear all caches atomically
            await cacheManager.ClearAllTokenCachesAsync(userId);

            logger.LogInformation(">>>>>>>>>> TokenState >>> Complete logout successful for user: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenState >>> Error during complete logout for user: {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> RefreshTokensIfPossibleAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Cannot refresh tokens - HttpContext is null");
            return false;
        }

        // Use mapped authentication scheme for consistency  
        var mappedScheme = GetActualAuthenticationScheme(httpContext);
        var strategy = GetAuthenticationStrategy(mappedScheme);
        if (strategy == null)
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Cannot refresh tokens - No authentication strategy");
            return false;
        }

        var userId = strategy.GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Cannot refresh tokens - No user ID");
            return false;
        }

        logger.LogInformation(">>>>>>>>>> TokenState >>> Attempting token refresh for user: {UserId}, Scheme: {Scheme}", 
            userId, strategy.SchemeName);

        try
        {
            // Check if refresh is possible
            if (!await strategy.CanRefreshTokenAsync(httpContext))
            {
                logger.LogWarning(">>>>>>>>>> TokenState >>> Token refresh not supported for scheme: {Scheme}", 
                    strategy.SchemeName);
                return false;
            }

            // Attempt refresh
            var refreshed = await strategy.RefreshExternalIdpTokenAsync(httpContext);
            if (refreshed)
            {
                // Clear OBO token cache to force re-exchange with new External IDP token
                tokenStore.ClearToken();
                
                // Clear logout flag if set
                await cacheManager.ClearLogoutFlagAsync(userId);
                
                // Set re-authentication detection flag
                cacheManager.SetRequestScopedFlag("ReAuthenticationDetected", true);
                
                logger.LogInformation(">>>>>>>>>> TokenState >>> Token refresh successful for user: {UserId}", userId);
                return true;
            }
            else
            {
                logger.LogWarning(">>>>>>>>>> TokenState >>> Token refresh failed for user: {UserId}", userId);
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenState >>> Error during token refresh for user: {UserId}", userId);
            return false;
        }
    }

    public bool ShouldForceLogout(TokenState state)
    {
        if (!state.IsAuthenticated)
        {
            return false;
        }

        // Force logout if tokens are expired and can't refresh
        var shouldLogout = state.IsAnyTokenExpired && !state.CanRefresh;
        
        logger.LogDebug(">>>>>>>>>> TokenState >>> Should force logout: {ShouldLogout}, Reason: {Reason}", 
            shouldLogout, state.LogoutReason);
        
        return shouldLogout;
    }

    public bool IsLogoutRequired()
    {
        return cacheManager.HasRequestScopedFlag("RequireLogout");
    }

    private IAuthenticationSchemeStrategy? GetAuthenticationStrategy(string? schemeName)
    {
        if (string.IsNullOrEmpty(schemeName))
        {
            logger.LogWarning(">>>>>>>>>> TokenState >>> Scheme name is null or empty");
            return null;
        }

        logger.LogDebug(">>>>>>>>>> TokenState >>> Looking for strategy for scheme: {Scheme}", schemeName);

        // Direct match first
        var strategy = authStrategies.FirstOrDefault(s => 
            string.Equals(s.SchemeName, schemeName, StringComparison.OrdinalIgnoreCase));

        if (strategy != null)
        {
            logger.LogDebug(">>>>>>>>>> TokenState >>> Found direct match strategy: {StrategyName}", strategy.SchemeName);
            return strategy;
        }

        // Fallback logic for common schemes
        if (schemeName.Contains("Test", StringComparison.OrdinalIgnoreCase) || 
            schemeName.Contains("Mock", StringComparison.OrdinalIgnoreCase))
        {
            strategy = authStrategies.FirstOrDefault(s => s.SchemeName == "TestScheme");
            if (strategy != null)
            {
                logger.LogInformation(">>>>>>>>>> TokenState >>> Using TestScheme strategy for scheme: {Scheme}", schemeName);
                return strategy;
            }
        }

        if (schemeName.Contains("OIDC", StringComparison.OrdinalIgnoreCase) || 
            schemeName.Contains("OpenId", StringComparison.OrdinalIgnoreCase))
        {
            strategy = authStrategies.FirstOrDefault(s => s.SchemeName == "OIDC");
            if (strategy != null)
            {
                logger.LogInformation(">>>>>>>>>> TokenState >>> Using OIDC strategy for scheme: {Scheme}", schemeName);
                return strategy;
            }
        }

        // Log available strategies for debugging
        var availableStrategies = string.Join(", ", authStrategies.Select(s => s.SchemeName));
        logger.LogWarning(">>>>>>>>>> TokenState >>> No matching authentication strategy for scheme: {Scheme}. Available strategies: {Available}", 
            schemeName, availableStrategies);
        
        return null;
    }

    private TokenInfo GetOboToken()
    {
        try
        {
            var token = tokenStore.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                logger.LogDebug(">>>>>>>>>> TokenState >>> No OBO token found in store");
                return new TokenInfo();
            }

            var expiry = tokenStore.GetTokenExpiry();
            var tokenInfo = new TokenInfo
            {
                Value = token,
                ExpiryTime = expiry
            };

            logger.LogDebug(">>>>>>>>>> TokenState >>> OBO token: Valid={IsValid}, Expires={Expiry}", 
                tokenInfo.IsValid, tokenInfo.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss UTC"));

            return tokenInfo;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> TokenState >>> Error getting OBO token");
            return new TokenInfo();
        }
    }

    private string? GetActualAuthenticationScheme(HttpContext httpContext)
    {
        // For OIDC flows, the AuthenticationType is often "AuthenticationTypes.Federation"
        // but we need the actual authentication scheme used ("OpenIdConnect")
        var authType = httpContext.User?.Identity?.AuthenticationType;
        
        if (string.IsNullOrEmpty(authType))
            return null;
            
        // Map common identity authentication types to actual scheme names
        return authType switch
        {
            "AuthenticationTypes.Federation" => "OpenIdConnect",
            "TestAuthentication" => "TestAuthentication", 
            _ => authType // Fallback to original value
        };
    }
}