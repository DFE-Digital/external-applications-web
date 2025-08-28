using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Authentication strategy for OIDC-based authentication
/// Handles DfE Sign-In and other OIDC providers
/// </summary>
public class OidcAuthenticationStrategy(ILogger<OidcAuthenticationStrategy> logger) : IAuthenticationSchemeStrategy
{
    /// <summary>
    /// Matches the OIDC authentication scheme name from Program.cs configuration
    /// Note: This matches OpenIdConnectDefaults.AuthenticationScheme ("OpenIdConnect")
    /// </summary>
    public string SchemeName => OpenIdConnectDefaults.AuthenticationScheme; // "OpenIdConnect"

    public async Task<TokenInfo> GetExternalIdpTokenAsync(HttpContext context)
    {
        logger.LogDebug(">>>>>>>>>> AuthStrategy >>> Getting External IDP token for OIDC scheme");
        
        try
        {
            var token = await context.GetTokenAsync("id_token");
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> No id_token found for OIDC scheme");
                return new TokenInfo();
            }

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var tokenInfo = new TokenInfo
            {
                Value = token,
                ExpiryTime = jsonToken.ValidTo
            };

            logger.LogInformation(">>>>>>>>>> AuthStrategy >>> OIDC External IDP token: Valid={IsValid}, Expires={Expiry}", 
                tokenInfo.IsValid, tokenInfo.ExpiryTime?.ToString("yyyy-MM-dd HH:mm:ss UTC"));

            return tokenInfo;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error getting OIDC External IDP token");
            return new TokenInfo();
        }
    }

    public async Task<bool> CanRefreshTokenAsync(HttpContext context)
    {
        logger.LogDebug(">>>>>>>>>> AuthStrategy >>> OIDC scheme CANNOT refresh - forcing logout when tokens expire (as per requirement)");
        
        // According to requirement: when tokens are within 5 minutes of expiry, force logout, don't refresh
        return await Task.FromResult(false);
    }

    public async Task<bool> RefreshExternalIdpTokenAsync(HttpContext context)
    {
        logger.LogInformation(">>>>>>>>>> AuthStrategy >>> Refresh called but will NOT refresh - forcing logout instead (as per requirement)");
        
        // According to requirement: when tokens expire, force logout, don't refresh
        // This method should never be called since CanRefreshTokenAsync returns false
        return false;
        
        try
        {
            var refreshToken = await context.GetTokenAsync("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                logger.LogWarning(">>>>>>>>>> AuthStrategy >>> No refresh token available for OIDC");
                return false;
            }

            // For OIDC, we typically need to use the refresh token to get new tokens
            // This would involve calling the token endpoint of the OIDC provider
            // For now, log that this is not implemented but the framework is in place
            logger.LogWarning(">>>>>>>>>> AuthStrategy >>> OIDC token refresh not yet implemented - would use refresh_token: {RefreshTokenPresent}", 
                !string.IsNullOrEmpty(refreshToken));
            
            // TODO: Implement actual OIDC token refresh logic
            // This would involve:
            // 1. Call the OIDC provider's token endpoint with refresh_token
            // 2. Get new id_token and access_token
            // 3. Update the authentication properties
            // 4. Re-sign in the user with new tokens
            
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> AuthStrategy >>> Error refreshing OIDC External IDP token");
            return false;
        }
    }

    public string? GetUserId(HttpContext context)
    {
        // OIDC typically uses different claim types than test authentication
        var userId = context.User?.FindFirst(ClaimTypes.Email)?.Value 
                    ?? context.User?.FindFirst("email")?.Value
                    ?? context.User?.FindFirst("sub")?.Value
                    ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User?.Identity?.Name;
        
        logger.LogDebug(">>>>>>>>>> AuthStrategy >>> OIDC UserId: {UserId}", userId ?? "Not found");
        return userId;
    }
}
