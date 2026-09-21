using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IRepository<Notification>> _repo = new();
    private readonly Mock<ILogger<NotificationService>> _logger = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _service = new NotificationService(_repo.Object, NewUserManager(), _logger.Object);
    }

    private static UserManager<User> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new UserManager<User>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
    }

    [Fact]
    public async Task NotifyAsync_AddsRowWithRequestedType()
    {
        Notification? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(n => added = n)
            .Returns(Task.CompletedTask);

        await _service.NotifyAsync(userId: 5, NotificationType.CaseSubmitted, caseId: 42);

        Assert.NotNull(added);
        Assert.Equal(5, added!.UserId);
        Assert.Equal(NotificationType.CaseSubmitted, added.Type);
        Assert.Equal(42, added.RelatedCaseId);
        Assert.False(added.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_SwallowsRepositoryException()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>())).ThrowsAsync(new InvalidOperationException("db down"));

        // Must not throw — the caller (e.g. a lawyer review submission) must
        // succeed even if the notification write fails.
        await _service.NotifyAsync(userId: 5, NotificationType.CaseSubmitted, caseId: 42);
    }

    [Fact]
    public async Task MarkReadAsync_ReturnsFalse_WhenCallerDoesNotOwnNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 99, IsRead = false };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.MarkReadAsync(notificationId: 1, userId: 5);

        Assert.False(ok);
        Assert.False(n.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task MarkReadAsync_MarksRead_WhenCallerOwnsNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 5, IsRead = false };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.MarkReadAsync(notificationId: 1, userId: 5);

        Assert.True(ok);
        Assert.True(n.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenCallerDoesNotOwnNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 99 };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.DeleteAsync(notificationId: 1, userId: 5);

        Assert.False(ok);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Notification>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_Deletes_WhenCallerOwnsNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 5 };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.DeleteAsync(notificationId: 1, userId: 5);

        Assert.True(ok);
        _repo.Verify(r => r.DeleteAsync(n), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_NeverReturnsAnotherUsersRows()
    {
        var rows = new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, CreatedAt = DateTime.UtcNow },
            new() { NotificationId = 2, UserId = 99, CreatedAt = DateTime.UtcNow },
        };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(rows);

        var page = await _service.GetPagedAsync(userId: 5, page: 1, pageSize: 20);

        Assert.All(page.Items, i => Assert.Equal(1, i.NotificationId));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task MarkAllSeenAsync_ClearsBadgeCount_WithoutMarkingAnythingRead()
    {
        var mine = new Notification { NotificationId = 1, UserId = 5 };
        var theirs = new Notification { NotificationId = 2, UserId = 99 };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { mine, theirs });

        await _service.MarkAllSeenAsync(userId: 5);

        Assert.True(mine.IsSeen);
        Assert.False(mine.IsRead); // My Cases' unread dot depends on IsRead
        Assert.False(theirs.IsSeen);
        Assert.Equal(0, await _service.GetUnseenCountAsync(userId: 5));
        Assert.Equal(1, await _service.GetUnreadCountAsync(userId: 5));
    }

    [Fact]
    public async Task MarkReadAsync_AlsoMarksSeen()
    {
        var n = new Notification { NotificationId = 1, UserId = 5 };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        await _service.MarkReadAsync(notificationId: 1, userId: 5);

        Assert.True(n.IsSeen);
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyUnreadForThatUser()
    {
        var rows = new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, IsRead = false },
            new() { NotificationId = 2, UserId = 5, IsRead = true },
            new() { NotificationId = 3, UserId = 99, IsRead = false },
        };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(rows);

        var count = await _service.GetUnreadCountAsync(userId: 5);

        Assert.Equal(1, count);
    }
}
