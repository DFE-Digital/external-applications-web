using DfE.CoreLibs.Security.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace DfE.ExternalApplications.Web.Middleware
{
    [ExcludeFromCodeCoverage]
    public class TokenExpiryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TokenExpiryMiddleware> _logger;
        private readonly TestAuthenticationOptions _testAuthOptions;
        private static readonly TimeSpan ExpiryThreshold = TimeSpan.FromMinutes(10);

        public TokenExpiryMiddleware(
            RequestDelegate next, 
            ILogger<TokenExpiryMiddleware> logger,
            IOptions<TestAuthenticationOptions> testAuthOptions)
        {
            _next = next;
            _logger = logger;
            _testAuthOptions = testAuthOptions.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Skip token expiry checks when test authentication is enabled
            if (_testAuthOptions.Enabled)
            {
                await _next(context);
                return;
            }

            var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (result.Succeeded)
            {
                var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var expiresUtc = result.Properties?.ExpiresUtc;
                
                if (expiresUtc.HasValue)
                {
                    var remaining = expiresUtc.Value - DateTimeOffset.UtcNow;
                    
                    // Log token status for debugging
                    _logger.LogDebug(
                        "Token check for user {UserId}: {RemainingMinutes} minutes remaining (Expires: {ExpiresUtc})", 
                        userId,
                        remaining.TotalMinutes,
                        expiresUtc.Value);
                    
                    if (remaining <= TimeSpan.Zero)
                    {
                        _logger.LogWarning(
                            "Authentication ticket EXPIRED for user {UserId} (Expired: {ExpiresUtc}, Current: {UtcNow}). Forcing logout.", 
                            userId,
                            expiresUtc.Value,
                            DateTimeOffset.UtcNow);

                        // Token already expired - force logout immediately
                        context.Response.Redirect("/Logout?reason=token_expired");
                        return;
                    }
                    else if (remaining <= ExpiryThreshold)
                    {
                        _logger.LogInformation(
                            "Authentication ticket expiring soon for user {UserId}: {RemainingSeconds} seconds remaining (UTC: {UtcNow}, Expires: {ExpiresUtc}). Triggering refresh.", 
                            userId,
                            remaining.TotalSeconds,
                            DateTimeOffset.UtcNow,
                            expiresUtc.Value);

                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
                        return;
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Authentication ticket for user {UserId} has no expiry time. This may indicate a configuration issue.", 
                        userId);
                }
            }
            else
            {
                _logger.LogDebug("Authentication failed or user not authenticated. Reason: {Failure}", result.Failure?.Message ?? "Unknown");
            }

            await _next(context);
        }
    }

    [ExcludeFromCodeCoverage]
    public static class TokenExpiryMiddlewareExtensions
    {
        public static IApplicationBuilder UseTokenExpiryCheck(this IApplicationBuilder app)
        {
            return app.UseMiddleware<TokenExpiryMiddleware>();
        }
    }
}