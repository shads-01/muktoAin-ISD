using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data.Seeding;
using Xunit;

namespace MuktoAin.UnitTests.Seeding;

public class SeedDemoUsersTests
{
    // Demo citizen + lawyer already exist; only the regular admin is missing.
    [Fact]
    public async Task SeedAsync_CreatesRegularAdmin_WhoIsNotSuperAdmin()
    {
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
        userManager.Setup(m => m.FindByEmailAsync(SeedDemoUsers.CitizenEmail)).ReturnsAsync(new User());
        userManager.Setup(m => m.FindByEmailAsync(SeedDemoUsers.LawyerEmail)).ReturnsAsync(new User());
        userManager.Setup(m => m.FindByEmailAsync(SeedDemoUsers.AdminEmail)).ReturnsAsync((User?)null);
        userManager.Setup(m => m.CheckPasswordAsync(It.IsAny<User>(), It.IsAny<string>())).ReturnsAsync(true);
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), SeedDemoUsers.AdminPassword))
            .Callback<User, string>((u, _) => created = u)
            .ReturnsAsync(IdentityResult.Success);

        await SeedDemoUsers.SeedAsync(userManager.Object, Mock.Of<IRepository<LawyerProfile>>(), Mock.Of<ILogger>());

        Assert.NotNull(created);
        Assert.Equal(SeedDemoUsers.AdminEmail, created!.Email);
        Assert.Equal(UserRole.Admin, created.Role);
        Assert.False(created.IsSuperAdmin);
        Assert.Equal(AccountStatus.Active, created.AccountStatus);
    }
}
