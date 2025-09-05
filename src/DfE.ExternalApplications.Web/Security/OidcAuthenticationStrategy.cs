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
        try
        {
            var token = await context.GetTokenAsync("id_token");
            if (string.IsNullOrEmpty(token))
            {
                return new TokenInfo();
            }

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            var tokenInfo = new TokenInfo
            {
                Value = token,
                ExpiryTime = jsonToken.ValidTo
            };

            return tokenInfo;
        }
        catch (Exception ex)
        {
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
                return false;
            }
            
            var timeUntilExpiry = tokenInfo.ExpiryTime.Value - DateTime.UtcNow;
            var minutesRemaining = timeUntilExpiry.TotalMinutes;

            // Allow refresh when remaining time is within the configured lead window
            var settings = context.RequestServices.GetService(typeof(Microsoft.Extensions.Options.IOptions<TokenRefreshSettings>)) as Microsoft.Extensions.Options.IOptions<TokenRefreshSettings>;
            var lead = settings?.Value.RefreshLeadTimeMinutes ?? 10;
            var forceLogoutAt = settings?.Value.ForceLogoutAtMinutesRemaining ?? 5;

            if (minutesRemaining > forceLogoutAt && minutesRemaining <= lead)
            {
                return true; 
            }
            
            if (minutesRemaining <= 5)
            {
            }
            else
            {
            }
            
            return false;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<bool> RefreshExternalIdpTokenAsync(HttpContext context)
    {
        try
        {
            var refreshToken = await context.GetTokenAsync("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            // For OIDC, we typically need to use the refresh token to get new tokens
            // This would involve calling the token endpoint of the OIDC provider
            
            // TODO: Implement actual OIDC token refresh logic
            // This would involve:
            // 1. Call the OIDC provider's token endpoint with refresh_token
            // 2. Get new id_token and access_token
            // 3. Update the authentication properties
            // 4. Re-sign in the user with new tokens
            
            return false;
        }
        catch (Exception ex)
        {
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
        
        return userId;
    }
}
