using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

/// <summary>
/// DEPRECATED: Use TokenManagementMiddleware instead
/// Middleware that proactively checks token expiry on every request and forces logout if tokens are expired
/// This middleware is kept for backward compatibility but uses the old architecture
/// </summary>
[Obsolete("Use TokenManagementMiddleware instead. This will be removed in a future version.")]
public class TokenExpiryMiddleware(RequestDelegate next, ILogger<TokenExpiryMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<TokenExpiryMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> MIDDLEWARE ENTRY: Processing request: {Method} {Path} - Time: {Time}", 
            context.Request.Method, context.Request.Path, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff UTC"));
        
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        var userName = context.User?.Identity?.Name ?? "Unknown";
        
        _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> MIDDLEWARE AUTH CHECK: IsAuthenticated={IsAuthenticated}, UserName={UserName}", 
            isAuthenticated, userName);

        // Only check tokens for authenticated users
        if (isAuthenticated)
        {
            _logger.LogDebug(">>>>>>>>>> TokenExpiry >>> User is authenticated, checking token expiry status");
            
            var tokenExpiryService = context.RequestServices.GetService(typeof(ITokenExpiryService)) as ITokenExpiryService;
            
            if (tokenExpiryService != null)
            {
                try
                {
                    _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> MIDDLEWARE: Calling tokenExpiryService.GetTokenExpiryInfoAsync()");
                    var expiryInfo = await tokenExpiryService.GetTokenExpiryInfoAsync();
                    
                    _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> MIDDLEWARE: ExpiryInfo - AnyExpired={AnyExpired}, CanProceed={CanProceed}, Reason={Reason}", 
                        expiryInfo.IsAnyTokenExpired, expiryInfo.CanProceedWithExchange, expiryInfo.ExpiryReason ?? "None");

                    // Only force logout if tokens are actually expired AND we can't proceed
                    // Don't force logout for first-time users who just need token exchange
                    if (expiryInfo.IsAnyTokenExpired && !expiryInfo.CanProceedWithExchange)
                    {
                        _logger.LogWarning(">>>>>>>>>> TokenExpiry >>> Tokens are expired and cannot proceed: {Reason} - forcing logout", 
                            expiryInfo.ExpiryReason);

                        // Force logout through the service
                        await tokenExpiryService.ForceLogoutAsync();

                        // Check if this is an AJAX request or API call
                        if (IsAjaxRequest(context) || IsApiRequest(context))
                        {
                            _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Returning 401 for AJAX/API request due to token expiry");
                            await WriteTokenExpiredResponse(context, expiryInfo);
                            return;
                        }
                        else
                        {
                            _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Redirecting to logout due to token expiry");
                            
                            // For web requests, set flag and let the application handle the logout
                            context.Items["TokenExpired"] = true;
                            context.Items["TokenExpiryInfo"] = expiryInfo;
                            
                            // The consuming application should check for this flag and handle logout appropriately
                        }
                    }
                    else if (expiryInfo.IsOboTokenMissing && expiryInfo.CanProceedWithExchange)
                    {
                        if (expiryInfo.ExpiryReason?.Contains("Fresh token exchange required after re-authentication") == true)
                        {
                            _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Re-authentication detected - cleared cache and will proceed with fresh token exchange");
                        }
                        else
                        {
                            _logger.LogDebug(">>>>>>>>>> TokenExpiry >>> First-time access detected - will proceed with token exchange when API calls are made");
                        }
                    }
                    else
                    {
                        // Log token health for monitoring
                        if (expiryInfo.TimeUntilEarliestExpiry.HasValue)
                        {
                            var timeRemaining = expiryInfo.TimeUntilEarliestExpiry.Value;
                            if (timeRemaining.TotalMinutes <= 10) // Log when tokens are getting close to expiry
                            {
                                _logger.LogInformation(">>>>>>>>>> TokenExpiry >>> Tokens will expire soon - Time remaining: {TimeRemaining} minutes", 
                                    timeRemaining.TotalMinutes);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ">>>>>>>>>> TokenExpiry >>> Error checking token expiry status");
                    // Don't block the request on token expiry check errors
                }
            }
            else
            {
                _logger.LogWarning(">>>>>>>>>> TokenExpiry >>> ITokenExpiryService not registered - token expiry checking disabled");
            }
        }
        else
        {
            _logger.LogDebug(">>>>>>>>>> TokenExpiry >>> User not authenticated, skipping token expiry check");
        }

        // Continue to next middleware
        await _next(context);
    }

    /// <summary>
    /// Determines if the request is an AJAX request
    /// </summary>
    private static bool IsAjaxRequest(HttpContext context)
    {
        return context.Request.Headers.ContainsKey("X-Requested-With") &&
               context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    /// <summary>
    /// Determines if the request is an API request (based on Accept header or path)
    /// </summary>
    private static bool IsApiRequest(HttpContext context)
    {
        // Check Accept header for JSON
        var acceptHeader = context.Request.Headers.Accept.FirstOrDefault();
        if (!string.IsNullOrEmpty(acceptHeader) && acceptHeader.Contains("application/json"))
        {
            return true;
        }

        // Check if path starts with /api
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            return true;
        }

        // Check Content-Type for JSON requests
        if (context.Request.ContentType?.Contains("application/json") == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Writes a JSON response for expired tokens
    /// </summary>
    private async Task WriteTokenExpiredResponse(HttpContext context, TokenExpiryInfo expiryInfo)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "token_expired",
            message = "Authentication tokens have expired. Please log in again.",
            details = expiryInfo.ExpiryReason,
            timestamp = DateTime.UtcNow,
            requiresLogin = true
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
