using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

// FR-24 sandbox payments: covers the invalid-amount guard on Honorarium/TopUp
// (Testing_Plan.md PAY-03). PaymentService is a concrete class with no interface
// seam, so it's constructed for real here with mocked repositories -- the guard
// clause under test returns before PaymentService is ever touched, so nothing
// on it needs to be stubbed.
public class PaymentControllerTests
{
    private readonly Mock<IRepository<PaymentOrder>> _orderRepo = new();
    private readonly PaymentController _controller;

    public PaymentControllerTests()
    {
        var paymentService = new PaymentService(
            _orderRepo.Object,
            Mock.Of<IRepository<PayoutRequest>>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            Mock.Of<ICaseRepository>(),
            NewUserManager(),
            Mock.Of<IAdminAuditService>(),
            Mock.Of<IRepository<Notification>>());

        _controller = new PaymentController(
            paymentService,
            _orderRepo.Object,
            Mock.Of<ILogger<PaymentController>>(),
            Mock.Of<ICaseRepository>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Honorarium_ZeroOrNegativeAmount_ReturnsBadRequest_WithoutCreatingOrder(decimal amount)
    {
        var result = await _controller.Honorarium(new HonorariumPaymentRequest { CaseId = 5, Amount = amount });

        Assert.IsType<BadRequestObjectResult>(result);
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public async Task TopUp_ZeroOrNegativeAmount_ReturnsBadRequest_WithoutCreatingOrder(decimal amount)
    {
        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = amount });

        Assert.IsType<BadRequestObjectResult>(result);
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    [Fact]
    public async Task Status_UnknownOrderId_ReturnsNotFound()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((PaymentOrder?)null);

        var result = await _controller.Status(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Honorarium_WhenUserDoesNotOwnCase_ReturnsForbid()
    {
        var caseRepo = new Mock<ICaseRepository>();
        caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Case
        {
            CaseId = 10,
            UserId = 999 // Different user
        });

        var paymentService = new PaymentService(
            _orderRepo.Object,
            Mock.Of<IRepository<PayoutRequest>>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            caseRepo.Object,
            NewUserManager(),
            Mock.Of<IAdminAuditService>(),
            Mock.Of<IRepository<Notification>>());

        var controller = new PaymentController(
            paymentService,
            _orderRepo.Object,
            Mock.Of<ILogger<PaymentController>>(),
            caseRepo.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "123")
                    }))
                }
            }
        };

        var result = await controller.Honorarium(new HonorariumPaymentRequest { CaseId = 10, Amount = 500 });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Status_WhenUserDoesNotOwnOrder_ReturnsForbid()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(50)).ReturnsAsync(new PaymentOrder
        {
            PaymentOrderId = 50,
            UserId = 999 // Different user
        });

        // PaymentService is a concrete class with required ctor params, so
        // Mock.Of<PaymentService>() can't proxy it — build it for real with
        // mocked repositories (Status never touches PaymentService on the
        // Forbid path).
        var paymentService = new PaymentService(
            _orderRepo.Object,
            Mock.Of<IRepository<PayoutRequest>>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            Mock.Of<ICaseRepository>(),
            NewUserManager(),
            Mock.Of<IAdminAuditService>(),
            Mock.Of<IRepository<Notification>>());

        var controller = new PaymentController(
            paymentService,
            _orderRepo.Object,
            Mock.Of<ILogger<PaymentController>>(),
            Mock.Of<ICaseRepository>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "123")
                    }))
                }
            }
        };

        var result = await controller.Status(50);

        Assert.IsType<ForbidResult>(result);
    }

    private static UserManager<User> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            null!,
            Mock.Of<ILogger<UserManager<User>>>()).Object;
    }

    // AUD-1 (CA5391): see ChatControllerTests for rationale.
    [Theory]
    [InlineData(nameof(PaymentController.Honorarium))]
    [InlineData(nameof(PaymentController.TopUp))]
    public void PostActions_CarryValidateAntiForgeryToken(string actionName)
    {
        var method = typeof(PaymentController).GetMethod(actionName)!;

        Assert.True(
            method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: false).Any(),
            $"PaymentController.{actionName} is missing [ValidateAntiForgeryToken].");
    }
}
