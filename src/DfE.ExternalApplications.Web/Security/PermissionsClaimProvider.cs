using DfE.CoreLibs.Security.Interfaces;
using System.Security.Claims;
using DfE.ExternalApplications.Client.Contracts;
using Microsoft.Extensions.Caching.Memory;
using DfE.ExternalApplications.Web.Middleware;

namespace DfE.ExternalApplications.Web.Security;

public class PermissionsClaimProvider : ICustomClaimProvider
{
    private readonly IMemoryCache _cache;

    public PermissionsClaimProvider(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(Enumerable.Empty<Claim>());

        var cacheKey = $"{PermissionsCacheMiddleware.PermissionsCacheKeyPrefix}{userId}";
        
        if (_cache.TryGetValue(cacheKey, out dynamic permissions))
        {
            var claims = ((IEnumerable<dynamic>)permissions).Select(p =>
                new Claim(
                    "permission",
                    $"{p.ResourceType}:{p.ResourceKey}:{p.AccessType}"
                )
            );
            return Task.FromResult(claims);
        }

        return Task.FromResult(Enumerable.Empty<Claim>());
    }
}