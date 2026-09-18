using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Web.Auth;
using Xunit;

namespace MuktoAin.UnitTests.Auth;

public class UserRoleClaimsTransformationSuperAdminTests
{
    private static UserManager<User> NewUserManager(User? user)
    {
        var store = new Mock<IUserStore<User>>();
        var manager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
        return manager.Object;
    }

    [Fact]
    public async Task TransformAsync_SuperAdmin_AddsIsSuperAdminClaim()
    {
        var user = new User { Id = 1, Role = UserRole.Admin, IsSuperAdmin = true, FullName = "Root" };
        var transformer = new UserRoleClaimsTransformation(NewUserManager(user));
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformer.TransformAsync(principal);

        Assert.True(result.HasClaim("IsSuperAdmin", "true"));
    }

    [Fact]
    public async Task TransformAsync_RegularAdmin_DoesNotAddIsSuperAdminClaim()
    {
        var user = new User { Id = 2, Role = UserRole.Admin, IsSuperAdmin = false, FullName = "Regular" };
        var transformer = new UserRoleClaimsTransformation(NewUserManager(user));
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "2") }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformer.TransformAsync(principal);

        Assert.False(result.HasClaim(c => c.Type == "IsSuperAdmin"));
    }
}
