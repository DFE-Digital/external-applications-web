using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using GovUK.Dfe.ExternalApplications.Api.Client.Contracts;

namespace DfE.ExternalApplications.Web.Middleware;

public class PermissionsCacheMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IUsersClient _usersClient;
    public const string PermissionsCacheKeyPrefix = "UserPermissions_";

    public PermissionsCacheMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IUsersClient usersClient)
    {
        _next = next;
        _cache = cache;
        _usersClient = usersClient;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                var cacheKey = $"{PermissionsCacheKeyPrefix}{userId}";
                if (!_cache.TryGetValue(cacheKey, out _))
                {
                    try
                    {
                        var permissions = await _usersClient.GetMyPermissionsAsync();
                        _cache.Set(cacheKey, permissions, TimeSpan.FromMinutes(5));
                    }
                    catch (Exception)
                    {
                        // Log error if needed, but continue the request
                        _cache.Set(cacheKey, Array.Empty<dynamic>(), TimeSpan.FromMinutes(1));
                    }
                }
            }
        }

        await _next(context);
    }
} 