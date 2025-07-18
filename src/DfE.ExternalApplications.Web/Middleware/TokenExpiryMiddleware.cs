﻿using DfE.CoreLibs.Security.Configurations;
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
                var expiresUtc = result.Properties?.ExpiresUtc;
                if (expiresUtc.HasValue)
                {
                    var remaining = expiresUtc.Value - DateTimeOffset.UtcNow;
                    if (remaining <= ExpiryThreshold)
                    {
                        _logger.LogInformation(
                            "Authentication ticket expiring in {RemainingSeconds} seconds (UTC: {UtcNow}, Expires: {ExpiresUtc}). Signing out.", 
                            remaining.TotalSeconds,
                            DateTimeOffset.UtcNow,
                            expiresUtc.Value);

                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        await context.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
                        return;
                    }
                }
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