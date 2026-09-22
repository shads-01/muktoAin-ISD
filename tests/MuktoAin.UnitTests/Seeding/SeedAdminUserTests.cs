using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Data.Seeding;
using Xunit;

namespace MuktoAin.UnitTests.Seeding;

public class SeedAdminUserTests
{
    [Fact]
    public async Task SeedAsync_NewAdmin_IsSuperAdmin()
    {
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
        userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => created = u)
            .ReturnsAsync(IdentityResult.Success);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = "admin@muktoain.bd",
            ["SeedAdmin:Password"] = "Admin@123!",
        }).Build();

        await SeedAdminUser.SeedAsync(userManager.Object, config, Mock.Of<ILogger>());

        Assert.NotNull(created);
        Assert.True(created!.IsSuperAdmin);
    }

    [Fact]
    public async Task SeedAsync_ExistingAdminWithoutFlag_IsPromotedToSuperAdmin()
    {
        // Admin seeded before scripts/12_add_user_issuperadmin.sql got IsSuperAdmin = 0.
        var existing = new User { Id = 22, Email = "admin@muktoain.bd", Role = MuktoAin.Domain.Enums.UserRole.Admin };
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
        userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(existing);
        userManager.Setup(m => m.CheckPasswordAsync(existing, It.IsAny<string>())).ReturnsAsync(true);
        userManager.Setup(m => m.UpdateAsync(existing)).ReturnsAsync(IdentityResult.Success);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = "admin@muktoain.bd",
            ["SeedAdmin:Password"] = "Admin@123!",
        }).Build();

        await SeedAdminUser.SeedAsync(userManager.Object, config, Mock.Of<ILogger>());

        Assert.True(existing.IsSuperAdmin);
        userManager.Verify(m => m.UpdateAsync(existing), Times.Once);
    }
}
