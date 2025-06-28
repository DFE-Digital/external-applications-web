namespace DfE.ExternalApplications.Web.Middleware;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UsePermissionsCache(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PermissionsCacheMiddleware>();
    }
} 