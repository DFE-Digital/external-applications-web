using DfE.ExternalApplications.Web.Security;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
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
        if (AuthenticationPathExclusions.ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

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
                    catch (ExternalApplicationsException ex) when (ex.StatusCode == 401)
                    {
                        context.Response.Redirect("/Logout?reason=auth_failed");
                        return;
                    }
                    catch (ExternalApplicationsException ex) when (ex.StatusCode == 403 && IsAuthenticationFailure(ex))
                    {
                        context.Response.Redirect("/Logout?reason=token_expired");
                        return;
                    }
                    catch (ExternalApplicationsException ex) when (ex.StatusCode == 403)
                    {
                        // Permission denied is not an auth failure; do not cache empty claims.
                        logger?.LogWarning(ex, "Permission denied fetching permissions for {UserId}", userId);
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
                    {
                        context.Response.Redirect("/Logout?reason=token_expired");
                        return;
                    }
                    catch (HttpRequestException ex) when (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                    {
                        context.Response.Redirect("/Logout?reason=auth_failed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to fetch user permissions for {UserId}", userId);
                    }
                }
            }
        }

        await next(context);
    }

    private static bool IsAuthenticationFailure(ExternalApplicationsException ex) =>
        ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Not authenticated", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("No user identifier", StringComparison.OrdinalIgnoreCase);
} 