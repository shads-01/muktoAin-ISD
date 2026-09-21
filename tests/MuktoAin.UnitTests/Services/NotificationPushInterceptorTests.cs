using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Web.Hubs;
using MuktoAin.Web.Services;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class NotificationPushInterceptorTests
{
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IHubContext<NotificationHub>> _hub = new();
    private IReadOnlyList<string>? _pushedTo;

    public NotificationPushInterceptorTests()
    {
        _clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(ids => _pushedTo = ids)
            .Returns(_proxy.Object);
        _hub.Setup(h => h.Clients).Returns(_clients.Object);
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .AddInterceptors(new NotificationPushInterceptor(
            _hub.Object, Mock.Of<ILogger<NotificationPushInterceptor>>()))
        .Options);

    [Fact]
    public async Task SavingANewNotification_PushesToItsOwnerOnly()
    {
        await using var db = NewContext();
        db.Set<Notification>().Add(new Notification { UserId = 7, Type = NotificationType.CaseSubmitted });

        await db.SaveChangesAsync();

        Assert.Equal(new[] { "7" }, _pushedTo);
        _proxy.Verify(p => p.SendCoreAsync(NotificationHub.ChangedEvent,
            It.Is<object?[]>(a => a.Length == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingWithoutNotificationChanges_PushesNothing()
    {
        await using var db = NewContext();
        db.Set<District>().Add(new District { DistrictId = 1, Name = "Dhaka" });

        await db.SaveChangesAsync();

        _clients.Verify(c => c.Users(It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task PushFailure_DoesNotFailTheSave()
    {
        _proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));
        await using var db = NewContext();
        db.Set<Notification>().Add(new Notification { UserId = 7, Type = NotificationType.CaseSubmitted });

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Set<Notification>().CountAsync());
    }
}
