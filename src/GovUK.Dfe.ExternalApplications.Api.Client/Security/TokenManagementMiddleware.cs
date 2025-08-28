using System;
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
public class TokenManagementMiddleware(RequestDelegate next, ILogger<TokenManagementMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<TokenManagementMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, ITokenStateManager tokenStateManager)
    {
        _logger.LogInformation(">>>>>>>>>> TokenManagement >>> MIDDLEWARE ENTRY: {Method} {Path} - {Time}", 
            context.Request.Method, context.Request.Path, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff UTC"));

        // Only process authenticated requests
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            _logger.LogDebug(">>>>>>>>>> TokenManagement >>> User not authenticated, skipping token management");
            await _next(context);
            return;
        }

        var userName = context.User.Identity.Name ?? "Unknown";
        _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Processing authenticated user: {UserName}", userName);

        try
        {
            // Get current token state
            var tokenState = await tokenStateManager.GetCurrentTokenStateAsync();
            
            // Check if we should force logout
            if (tokenStateManager.ShouldForceLogout(tokenState))
            {
                _logger.LogWarning(">>>>>>>>>> TokenManagement >>> Forcing logout for user: {UserName}, Reason: {Reason}", 
                    userName, tokenState.LogoutReason);

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
                    _logger.LogWarning(">>>>>>>>>> TokenManagement >>> Forcing web logout and redirect for user: {UserName}", userName);
                    
                    // Sign out from both cookie and OIDC schemes for complete logout
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    await context.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
                    
                    // Clear authentication-specific session data to prevent re-authentication
                    var authScheme = context.User?.Identity?.AuthenticationType;
                    if (authScheme == "TestAuthentication")
                    {
                        // Clear TestAuth session data to prevent infinite logout loop
                        context.Session.Remove("TestAuth:Email");
                        context.Session.Remove("TestAuth:Token");
                        _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Cleared TestAuth session data for logout");
                    }
                    else if (authScheme == "AuthenticationTypes.Federation")
                    {
                        // Clear OIDC-related data to prevent re-authentication
                        context.Session.Clear(); // Clear entire session for OIDC
                        _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Cleared OIDC session data for logout");
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
        
        _logger.LogInformation(">>>>>>>>>> TokenManagement >>> Returned 401 JSON response for API request");
    }
}