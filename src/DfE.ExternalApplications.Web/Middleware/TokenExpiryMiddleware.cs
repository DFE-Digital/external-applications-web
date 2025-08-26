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
            var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";
            var requestPath = context.Request.Path;
            var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown";
            
            _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Processing request for user {UserId} at path {Path} from {UserAgent}", 
                userId, requestPath, userAgent);
            
            // Skip token expiry checks when test authentication is enabled
            if (_testAuthOptions.Enabled)
            {
                _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Test authentication enabled, skipping token expiry checks for user {UserId}", userId);
                await _next(context);
                return;
            }

            _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Checking authentication state for user {UserId}", userId);
            var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            
            if (result.Succeeded)
            {
                _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Authentication succeeded for user {UserId}", userId);
                
                var expiresUtc = result.Properties?.ExpiresUtc;
                
                if (expiresUtc.HasValue)
                {
                    var remaining = expiresUtc.Value - DateTimeOffset.UtcNow;
                    
                    // Log token status for debugging
                    _logger.LogDebug(
                        ">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Token check for user {UserId}: {RemainingMinutes} minutes remaining (Expires: {ExpiresUtc})", 
                        userId,
                        remaining.TotalMinutes,
                        expiresUtc.Value);
                    
                    if (remaining <= TimeSpan.Zero)
                    {
                        _logger.LogWarning(
                            ">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Authentication ticket EXPIRED for user {UserId} (Expired: {ExpiresUtc}, Current: {UtcNow}). Forcing logout.", 
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
                            ">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Authentication ticket expiring soon for user {UserId}: {RemainingSeconds} seconds remaining (UTC: {UtcNow}, Expires: {ExpiresUtc}). Triggering refresh.", 
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
                        ">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Authentication ticket for user {UserId} has no expiry time. This may indicate a configuration issue.", 
                        userId);
                }
            }
            else
            {
                _logger.LogWarning(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Authentication failed for user {UserId} at path {Path}. Reason: {Failure}", 
                    userId, requestPath, result.Failure?.Message ?? "Unknown");
            }

            _logger.LogDebug(">>>>>>>>>> Authentication >>> TokenExpiryMiddleware: Proceeding to next middleware for user {UserId}", userId);
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