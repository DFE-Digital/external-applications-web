using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Diagnostics.CodeAnalysis;
using DfE.ExternalApplications.Domain.Models;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;
using Task = System.Threading.Tasks.Task;

namespace DfE.ExternalApplications.Web.Middleware;

[ExcludeFromCodeCoverage]
public class PermissionsCacheMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    IUsersClient usersClient)
{
    public const string PermissionsCacheKeyPrefix = "UserPermissions_";

    public async Task InvokeAsync(HttpContext context)
    {
        var logger = context.RequestServices.GetService<ILogger<PermissionsCacheMiddleware>>();
        var user = context.User;
        var requestPath = context.Request.Path;
        
        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Processing request at path {Path}", requestPath);
        
        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: User {UserId} is authenticated", userId ?? "Unknown");
            
            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"{PermissionsCacheKeyPrefix}{userId}";
                if (!cache.TryGetValue(cacheKey, out _))
                {
                    logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: No cached permissions found for user {UserId}, fetching from API", userId);
                    
                    try
                    {
                        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Calling API to get permissions for user {UserId}", userId);
                        var permissions = await usersClient.GetMyPermissionsAsync();
                        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Successfully received {PermissionCount} permissions for user {UserId}", 
                            permissions?.Permissions?.Count() ?? 0, userId);
                        cache.Set(cacheKey, permissions, TimeSpan.FromMinutes(5));
                        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Cached permissions for user {UserId}", userId);
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
                    {
                        logger?.LogWarning(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Received 403/Forbidden from API for user {UserId}: {Error}. Token likely invalid/expired, redirecting to logout.", 
                            userId, ex.Message);
                        
                        // Token is invalid/expired - force re-authentication
                        context.Response.Redirect("/Logout?reason=token_expired");
                        return;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                    {
                        logger?.LogWarning(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Received 401/Unauthorized from API for user {UserId}: {Error}. Authentication failed, redirecting to logout.", 
                            userId, ex.Message);
                        
                        // Authentication failed - force re-authentication
                        context.Response.Redirect("/Logout?reason=auth_failed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        // Cache empty auth data on other errors but log the issue
                        logger?.LogWarning(ex, ">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Failed to load user permissions for user {UserId}. Error: {Error}", userId, ex.Message);
                        
                        var emptyAuthData = new UserAuthorizationDto
                        {
                            Permissions = Enumerable.Empty<UserPermissionDto>(),
                            Roles = Enumerable.Empty<string>()
                        };
                        cache.Set(cacheKey, emptyAuthData, TimeSpan.FromMinutes(1));
                        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Cached empty permissions for user {UserId} due to error", userId);
                    }
                }
                else
                {
                    logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Using cached permissions for user {UserId}", userId);
                }
            }
            else
            {
                logger?.LogWarning(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: User is authenticated but has no NameIdentifier claim");
            }
        }
        else
        {
            logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: User is not authenticated");
        }

        logger?.LogDebug(">>>>>>>>>> Authentication >>> PermissionsCacheMiddleware: Proceeding to next middleware");
        await next(context);
    }
} 