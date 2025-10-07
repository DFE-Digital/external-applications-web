using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using System.Diagnostics.CodeAnalysis;
using DfE.ExternalApplications.Domain.Models;
using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
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

        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = user.FindFirstValue(ClaimTypes.Email);

            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"{PermissionsCacheKeyPrefix}{userId+email}";
                
                if (!cache.TryGetValue(cacheKey, out _))
                {
                    try
                    {
                        var permissions = await usersClient.GetMyPermissionsAsync();
                        cache.Set(cacheKey, permissions, TimeSpan.FromMinutes(5));
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
                    {
                        // Token is invalid/expired - force re-authentication
                        context.Response.Redirect("/Logout?reason=token_expired");
                        return;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                    {
                        // Authentication failed - force re-authentication
                        context.Response.Redirect("/Logout?reason=auth_failed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to fetch user permissions for {UserId}", userId);
                        
                        var emptyAuthData = new UserAuthorizationDto
                        {
                            Permissions = Enumerable.Empty<UserPermissionDto>(),
                            Roles = Enumerable.Empty<string>()
                        };
                        cache.Set(cacheKey, emptyAuthData, TimeSpan.FromMinutes(1));
                    }
                }
            }
        }

        await next(context);
    }
} 