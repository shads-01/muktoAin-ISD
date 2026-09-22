using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

// FR-24 payments: request guards (Testing_Plan.md PAY-03), the gateway
// checkout response, and the gateway callbacks. PaymentService is concrete,
// so it is built for real over mocked repositories and a mocked gateway.
public class PaymentControllerTests
{
    private readonly Mock<IRepository<PaymentOrder>> _orderRepo = new();
    private readonly Mock<ICaseRepository> _caseRepo = new();
    private readonly Mock<IPaymentGatewayClient> _gateway = new();
    private readonly Mock<IPaymentGatewayResolver> _gateways = new();
    private readonly PaymentController _controller;

    public PaymentControllerTests()
    {
        // Sandbox mode unless a test says otherwise; one mocked client for all gateways.
        _gateways.Setup(r => r.ForMethod(It.IsAny<PaymentMethod>()))
            .Returns<PaymentMethod>(m => m == PaymentMethod.Bkash ? PaymentGateway.Bkash : PaymentGateway.SslCommerz);
        _gateways.Setup(r => r.Get(It.IsAny<PaymentGateway>())).Returns(_gateway.Object);
        _controller = NewController(userId: "123", role: "Citizen");
    }

    private PaymentController NewController(string? userId, string? role = null)
    {
        var paymentService = new PaymentService(
            _orderRepo.Object,
            Mock.Of<IRepository<PayoutRequest>>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            _caseRepo.Object,
            NewUserManager(),
            Mock.Of<IAdminAuditService>(),
            Mock.Of<IRepository<Notification>>(),
            _gateways.Object,
            Mock.Of<IAiTurnReservationStore>());

        var claims = new List<Claim>();
        if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));

        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
            .Returns<UrlActionContext>(c => $"https://app/Payment/{c.Action}");

        return new PaymentController(
            paymentService,
            _orderRepo.Object,
            Mock.Of<ILogger<PaymentController>>(),
            _caseRepo.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId == null ? null : "test"))
                }
            },
            Url = url.Object,
        };
    }

    private void GatewayInit(bool success) =>
        _gateway
            .Setup(g => g.InitSessionAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayCustomer?>()))
            .ReturnsAsync(success
                ? new GatewaySessionResult(true, "/GatewaySim/Checkout/xyz", null)
                : new GatewaySessionResult(false, null, "Invalid Store Id"));

    private static object? Prop(IActionResult result, string name)
    {
        var json = Assert.IsType<JsonResult>(result);
        return json.Value!.GetType().GetProperty(name)?.GetValue(json.Value);
    }

    // ---- request guards -----------------------------------------------------

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
    [InlineData(45)]   // below the minimum
    [InlineData(52)]   // not a multiple of the chat credit price
    public async Task TopUp_InvalidAmount_ReturnsBadRequest_WithoutCreatingOrder(decimal amount)
    {
        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = amount });

        Assert.IsType<BadRequestObjectResult>(result);
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    // Chat credits belong to an account; guests share the free pool only.
    [Fact]
    public async Task TopUp_Anonymous_ReturnsUnauthorized_WithoutCreatingOrder()
    {
        var controller = NewController(userId: null);

        var result = await controller.TopUp(new TopUpPaymentRequest { Amount = 100m });

        Assert.IsType<UnauthorizedObjectResult>(result);
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    // The chatbot is citizen intake; lawyers and admins never need credits.
    [Theory]
    [InlineData("Lawyer")]
    [InlineData("Admin")]
    public async Task TopUp_NonCitizen_Returns403_WithoutCreatingOrder(string role)
    {
        var controller = NewController(userId: "123", role: role);

        var result = await controller.TopUp(new TopUpPaymentRequest { Amount = 100m });

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    [Fact]
    public async Task Honorarium_WhenUserDoesNotOwnCase_ReturnsForbid()
    {
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Case { CaseId = 10, UserId = 999 });

        var result = await _controller.Honorarium(new HonorariumPaymentRequest { CaseId = 10, Amount = 500 });

        Assert.IsType<ForbidResult>(result);
    }

    // ---- checkout -----------------------------------------------------------

    [Fact]
    public async Task Honorarium_GatewaySucceeds_ReturnsGatewayUrl_AndLeavesOrderPending()
    {
        _caseRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Case { CaseId = 5, UserId = 123 });
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(new Case { CaseId = 5, UserId = 123 });
        PaymentOrder? created = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<PaymentOrder>()))
            .Callback<PaymentOrder>(o => created = o).Returns(Task.CompletedTask);
        GatewayInit(success: true);

        var result = await _controller.Honorarium(new HonorariumPaymentRequest { CaseId = 5, Amount = 500m });

        Assert.Equal(true, Prop(result, "success"));
        Assert.Equal("/GatewaySim/Checkout/xyz", Prop(result, "gatewayUrl"));
        Assert.Equal(PaymentStatus.Pending, created!.Status);
        _gateway.Verify(g => g.InitSessionAsync(It.IsAny<string>(), 500m, "Honorarium",
            "https://app/Payment/Success", "https://app/Payment/Fail", "https://app/Payment/Cancel",
            It.IsAny<GatewayCustomer?>()), Times.Once);
    }

    [Fact]
    public async Task TopUp_GatewaySucceeds_ReturnsGatewayUrl()
    {
        GatewayInit(success: true);

        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = 300m });

        Assert.Equal(true, Prop(result, "success"));
        Assert.Equal("/GatewaySim/Checkout/xyz", Prop(result, "gatewayUrl"));
    }

    [Fact]
    public async Task TopUp_GatewayFails_ReturnsFailure_WithoutGatewayUrl()
    {
        PaymentOrder? created = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<PaymentOrder>()))
            .Callback<PaymentOrder>(o => created = o).Returns(Task.CompletedTask);
        GatewayInit(success: false);

        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = 300m });

        Assert.Equal(false, Prop(result, "success"));
        Assert.Null(Prop(result, "gatewayUrl"));
        Assert.Equal(PaymentStatus.Failed, created!.Status);
    }

    [Fact]
    public async Task TopUp_Bkash_UsesBkashCallbackForEveryOutcome_AndStoresPaymentId()
    {
        PaymentOrder? created = null;
        _orderRepo.Setup(r => r.AddAsync(It.IsAny<PaymentOrder>()))
            .Callback<PaymentOrder>(o => created = o).Returns(Task.CompletedTask);
        _gateway
            .Setup(g => g.InitSessionAsync(It.IsAny<string>(), 300m, "TopUp",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayCustomer?>()))
            .ReturnsAsync(new GatewaySessionResult(true, "https://sandbox.payment.bkash.com/?paymentId=TR1", null, "TR1"));

        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = 300m, Method = PaymentMethod.Bkash });

        Assert.Equal("https://sandbox.payment.bkash.com/?paymentId=TR1", Prop(result, "gatewayUrl"));
        Assert.Equal(PaymentGateway.Bkash, created!.Gateway);
        Assert.Equal("TR1", created.GatewaySessionId);
        _gateway.Verify(g => g.InitSessionAsync(It.IsAny<string>(), 300m, "TopUp",
            "https://app/Payment/BkashCallback", "https://app/Payment/BkashCallback",
            "https://app/Payment/BkashCallback", It.IsAny<GatewayCustomer?>()), Times.Once);
    }

    [Theory]
    [InlineData(PaymentMethod.Bkash, "bkash")]
    [InlineData(PaymentMethod.Card, "card")]
    public async Task TopUp_SimulatorMode_OpensSimulatorOnChosenMethod(PaymentMethod method, string tab)
    {
        _gateways.Setup(r => r.ForMethod(It.IsAny<PaymentMethod>())).Returns(PaymentGateway.Simulator);
        GatewayInit(success: true);

        var result = await _controller.TopUp(new TopUpPaymentRequest { Amount = 300m, Method = method });

        Assert.Equal($"/GatewaySim/Checkout/xyz?method={tab}", Prop(result, "gatewayUrl"));
    }

    [Fact]
    public void PaymentRequests_BindMethodFromJsonString()
    {
        var body = System.Text.Json.JsonSerializer.Deserialize<TopUpPaymentRequest>(
            "{\"amount\":100,\"method\":\"bkash\"}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(PaymentMethod.Bkash, body!.Method);
    }

    // ---- callbacks ----------------------------------------------------------

    private PaymentOrder PendingBkashOrder(int id)
    {
        var order = new PaymentOrder
        {
            PaymentOrderId = id, Amount = 500m, Status = PaymentStatus.Pending,
            TransactionId = $"MA-{id}-x", Gateway = PaymentGateway.Bkash, GatewaySessionId = $"TR{id}",
        };
        _orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(order);
        return order;
    }

    [Fact]
    public async Task BkashCallback_Success_ExecutesAndMarksPaid()
    {
        var order = PendingBkashOrder(40);
        _gateway.Setup(g => g.ValidateAsync("TR40"))
            .ReturnsAsync(new GatewayValidationResult(true, "MA-40-x", "BKX40", "Completed", 500m, null));

        var result = await _controller.BkashCallback(40, "TR40", "success");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PaymentController.Result), redirect.ActionName);
        Assert.Equal(PaymentStatus.Paid, order.Status);
    }

    [Theory]
    [InlineData("failure", false)]
    [InlineData("cancel", true)]
    public async Task BkashCallback_FailureOrCancel_WithItsPaymentId_MarksFailed(string status, bool cancelled)
    {
        var order = PendingBkashOrder(41);

        var result = await _controller.BkashCallback(41, "TR41", status);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(cancelled, redirect.RouteValues!["cancelled"]);
        Assert.Equal(PaymentStatus.Failed, order.Status);
        _gateway.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public async Task BkashCallback_ForeignPaymentId_LeavesOrderAlone(string status)
    {
        var order = PendingBkashOrder(42);

        await _controller.BkashCallback(42, "TR-OTHER", status);

        Assert.Equal(PaymentStatus.Pending, order.Status);
        _gateway.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void BkashCallback_IsAnonymousGet()
    {
        var method = typeof(PaymentController).GetMethod(nameof(PaymentController.BkashCallback))!;

        Assert.True(method.IsDefined(typeof(AllowAnonymousAttribute), inherit: false));
        Assert.True(method.IsDefined(typeof(HttpGetAttribute), inherit: false));
    }

    [Fact]
    public async Task Success_ConfirmedPayment_MarksPaid_RedirectsToResult()
    {
        var order = new PaymentOrder { PaymentOrderId = 7, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-7-x" };
        _orderRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(order);
        _gateway.Setup(g => g.ValidateAsync("val-7"))
            .ReturnsAsync(new GatewayValidationResult(true, "MA-7-x", "BKS7", "VALID", 500m, null));

        var result = await _controller.Success(7, new GatewayCallbackForm { val_id = "val-7", tran_id = "MA-7-x" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PaymentController.Result), redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["orderId"]);
        Assert.Equal(PaymentStatus.Paid, order.Status);
    }

    [Fact]
    public async Task Success_UnconfirmedPayment_MarksFailed_RedirectsToResult()
    {
        var order = new PaymentOrder { PaymentOrderId = 8, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-8-x" };
        _orderRepo.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(order);
        _gateway.Setup(g => g.ValidateAsync("val-8"))
            .ReturnsAsync(new GatewayValidationResult(false, null, null, "FAILED", null, "Payment not valid"));

        var result = await _controller.Success(8, new GatewayCallbackForm { val_id = "val-8" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task Fail_WithMatchingTranId_MarksFailed()
    {
        var order = new PaymentOrder { PaymentOrderId = 9, Status = PaymentStatus.Pending, TransactionId = "MA-9-x" };
        _orderRepo.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(order);

        var result = await _controller.Fail(9, new GatewayCallbackForm { tran_id = "MA-9-x" });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task Cancel_WithoutTranId_LeavesOrderAlone_RedirectsWithCancelledFlag()
    {
        var order = new PaymentOrder { PaymentOrderId = 10, Status = PaymentStatus.Pending, TransactionId = "MA-10-x" };
        _orderRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(order);

        var result = await _controller.Cancel(10, new GatewayCallbackForm());

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(true, redirect.RouteValues!["cancelled"]);
        Assert.Equal(PaymentStatus.Pending, order.Status);
    }

    // The gateway posts cross-site: no antiforgery token, no auth cookie.
    [Theory]
    [InlineData(nameof(PaymentController.Success))]
    [InlineData(nameof(PaymentController.Fail))]
    [InlineData(nameof(PaymentController.Cancel))]
    public void Callbacks_AreAnonymous_AndSkipAntiforgery(string actionName)
    {
        var method = typeof(PaymentController).GetMethod(actionName)!;

        Assert.True(method.IsDefined(typeof(AllowAnonymousAttribute), inherit: false));
        Assert.True(method.IsDefined(typeof(IgnoreAntiforgeryTokenAttribute), inherit: false));
    }

    // ---- result page --------------------------------------------------------

    [Fact]
    public async Task Result_ReadsStatusFromDatabase()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(new PaymentOrder
        {
            PaymentOrderId = 11, UserId = 123, CaseId = 5, Status = PaymentStatus.Paid,
            Purpose = PaymentPurpose.Honorarium, Amount = 500m, GatewayRef = "BKS1"
        });

        var result = await _controller.Result(11);

        var model = Assert.IsType<PaymentResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Found);
        Assert.Equal(PaymentStatus.Paid, model.Status);
        Assert.Equal(5, model.CaseId);
    }

    [Fact]
    public async Task Result_OtherUsersOrder_ShowsNotFound()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(12)).ReturnsAsync(new PaymentOrder
        {
            PaymentOrderId = 12, UserId = 999, Status = PaymentStatus.Paid, Amount = 500m
        });

        var result = await _controller.Result(12);

        var model = Assert.IsType<PaymentResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.Found);
    }

    [Fact]
    public async Task Result_AnonymousOrder_IsShown_WithoutCaseLink()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(13)).ReturnsAsync(new PaymentOrder
        {
            PaymentOrderId = 13, UserId = null, CaseId = 5, Status = PaymentStatus.Failed, Amount = 500m
        });

        var result = await NewController(userId: null).Result(13, cancelled: true);

        var model = Assert.IsType<PaymentResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Found);
        Assert.True(model.Cancelled);
        Assert.Null(model.CaseId);
    }

    // ---- status -------------------------------------------------------------

    [Fact]
    public async Task Status_UnknownOrderId_ReturnsNotFound()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((PaymentOrder?)null);

        var result = await _controller.Status(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Status_WhenUserDoesNotOwnOrder_ReturnsForbid()
    {
        _orderRepo.Setup(r => r.GetByIdAsync(50)).ReturnsAsync(new PaymentOrder { PaymentOrderId = 50, UserId = 999 });

        var result = await _controller.Status(50);

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
