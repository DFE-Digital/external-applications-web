using DfE.CoreLibs.Security.Interfaces;
using System.Security.Claims;

namespace DfE.ExternalApplications.Web.Security;

public record UserPermission(string ResourceKey, string Action);

public class PermissionsClaimProvider() : ICustomClaimProvider
{
    public async Task<IEnumerable<Claim>> GetClaimsAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Array.Empty<Claim>();

        var permissions = new List<UserPermission>()
        {
            new UserPermission("task1", "Read"),
            new UserPermission("task2", "Write")
        };

        return permissions.Select(p =>
            new Claim(
                "permission",
                $"{p.ResourceKey}:{p.Action}"
            )
        );
    }
}