using Microsoft.AspNetCore.Identity;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.UnitTests.Services;

public class UserManagementServiceTests
{
    private readonly Mock<IUserStore<User>> _userStoreMock = new();
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        _userManagerMock = new Mock<UserManager<User>>(
            _userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _service = new UserManagementService(_userManagerMock.Object);
    }

    [Fact]
    public async Task GetAllUsersAsync_ReturnsProjectedUserList()
    {
        var users = new List<User>
        {
            new() { Id = 1, FullName = "Admin User", Email = "admin@muktoain.bd", Role = UserRole.Admin, AccountStatus = AccountStatus.Active },
            new() { Id = 2, FullName = "Citizen User", Email = "citizen@muktoain.bd", Role = UserRole.Citizen, AccountStatus = AccountStatus.Active }
        }.AsQueryable();

        _userManagerMock.Setup(m => m.Users).Returns(users);

        var result = (await _service.GetAllUsersAsync()).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Admin User", result[0].FullName);
        Assert.Equal("Admin", result[0].Role);
        Assert.Equal("Citizen User", result[1].FullName);
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
}
