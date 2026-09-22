using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class NotificationControllerTests
{
    private static NotificationController NewController(NotificationService service, int userId)
    {
        var controller = new NotificationController(service);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return controller;
    }

    [Fact]
    public async Task MarkRead_ReturnsForbid_ForSomeoneElsesNotification()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 99 }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = await controller.MarkRead(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsForbid_ForSomeoneElsesNotification()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 99 }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = await controller.Delete(1);

        Assert.IsType<ForbidResult>(result);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Notification>()), Times.Never);
    }

    [Fact]
    public async Task MarkAllSeen_MarksOnlyCallersNotificationsSeen_AndReturnsOk()
    {
        var mine = new Notification { NotificationId = 1, UserId = 5, IsRead = false };
        var theirs = new Notification { NotificationId = 2, UserId = 99, IsRead = false };
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { mine, theirs });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = await controller.MarkAllSeen();

        Assert.IsType<OkResult>(result);
        Assert.True(mine.IsSeen);
        Assert.False(mine.IsRead);
        Assert.False(theirs.IsSeen);
    }

    [Fact]
    public async Task Unread_ReturnsOnlyCallersOwnNotifications()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, CreatedAt = DateTime.UtcNow },
            new() { NotificationId = 2, UserId = 99, CreatedAt = DateTime.UtcNow }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = Assert.IsType<JsonResult>(await controller.Unread());
        var count = (int)result.Value!.GetType().GetProperty("count")!.GetValue(result.Value)!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MarkAndGo_RedirectsToIndex_ForExternalReturnUrl()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5 }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);
        controller.Url = NewUrlHelperStub();

        var result = await controller.MarkAndGo(1, "https://evil.com");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task MarkAndGo_RedirectsToReturnUrl_ForLocalReturnUrl()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5 }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);
        controller.Url = NewUrlHelperStub();

        var result = await controller.MarkAndGo(1, "/Case/Result?id=42");

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Case/Result?id=42", redirect.Url);
    }

    private static Microsoft.AspNetCore.Mvc.IUrlHelper NewUrlHelperStub()
    {
        var urlHelper = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelper.Setup(u => u.IsLocalUrl(It.IsAny<string>()))
            .Returns<string>(url => !string.IsNullOrEmpty(url) && url.StartsWith("/"));
        return urlHelper.Object;
    }

    private static Microsoft.AspNetCore.Identity.UserManager<User> NewUserManagerStub()
    {
        var store = new Mock<Microsoft.AspNetCore.Identity.IUserStore<User>>();
        return new Microsoft.AspNetCore.Identity.UserManager<User>(store.Object,
            Mock.Of<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>(),
            new Microsoft.AspNetCore.Identity.PasswordHasher<User>(),
            Array.Empty<Microsoft.AspNetCore.Identity.IUserValidator<User>>(),
            Array.Empty<Microsoft.AspNetCore.Identity.IPasswordValidator<User>>(),
            new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer(),
            new Microsoft.AspNetCore.Identity.IdentityErrorDescriber(), null!,
            Mock.Of<ILogger<Microsoft.AspNetCore.Identity.UserManager<User>>>());
    }
}
