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
        

        
        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            
            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"{PermissionsCacheKeyPrefix}{userId}";
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
                        // Cache empty auth data on other errors but log the issue

                        
                        var emptyAuthData = new UserAuthorizationDto
                        {
                            Permissions = Enumerable.Empty<UserPermissionDto>(),
                            Roles = Enumerable.Empty<string>()
                        };
                        cache.Set(cacheKey, emptyAuthData, TimeSpan.FromMinutes(1));

                    }
                }
                else
                {

                }
            }
            else
            {

            }
        }
        else
        {

        }


        await next(context);
    }
} 