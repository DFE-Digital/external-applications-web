using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.ExternalApplications.Api.Client.Security;

public static class TokenRefreshExtensions
{
    /// <summary>
    /// Forces a token refresh by clearing the stored internal token. This will cause a new token exchange on the next API request,
    /// ensuring that any changes to user permissions are reflected in the new token.
    /// </summary>
    public static void ForceTokenRefresh(this IHttpContextAccessor httpContextAccessor)
    {
        var httpContext = httpContextAccessor.HttpContext;
        
        // Get logger factory and create logger with specific category name since this is a static class
        var loggerFactory = httpContext?.RequestServices.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("GovUK.Dfe.ExternalApplications.Api.Client.Security.TokenRefreshExtensions");
        
        logger?.LogInformation(">>>>>>>>>> Authentication >>> ForceTokenRefresh called");
        
        if (httpContext == null) 
        {
            logger?.LogWarning(">>>>>>>>>> Authentication >>> HttpContext is null in ForceTokenRefresh");
            return;
        }

        logger?.LogDebug(">>>>>>>>>> Authentication >>> Getting IInternalUserTokenStore service for token refresh");
        
        try
        {
            var tokenStore = httpContext.RequestServices.GetRequiredService<IInternalUserTokenStore>();
            
            logger?.LogInformation(">>>>>>>>>> Authentication >>> Clearing token to force refresh for user: {UserName}", 
                httpContext.User?.Identity?.Name);
            
            tokenStore.ClearToken();
            
            logger?.LogInformation(">>>>>>>>>> Authentication >>> Token refresh completed - next API request will trigger new token exchange");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, ">>>>>>>>>> Authentication >>> Failed to force token refresh for user: {UserName}", 
                httpContext.User?.Identity?.Name);
            throw;
        }
    }
}