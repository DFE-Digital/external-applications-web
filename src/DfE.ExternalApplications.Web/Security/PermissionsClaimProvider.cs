using DfE.CoreLibs.Security.Interfaces;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using DfE.ExternalApplications.Web.Middleware;
using System.Diagnostics.CodeAnalysis;
using DfE.CoreLibs.Contracts.ExternalApplications.Models.Response;

namespace DfE.ExternalApplications.Web.Security;

[ExcludeFromCodeCoverage]
public class PermissionsClaimProvider(IMemoryCache cache) : ICustomClaimProvider
{
    public Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Task.FromResult(Enumerable.Empty<Claim>());

        var cacheKey = $"{PermissionsCacheMiddleware.PermissionsCacheKeyPrefix}{userId}";
        
        if (cache.TryGetValue(cacheKey, out IEnumerable<UserPermissionDto>? permissions))
        {
            var claims = permissions?.Select(p =>
                new Claim(
                    "permission",
                    $"{p.ResourceType}:{p.ResourceKey}:{p.AccessType}"
                )
            );
            return Task.FromResult(claims ?? []);
        }

        return Task.FromResult(Enumerable.Empty<Claim>());
    }
}