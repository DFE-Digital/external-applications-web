using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
[ExcludeFromCodeCoverage]
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
            return state;
        }

        // Get the appropriate authentication strategy
        var strategy = GetAuthenticationStrategy(state.AuthenticationScheme);
        if (strategy == null)
        {
            state.LogoutReason = $"No authentication strategy for scheme: {state.AuthenticationScheme}";
            return state;
        }

        // Get user ID
        state.UserId = strategy.GetUserId(httpContext);
        if (string.IsNullOrEmpty(state.UserId))
        {
            state.LogoutReason = "No user ID found";
            return state;
        }

        // Check if logout is already flagged
        if (await cacheManager.IsLogoutFlagSetAsync(state.UserId))
        {
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
                    }
                    
                    var isRecentAuthentication = timeSinceIssue <= TimeSpan.FromMinutes(2);
                    
                    if (isRecentAuthentication)
                    {
                        // Clear the logout flag since this is fresh authentication
                        await cacheManager.ClearLogoutFlagAsync(state.UserId);
                        
                        // Clear OBO token cache to force fresh exchange
                        tokenStore.ClearToken();
                        
                        // Set re-authentication detection flag for this request
                        cacheManager.SetRequestScopedFlag("ReAuthenticationDetected", true);
                        
                        // Continue with normal token processing
                    }
                    else
                    {
                        // ALTERNATIVE: If user is authenticated but logout flag is set, this might be a re-authentication
                        // Clear the flag and try once more (to handle cases where token timestamps are unreliable)
                        
                        await cacheManager.ClearLogoutFlagAsync(state.UserId);
                        tokenStore.ClearToken();
                        
                        // Set flag to prevent infinite loops
                        cacheManager.SetRequestScopedFlag("ReAuthenticationDetected", true);
                        
                        // Continue with normal token processing
                    }
                }
                catch (Exception ex)
                {
                    state.LogoutReason = "Logout flag set (token check error)";
                    return state;
                }
            }
            else
            {
                state.LogoutReason = "Logout flag set (no External IDP token)";
                return state;
            }
        }

        // Check for re-authentication detection (prevents multiple calls in same request)
        bool isReAuthentication = cacheManager.HasRequestScopedFlag("ReAuthenticationDetected");

        // Get External IDP token
        state.ExternalIdpToken = await strategy.GetExternalIdpTokenAsync(httpContext);
        
        // Get OBO token
        state.OboToken = GetOboToken();

        // Check refresh capability
        if (isReAuthentication)
        {
            // During re-authentication, allow fresh token exchange even if strategy says no refresh
            state.CanRefresh = true;
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

        return state;
    }

    public async Task<bool> ForceCompleteLogoutAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return false;
        }

        // Use mapped authentication scheme for consistency
        var mappedScheme = GetActualAuthenticationScheme(httpContext);
        var strategy = GetAuthenticationStrategy(mappedScheme);
        var userId = strategy?.GetUserId(httpContext);

        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        try
        {
            // Set logout flag first
            await cacheManager.SetLogoutFlagAsync(userId, TimeSpan.FromMinutes(10));

            // Clear all caches atomically
            await cacheManager.ClearAllTokenCachesAsync(userId);

            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<bool> RefreshTokensIfPossibleAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return false;
        }

        // Use mapped authentication scheme for consistency  
        var mappedScheme = GetActualAuthenticationScheme(httpContext);
        var strategy = GetAuthenticationStrategy(mappedScheme);
        if (strategy == null)
        {
            return false;
        }

        var userId = strategy.GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        try
        {
            // Check if refresh is possible
            if (!await strategy.CanRefreshTokenAsync(httpContext))
            {
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
                
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception ex)
        {
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
            return null;
        }

        // Direct match first
        var strategy = authStrategies.FirstOrDefault(s => 
            string.Equals(s.SchemeName, schemeName, StringComparison.OrdinalIgnoreCase));

        if (strategy != null)
        {
            return strategy;
        }

        // Fallback logic for common schemes
        if (schemeName.Contains("Test", StringComparison.OrdinalIgnoreCase) || 
            schemeName.Contains("Mock", StringComparison.OrdinalIgnoreCase))
        {
            strategy = authStrategies.FirstOrDefault(s => s.SchemeName == "TestScheme");
            if (strategy != null)
            {
                return strategy;
            }
        }

        if (schemeName.Contains("OIDC", StringComparison.OrdinalIgnoreCase) || 
            schemeName.Contains("OpenId", StringComparison.OrdinalIgnoreCase))
        {
            strategy = authStrategies.FirstOrDefault(s => s.SchemeName == "OIDC");
            if (strategy != null)
            {
                return strategy;
            }
        }

        // Log available strategies for debugging
        var availableStrategies = string.Join(", ", authStrategies.Select(s => s.SchemeName));
        
        return null;
    }

    private TokenInfo GetOboToken()
    {
        try
        {
            var token = tokenStore.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                return new TokenInfo();
            }

            var expiry = tokenStore.GetTokenExpiry();
            var tokenInfo = new TokenInfo
            {
                Value = token,
                ExpiryTime = expiry
            };

            return tokenInfo;
        }
        catch (Exception ex)
        {
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