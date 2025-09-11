using GovUK.Dfe.CoreLibs.Security.TokenRefresh.Interfaces;
using GovUK.Dfe.ExternalApplications.Api.Client.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DfE.ExternalApplications.Web.Security;

/// <summary>
/// Authentication strategy for OIDC-based authentication
/// Handles DfE Sign-In and other OIDC providers
/// </summary>
public class OidcAuthenticationStrategy(ILogger<OidcAuthenticationStrategy> logger, ITokenRefreshService tokenRefreshService) : IAuthenticationSchemeStrategy
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

            var refreshedToken = await tokenRefreshService.RefreshTokenAsync(refreshToken, CancellationToken.None);

            if (!refreshedToken.IsSuccess)
            {
                return false;
            }

            await UpdateAuthenticationTokenAsync(context, refreshedToken.Token!.IdToken!, refreshedToken.Token.RefreshToken!);

            return true;
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

    /// <summary>
    /// Update authentication context with new token
    /// For OIDC we need to update the authentication properties and set proper expiry times
    /// </summary>
    private async Task UpdateAuthenticationTokenAsync(HttpContext context, string newToken, string refreshToken)
    {
        try
        {
            // Get current authentication result
            var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (authResult.Succeeded && authResult.Properties != null)
            {
                // Parse the new token to get its expiry time
                var handler = new JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(newToken);
                var expiresAt = jsonToken.ValidTo.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

                // Update tokens in authentication properties
                var tokens = new[]
                {
                    new AuthenticationToken { Name = "id_token", Value = newToken },
                    new AuthenticationToken { Name = "access_token", Value = newToken },
                    new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
                    new AuthenticationToken { Name = "expires_at", Value = expiresAt }
                };
                authResult.Properties.StoreTokens(tokens);

                // Update the cookie expiry to match the new token expiry
                authResult.Properties.ExpiresUtc = jsonToken.ValidTo;

                // Re-sign in with updated properties to refresh the authentication cookie
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, authResult.Principal, authResult.Properties);
                
                logger.LogDebug("Successfully updated OIDC authentication tokens. New token expires at: {ExpiryTime}", jsonToken.ValidTo);
            }
            else
            {
                logger.LogWarning("Failed to update OIDC authentication tokens: Authentication result was not successful or properties were null");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating OIDC authentication tokens");
            throw;
        }
    }
}
