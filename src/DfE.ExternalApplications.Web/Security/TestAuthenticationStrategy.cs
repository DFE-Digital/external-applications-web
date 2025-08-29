using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DfE.CoreLibs.Security.Interfaces;
using DfE.CoreLibs.Security.Configurations;
using DfE.ExternalApplications.Web.Authentication;
using DfE.ExternalApplications.Web.Services;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Authentication strategy for Test authentication scheme
/// Can always "refresh" by generating new tokens using consuming app's services
/// </summary>
public class TestAuthenticationStrategy(
    ILogger<TestAuthenticationStrategy> logger,
    IUserTokenService userTokenService,
    IOptions<TestAuthenticationOptions> testAuthOptions) : IAuthenticationSchemeStrategy
{
    private readonly TestAuthenticationOptions _testAuthOptions = testAuthOptions.Value;
    
    /// <summary>
    /// Matches the actual scheme name from TestAuthenticationHandler
    /// </summary>
    public string SchemeName => TestAuthenticationHandler.SchemeName; // "TestAuthentication"

    public async Task<TokenInfo> GetExternalIdpTokenAsync(HttpContext context)
    {
        logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Getting External IDP token for Test scheme");
        
        try
        {
            // First check session storage (primary storage for TestAuth)
            var token = context.Session.GetString("TestAuth:Token");
            
            // Fallback to authentication properties if not in session
            if (string.IsNullOrEmpty(token))
            {
                token = await context.GetTokenAsync("id_token");
            }
            
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> No id_token found in session or auth properties for Test scheme");
                return new TokenInfo();
            }

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var tokenInfo = new TokenInfo
            {
                Value = token,
                ExpiryTime = jsonToken.ValidTo
            };

            logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Test External IDP token: Valid={IsValid}, Expires={Expiry}", 
                tokenInfo.IsValid, tokenInfo.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss UTC"));

            return tokenInfo;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error getting Test External IDP token");
            return new TokenInfo();
        }
    }

    public async Task<bool> CanRefreshTokenAsync(HttpContext context)
    {
        try
        {
            // Get current token to check its expiry
            var tokenInfo = await GetExternalIdpTokenAsync(context);
            
            if (!tokenInfo.IsPresent || !tokenInfo.ExpiryTime.HasValue)
            {
                logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Cannot refresh - no valid token found");
                return false;
            }
            
            var timeUntilExpiry = tokenInfo.ExpiryTime.Value - DateTime.UtcNow;
            var minutesRemaining = timeUntilExpiry.TotalMinutes;
            
            // Allow refresh if token is in the 5-10 minute window
            // - More than 10 minutes: no refresh needed
            // - 5-10 minutes: allow refresh to get fresh token  
            // - Less than 5 minutes: force logout (handled by TokenInfo.IsExpired)
            if (minutesRemaining > 5 && minutesRemaining <= 10)
            {
                logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Token expires in {Minutes:F1} minutes - ALLOWING refresh during 5-10 minute window", minutesRemaining);
                return true;
            }
            
            if (minutesRemaining <= 5)
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> Token expires in {Minutes:F1} minutes - FORCING logout (less than 5 minutes remaining)", minutesRemaining);
            }
            else
            {
                logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Token expires in {Minutes:F1} minutes - no refresh needed yet", minutesRemaining);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error checking if token can be refreshed");
            return false;
        }
    }

    public async Task<bool> RefreshExternalIdpTokenAsync(HttpContext context)
    {
        logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Starting token refresh during 5-10 minute window");
        
        try
        {
            if (!_testAuthOptions.Enabled)
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> Test authentication is disabled, cannot refresh");
                return false;
            }

            // Get user ID
            var userId = GetUserId(context);
            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> Cannot refresh - no user ID found");
                return false;
            }

            // Generate a new token using the proper consuming app services
            var newToken = await GenerateNewTestTokenAsync(userId, context);
            if (string.IsNullOrEmpty(newToken))
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> Failed to generate new test token");
                return false;
            }

            // Update the authentication context with the new token
            await UpdateAuthenticationTokenAsync(context, newToken);
            
            logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Test token refresh successful for user: {UserId} - fresh token generated", userId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error refreshing Test External IDP token");
            return false;
        }
    }

    /// <summary>
    /// Generate new test token using the consuming app's UserTokenService
    /// This ensures consistent token generation with the rest of the application
    /// </summary>
    private async Task<string?> GenerateNewTestTokenAsync(string userId, HttpContext context)
    {
        try
        {
            logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Creating claims for user {UserId}", userId);
            
            // Create claims for the user (same as TestAuthenticationService does)
            var claims = new[]
            {
                new Claim(ClaimTypes.Email, userId),
                new Claim("sub", userId),
                new Claim(ClaimTypes.Name, userId),
                new Claim("name", userId)
            };
            
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Use the consuming app's UserTokenService to generate the token
            // This ensures consistency with how tokens are generated during login
            var newToken = await userTokenService.GetUserTokenAsync(principal);
            
            logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Generated new test token for user: {UserId}", userId);
            
            return newToken;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error generating new test token using UserTokenService");
            return null;
        }
    }

    /// <summary>
    /// Update authentication context with new token
    /// Ensures both session storage (primary) and authentication properties are updated
    /// </summary>
    private async Task UpdateAuthenticationTokenAsync(HttpContext context, string newToken)
    {
        try
        {
            // Update session first (primary storage for TestAuth)
            context.Session.SetString("TestAuth:Token", newToken);
            logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Updated session with new token");

            // Get current authentication result
            var authResult = await context.AuthenticateAsync();
            if (authResult.Succeeded && authResult.Properties != null)
            {
                // Update authentication properties tokens as well for consistency
                var tokens = new[]
                {
                    new AuthenticationToken { Name = "id_token", Value = newToken },
                    new AuthenticationToken { Name = "access_token", Value = newToken }
                };
                authResult.Properties.StoreTokens(tokens);
                
                // Re-sign in with updated token and properties
                await context.SignInAsync(authResult.Principal, authResult.Properties);
                
                logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Updated authentication context with new token");
            }
            else
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> Authentication result not succeeded or no properties found - token updated in session only");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error updating authentication token");
            throw;
        }
    }

    public string? GetUserId(HttpContext context)
    {
        var userId = context.User?.FindFirst(ClaimTypes.Email)?.Value 
                    ?? context.User?.FindFirst("sub")?.Value
                    ?? context.User?.Identity?.Name;
        
        logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Test UserId: {UserId}", userId ?? "Not found");
        return userId;
    }
}
