using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// Simplified middleware that orchestrates token management
/// Single responsibility: Request interception and token state orchestration
/// </summary>
[ExcludeFromCodeCoverage]
public class TokenManagementMiddleware(RequestDelegate next, ILogger<TokenManagementMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<TokenManagementMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, ITokenStateManager tokenStateManager)
    {
        // Only process authenticated requests
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userName = context.User.Identity.Name ?? "Unknown";

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
            
            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                await tokenStateManager.ForceCompleteLogoutAsync();

                // Handle response based on request type
                if (IsApiRequest(context))
                {
                    await WriteUnauthorizedJsonResponse(context, tokenState.LogoutReason);
                    return;
                }
                else
                {
                    // For web requests, force immediate logout and redirect
                    
                    // Get authentication scheme to determine which schemes to sign out from
                    var authScheme = context.User?.Identity?.AuthenticationType;
                    
                    // Sign out from appropriate schemes based on current authentication
                    if (authScheme == "AuthenticationTypes.Federation")
                    {
                        // For OIDC, sign out from both cookie and OIDC schemes
                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
                    }
                    else
                    {
                        // For other schemes (like TestAuthentication), only sign out from default scheme
                        await context.SignOutAsync();
                    }
                    
                    // Clear authentication-specific session data to prevent re-authentication
                    if (authScheme == "TestAuthentication")
                    {
                        // Clear TestAuth session data to prevent infinite logout loop
                        context.Session.Remove("TestAuth:Email");
                        context.Session.Remove("TestAuth:Token");
                    }
                    else if (authScheme == "AuthenticationTypes.Federation")
                    {
                        // Clear OIDC-related data to prevent re-authentication
                        context.Session.Clear(); // Clear entire session for OIDC
                    }
                    
                    // Redirect to home page or login page
                    context.Response.Redirect("/", permanent: false);
                    return;
                }
            }
            else if (tokenState.CanRefresh && tokenState.IsAnyTokenExpired)
            {
                _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Attempting token refresh for user: {UserName}", userName);
                
                var refreshed = await tokenStateManager.RefreshTokensIfPossibleAsync();
                if (refreshed)
                {
                    _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Token refresh successful for user: {UserName}", userName);
                }
                else
                {
                    _logger.LogWarning(">>>>>>>>>> TokenManagement >>> Token refresh failed for user: {UserName}", userName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ">>>>>>>>>> TokenManagement >>> Error during token management for user: {UserName}", userName);
            // Continue processing - don't break the request for token management errors
        }

        await _next(context);
    }

    private static bool IsApiRequest(HttpContext context)
    {
        return context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
               context.Request.Headers.ContainsKey("X-Requested-With");
    }

    private async Task WriteUnauthorizedJsonResponse(HttpContext context, string? reason)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "unauthorized",
            message = "Authentication tokens have expired",
            reason = reason ?? "Token expiry",
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}