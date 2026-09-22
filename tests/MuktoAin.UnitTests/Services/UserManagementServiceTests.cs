using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class UserManagementServiceTests
{
    private static Mock<UserManager<User>> NewUserManagerMock()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
    }

    private readonly Mock<IUserStore<User>> _userStoreMock = new();
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IAdminAuditService> _auditMock = new();
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        _userManagerMock = new Mock<UserManager<User>>(
            _userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _service = new UserManagementService(_userManagerMock.Object, _auditMock.Object);
    }

    [Fact]
    public async Task GetAllUsersAsync_ReturnsProjectedUserList()
    {
        var users = new List<User>
        {
            new() { Id = 1, FullName = "Admin User", Email = "admin@muktoain.bd", Role = UserRole.Admin, AccountStatus = AccountStatus.Active, IsSuperAdmin = true },
            new() { Id = 2, FullName = "Citizen User", Email = "citizen@muktoain.bd", Role = UserRole.Citizen, AccountStatus = AccountStatus.Active, IsSuperAdmin = false }
        }.AsQueryable();

        _userManagerMock.Setup(m => m.Users).Returns(users);

        var result = (await _service.GetAllUsersAsync()).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Admin User", result[0].FullName);
        Assert.Equal("Admin", result[0].Role);
        Assert.True(result[0].IsSuperAdmin);
        Assert.Equal("Citizen User", result[1].FullName);
        Assert.False(result[1].IsSuperAdmin);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenUserNotFound_ReturnsFalse()
    {
        _userManagerMock.Setup(m => m.FindByIdAsync("99")).ReturnsAsync((User?)null);

        var result = await _service.SetAccountStatusAsync(99, AccountStatus.Suspended, 1);

        Assert.False(result);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenTargetIsAdmin_ReturnsFalse()
    {
        var adminUser = new User { Id = 2, Role = UserRole.Admin };
        _userManagerMock.Setup(m => m.FindByIdAsync("2")).ReturnsAsync(adminUser);

        var result = await _service.SetAccountStatusAsync(2, AccountStatus.Suspended, 1);

        Assert.False(result);
        _userManagerMock.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenAdminSuspendsSelf_ReturnsFalse()
    {
        var user = new User { Id = 1, Role = UserRole.Citizen };
        _userManagerMock.Setup(m => m.FindByIdAsync("1")).ReturnsAsync(user);

        var result = await _service.SetAccountStatusAsync(1, AccountStatus.Suspended, 1);

        Assert.False(result);
        _userManagerMock.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenValidCitizen_UpdatesStatusAndSecurityStampOnSuspension()
    {
        var user = new User { Id = 5, Role = UserRole.Citizen, AccountStatus = AccountStatus.Active };
        _userManagerMock.Setup(m => m.FindByIdAsync("5")).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(m => m.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _service.SetAccountStatusAsync(5, AccountStatus.Suspended, 1);

        Assert.True(result);
        Assert.Equal(AccountStatus.Suspended, user.AccountStatus);
        _userManagerMock.Verify(m => m.UpdateAsync(user), Times.Once);
        _userManagerMock.Verify(m => m.UpdateSecurityStampAsync(user), Times.Once);
    }

    [Fact]
    public async Task CreateAdminAsync_CreatesAdminWithRequestedTier_AndSetsCreatedByAdminId()
    {
        var userManager = NewUserManagerMock();
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => { u.Id = 5; created = u; })
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>())).ReturnsAsync("tok");
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var result = await service.CreateAdminAsync(
            "New Admin", "newadmin@example.com", asSuperAdmin: false, actingSuperAdminId: 1);

        Assert.NotNull(created);
        Assert.Equal(UserRole.Admin, created!.Role);
        Assert.False(created.IsSuperAdmin);
        Assert.Equal(1, created.CreatedByAdminId);
        Assert.Equal(5, result.CreatedUser.Id);
        Assert.Contains("tok", result.PasswordResetUrl);
    }

    [Fact]
    public async Task CreateAdminAsync_CanCreateAnotherSuperAdmin()
    {
        var userManager = NewUserManagerMock();
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => { u.Id = 6; created = u; })
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>())).ReturnsAsync("tok");
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        await service.CreateAdminAsync("New Super", "super2@example.com", asSuperAdmin: true, actingSuperAdminId: 1);

        Assert.True(created!.IsSuperAdmin);
    }

    [Fact]
    public async Task CreateAdminAsync_DuplicateEmail_ThrowsWithIdentityErrorsIntact()
    {
        var userManager = NewUserManagerMock();
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "already used" }));
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ex = await Assert.ThrowsAsync<IdentityCreationFailedException>(() =>
            service.CreateAdminAsync("Dup", "dup@example.com", asSuperAdmin: false, actingSuperAdminId: 1));

        Assert.Single(ex.Errors);
        Assert.Equal("DuplicateEmail", ex.Errors.First().Code);
    }

    [Fact]
    public async Task SetAdminStatusAsync_ReturnsFalse_WhenTargetIsSuperAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = true, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ok = await service.SetAdminStatusAsync(9, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.False(ok);
        Assert.Equal(AccountStatus.Active, target.AccountStatus);
        userManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SetAdminStatusAsync_ReturnsFalse_WhenTargetIsSelf()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 1, Role = UserRole.Admin, IsSuperAdmin = true, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("1")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ok = await service.SetAdminStatusAsync(1, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.False(ok);
    }

    [Fact]
    public async Task SetAdminStatusAsync_Succeeds_WhenTargetIsRegularAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = false, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ok = await service.SetAdminStatusAsync(9, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.True(ok);
        Assert.Equal(AccountStatus.Suspended, target.AccountStatus);
    }

    [Fact]
    public async Task PromoteToSuperAdminAsync_ReturnsFalse_WhenAlreadySuperAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = true };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ok = await service.PromoteToSuperAdminAsync(9, actingSuperAdminId: 1);

        Assert.False(ok);
    }

    [Fact]
    public async Task PromoteToSuperAdminAsync_Succeeds_WhenTargetIsRegularAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = false };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        var service = new UserManagementService(userManager.Object, Mock.Of<IAdminAuditService>());

        var ok = await service.PromoteToSuperAdminAsync(9, actingSuperAdminId: 1);

        Assert.True(ok);
        Assert.True(target.IsSuperAdmin);
    }

    // AUD-7: the audit row records WHO flipped the status, on WHOM, to WHAT.
    [Fact]
    public async Task SetAccountStatusAsync_WhenSuspensionSucceeds_LogsSuspendAudit()
    {
        var user = new User { Id = 5, Role = UserRole.Citizen, AccountStatus = AccountStatus.Active };
        _userManagerMock.Setup(m => m.FindByIdAsync("5")).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(m => m.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        await _service.SetAccountStatusAsync(5, AccountStatus.Suspended, actingAdminId: 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "SuspendUser", 5, null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenReactivationSucceeds_LogsUnsuspendAudit()
    {
        var user = new User { Id = 5, Role = UserRole.Citizen, AccountStatus = AccountStatus.Suspended };
        _userManagerMock.Setup(m => m.FindByIdAsync("5")).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        await _service.SetAccountStatusAsync(5, AccountStatus.Active, actingAdminId: 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "UnsuspendUser", 5, null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenForbidden_LogsNoAudit()
    {
        var adminUser = new User { Id = 2, Role = UserRole.Admin };
        _userManagerMock.Setup(m => m.FindByIdAsync("2")).ReturnsAsync(adminUser);

        await _service.SetAccountStatusAsync(2, AccountStatus.Suspended, 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>()),
            Times.Never);
    }
}
