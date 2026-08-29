using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Web.Auth;

// S-1.1: scripts/02_schema.sql deliberately has no ASP.NET Identity role tables
// (no AspNetRoles). Authorization therefore cannot use store-backed roles; instead
// the domain Role enum on [dbo].[USER] is projected into a standard role claim at
// principal-build time, so [Authorize(Roles = nameof(UserRole.Admin))] works
// everywhere without ever touching a role table.
public class UserRoleClaimsTransformation(UserManager<User> userManager) : IClaimsTransformation
{
    private const string RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return principal;
        }

        if (identity.HasClaim(c => c.Type == RoleClaimType))
        {
            return principal;
        }

        var userIdValue = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdValue, out var userId))
        {
            return principal;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return principal;
        }

        identity.AddClaim(new Claim(RoleClaimType, user.Role.ToString()));
        if (!identity.HasClaim(c => c.Type == "FullName") && !string.IsNullOrWhiteSpace(user.FullName))
        {
            identity.AddClaim(new Claim("FullName", user.FullName));
        }
        return principal;
    }
}
