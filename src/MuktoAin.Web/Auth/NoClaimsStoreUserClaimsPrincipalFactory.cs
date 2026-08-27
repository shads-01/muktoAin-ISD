using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Web.Auth;

// The default UserClaimsPrincipalFactory<TUser> checks UserManager.SupportsUserClaim
// during every real sign-in and, if true, calls UserManager.GetClaimsAsync(user) --
// that flag is true here because AppDbContext's EF store implements
// IUserClaimStore<User> (bundled automatically with IdentityDbContext), even though
// scripts/02_schema.sql deliberately never creates AspNetUserClaims (see AppDbContext
// and UserRoleClaimsTransformation). Left unpatched, the first real
// SignInManager.SignInAsync/PasswordSignInAsync call throws
// "Invalid object name 'AspNetUserClaims'." mid-signin.
//
// This factory reproduces the base class's claim set exactly, minus the
// GetClaimsAsync(user) call -- role authorization already comes from the User.Role
// enum via UserRoleClaimsTransformation (added post-authentication), so no
// functionality is lost by skipping the (nonexistent) claims store here.
public class NoClaimsStoreUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<User>
{
    public NoClaimsStoreUserClaimsPrincipalFactory(
        UserManager<User> userManager, IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var userId = await UserManager.GetUserIdAsync(user);
        var userName = await UserManager.GetUserNameAsync(user);
        var identity = new ClaimsIdentity(
            IdentityConstants.ApplicationScheme,
            Options.ClaimsIdentity.UserNameClaimType,
            Options.ClaimsIdentity.RoleClaimType);
        identity.AddClaim(new Claim(Options.ClaimsIdentity.UserIdClaimType, userId));
        identity.AddClaim(new Claim(Options.ClaimsIdentity.UserNameClaimType, userName ?? string.Empty));

        if (UserManager.SupportsUserEmail)
        {
            var email = await UserManager.GetEmailAsync(user);
            if (!string.IsNullOrEmpty(email))
            {
                identity.AddClaim(new Claim(Options.ClaimsIdentity.EmailClaimType, email));
            }
        }

        if (UserManager.SupportsUserSecurityStamp)
        {
            identity.AddClaim(new Claim(
                Options.ClaimsIdentity.SecurityStampClaimType,
                await UserManager.GetSecurityStampAsync(user)));
        }

        // Deliberately omitted: UserManager.SupportsUserClaim / GetClaimsAsync(user) --
        // see class-level comment. UserRoleClaimsTransformation adds the role claim
        // afterward, once the principal reaches request-authentication time.
        return identity;
    }
}
