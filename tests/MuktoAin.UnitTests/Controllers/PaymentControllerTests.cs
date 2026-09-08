using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;

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
            Mock.Of<IRepository<Case>>(),
            NewUserManager());

        _controller = new PaymentController(paymentService, _orderRepo.Object, Mock.Of<ILogger<PaymentController>>());
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
}
