using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Moq;

namespace MuktoAin.UnitTests.Services;

// FR-24 payments. Orders reach Paid only through ConfirmPaymentAsync, after
// the gateway's server-to-server validation matches the order (SSL-2), and
// can never be processed twice (AUD-11).
public class PaymentServiceTests
{
    private readonly Mock<IRepository<PaymentOrder>> _orderRepo = new();
    private readonly Mock<IRepository<PayoutRequest>> _payoutRepo = new();
    private readonly Mock<IRepository<LawyerProfile>> _lawyerRepo = new();
    private readonly Mock<ICaseRepository> _caseRepo = new();
    private readonly Mock<IAdminAuditService> _auditMock = new();
    private readonly Mock<IRepository<Notification>> _notificationRepo = new();
    private readonly Mock<IPaymentGatewayClient> _gateway = new();
    private readonly Mock<IPaymentGatewayResolver> _gateways = new();
    private readonly Mock<IAiTurnReservationStore> _chatTurns = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _gateways.Setup(r => r.Get(It.IsAny<PaymentGateway>())).Returns(_gateway.Object);
        _service = new PaymentService(
            _orderRepo.Object, _payoutRepo.Object, _lawyerRepo.Object, _caseRepo.Object,
            NewUserManager(), _auditMock.Object, _notificationRepo.Object, _gateways.Object, _chatTurns.Object);
    }

    private static UserManager<User> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new UserManager<User>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
    }

    private PaymentOrder PendingOrder(int id, decimal amount = 500m, string tranId = "MA-x",
        PaymentPurpose purpose = PaymentPurpose.TopUp, int? caseId = null, int? lawyerId = null)
    {
        var order = new PaymentOrder
        {
            PaymentOrderId = id, Amount = amount, Status = PaymentStatus.Pending,
            TransactionId = tranId, Purpose = purpose, CaseId = caseId, LawyerProfileId = lawyerId
        };
        _orderRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(order);
        return order;
    }

    private void GatewayValidates(string valId, string? tranId, decimal? amount, bool success = true) =>
        _gateway.Setup(g => g.ValidateAsync(valId)).ReturnsAsync(
            new GatewayValidationResult(success, tranId, "BKS777", success ? "VALID" : "FAILED", amount, null));

    // ---- order creation -----------------------------------------------------

    // Bug fixed earlier: the case must be loaded WITH its documents, or the
    // honorarium is never attributed to the reviewing lawyer.
    [Fact]
    public async Task CreateHonorariumOrderAsync_ResolvesLawyerFromCasesClaimedDocument()
    {
        var doc = new GeneratedDocument { DocumentId = 1, CaseId = 5, AssignedLawyerProfileId = 42 };
        var c = new Case { CaseId = 5, Documents = new List<GeneratedDocument> { doc } };
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(c);

        var order = await _service.CreateHonorariumOrderAsync(caseId: 5, userId: 7, amount: 1000m);

        Assert.Equal(42, order.LawyerProfileId);
        Assert.Equal(PaymentStatus.Pending, order.Status);
    }

    [Fact]
    public async Task CreateHonorariumOrderAsync_DoesNotNotify_WhilePaymentIsPending()
    {
        var doc = new GeneratedDocument { DocumentId = 1, CaseId = 5, AssignedLawyerProfileId = 42 };
        var c = new Case { CaseId = 5, Documents = new List<GeneratedDocument> { doc } };
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(c);

        await _service.CreateHonorariumOrderAsync(caseId: 5, userId: 7, amount: 1000m);

        _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
    }

    // ---- checkout session ---------------------------------------------------

    [Fact]
    public async Task CreateCheckoutSessionAsync_GatewaySucceeds_StoresTransactionIdAndReturnsUrl()
    {
        var order = new PaymentOrder { PaymentOrderId = 1, Amount = 500m, Purpose = PaymentPurpose.Honorarium };
        string? sentTranId = null;
        _gateway
            .Setup(g => g.InitSessionAsync(It.IsAny<string>(), 500m, "Honorarium",
                "https://x/s", "https://x/f", "https://x/c", It.IsAny<GatewayCustomer?>()))
            .Callback<string, decimal, string, string, string, string, GatewayCustomer?>((t, _, _, _, _, _, _) => sentTranId = t)
            .ReturnsAsync(new GatewaySessionResult(true, "/GatewaySim/Checkout/abc", null));

        var result = await _service.CreateCheckoutSessionAsync(
            order, PaymentGateway.Simulator, "https://x/s", "https://x/f", "https://x/c");

        Assert.True(result.Success);
        Assert.Equal("/GatewaySim/Checkout/abc", result.GatewayPageUrl);
        Assert.StartsWith("MA-1-", order.TransactionId);
        Assert.Equal(order.TransactionId, sentTranId);
        Assert.Equal(PaymentStatus.Pending, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_GatewayFails_MarksOrderFailed()
    {
        var order = new PaymentOrder { PaymentOrderId = 2, Amount = 200m, Purpose = PaymentPurpose.TopUp };
        _gateway
            .Setup(g => g.InitSessionAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayCustomer?>()))
            .ReturnsAsync(new GatewaySessionResult(false, null, "Invalid Store Id"));

        var result = await _service.CreateCheckoutSessionAsync(
            order, PaymentGateway.Simulator, "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Null(order.TransactionId);
        Assert.Equal(PaymentStatus.Failed, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateCheckoutSessionAsync_Bkash_StoresGatewayAndPaymentId()
    {
        var order = new PaymentOrder { PaymentOrderId = 3, Amount = 500m, Purpose = PaymentPurpose.Honorarium };
        var bkash = new Mock<IPaymentGatewayClient>();
        bkash.Setup(g => g.InitSessionAsync(It.IsAny<string>(), 500m, "Honorarium",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GatewayCustomer?>()))
            .ReturnsAsync(new GatewaySessionResult(true, "https://sandbox.payment.bkash.com/?paymentId=TR3", null, "TR3"));
        _gateways.Setup(r => r.Get(PaymentGateway.Bkash)).Returns(bkash.Object);

        var result = await _service.CreateCheckoutSessionAsync(
            order, PaymentGateway.Bkash, "https://x/cb", "https://x/cb", "https://x/cb");

        Assert.True(result.Success);
        Assert.Equal(PaymentGateway.Bkash, order.Gateway);
        Assert.Equal("TR3", order.GatewaySessionId);
        _gateway.VerifyNoOtherCalls();
    }

    // ---- confirmation -------------------------------------------------------

    [Fact]
    public async Task ConfirmPaymentAsync_ValidatesWithTheOrdersOwnGateway()
    {
        var order = PendingOrder(30, tranId: "MA-30-x");
        order.Gateway = PaymentGateway.Bkash;
        order.GatewaySessionId = "TR30";
        var bkash = new Mock<IPaymentGatewayClient>();
        bkash.Setup(g => g.ValidateAsync("TR30"))
            .ReturnsAsync(new GatewayValidationResult(true, "MA-30-x", "BKX30", "Completed", 500m, null));
        _gateways.Setup(r => r.Get(PaymentGateway.Bkash)).Returns(bkash.Object);

        Assert.True(await _service.ConfirmPaymentAsync(30, "TR30"));

        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.Equal("BKX30", order.GatewayRef);
        _gateway.VerifyNoOtherCalls();
    }

    // A bKash order only accepts the paymentID bKash issued for it: another
    // payment's id must neither be executed nor fail this order.
    [Fact]
    public async Task ConfirmPaymentAsync_ForeignBkashPaymentId_LeavesOrderAlone()
    {
        var order = PendingOrder(31, tranId: "MA-31-x");
        order.Gateway = PaymentGateway.Bkash;
        order.GatewaySessionId = "TR31";

        Assert.False(await _service.ConfirmPaymentAsync(31, "TR-OTHER"));

        Assert.Equal(PaymentStatus.Pending, order.Status);
        _gateways.Verify(r => r.Get(It.IsAny<PaymentGateway>()), Times.Never);
    }

    [Fact]
    public async Task MarkFailedAsync_MatchingBkashPaymentId_MarksFailed()
    {
        var order = PendingOrder(32, tranId: "MA-32-x");
        order.GatewaySessionId = "TR32";

        Assert.True(await _service.MarkFailedAsync(32, transactionId: null, gatewaySessionId: "TR32"));
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task MarkFailedAsync_WrongBkashPaymentId_LeavesOrderAlone()
    {
        var order = PendingOrder(33, tranId: "MA-33-x");
        order.GatewaySessionId = "TR33";

        Assert.False(await _service.MarkFailedAsync(33, transactionId: null, gatewaySessionId: "TR-OTHER"));
        Assert.Equal(PaymentStatus.Pending, order.Status);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_ValidMatchingValidation_MarksPaidWithBankTranId()
    {
        var order = PendingOrder(3, tranId: "MA-3-x");
        GatewayValidates("val-1", "MA-3-x", 500m);

        var confirmed = await _service.ConfirmPaymentAsync(3, "val-1");

        Assert.True(confirmed);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.Equal("BKS777", order.GatewayRef);
        Assert.NotNull(order.PaidAt);
    }

    // Bug fixed: nothing used to set Case.HonorariumPaid; only refund cleared it.
    [Fact]
    public async Task ConfirmPaymentAsync_Honorarium_SetsCaseHonorariumPaid_AndNotifiesLawyerOnce()
    {
        PendingOrder(9, tranId: "MA-9-x", purpose: PaymentPurpose.Honorarium, caseId: 5, lawyerId: 42);
        var c = new Case { CaseId = 5, HonorariumPaid = false };
        _caseRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(c);
        _lawyerRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(new LawyerProfile { LawyerProfileId = 42, UserId = 88 });
        GatewayValidates("val-9", "MA-9-x", 500m);
        Notification? captured = null;
        _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(n => captured = n)
            .Returns(Task.CompletedTask);

        Assert.True(await _service.ConfirmPaymentAsync(9, "val-9"));
        Assert.True(await _service.ConfirmPaymentAsync(9, "val-9")); // replayed callback

        Assert.True(c.HonorariumPaid);
        Assert.NotNull(captured);
        Assert.Equal(88, captured!.UserId);
        Assert.Equal(NotificationType.PaymentReceived, captured.Type);
        Assert.Equal(5, captured.RelatedCaseId);
        _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_HonorariumWithoutLawyer_SkipsNotification()
    {
        PendingOrder(10, tranId: "MA-10-x", purpose: PaymentPurpose.Honorarium, caseId: 5);
        _caseRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Case { CaseId = 5 });
        GatewayValidates("val-10", "MA-10-x", 500m);

        Assert.True(await _service.ConfirmPaymentAsync(10, "val-10"));

        _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_TransactionIdMismatch_MarksFailed()
    {
        var order = PendingOrder(4, tranId: "MA-4-x");
        GatewayValidates("val-2", "SOME-OTHER-TRAN-ID", 500m);

        var confirmed = await _service.ConfirmPaymentAsync(4, "val-2");

        Assert.False(confirmed);
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_AmountMismatch_MarksFailed()
    {
        var order = PendingOrder(5, tranId: "MA-5-x");
        GatewayValidates("val-3", "MA-5-x", 100m); // tampered amount

        var confirmed = await _service.ConfirmPaymentAsync(5, "val-3");

        Assert.False(confirmed);
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_GatewaySaysInvalid_MarksFailed()
    {
        var order = PendingOrder(11, tranId: "MA-11-x");
        GatewayValidates("val-11", null, null, success: false);

        Assert.False(await _service.ConfirmPaymentAsync(11, "val-11"));
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_GatewayUnreachable_LeavesOrderPending()
    {
        var order = PendingOrder(15, tranId: "MA-15-x");
        _gateway.Setup(g => g.ValidateAsync("val-15")).ReturnsAsync(
            new GatewayValidationResult(false, null, null, null, null, "Gateway request timed out"));

        Assert.False(await _service.ConfirmPaymentAsync(15, "val-15"));
        Assert.Equal(PaymentStatus.Pending, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_AlreadyPaid_IsIdempotent_SkipsGatewayCall()
    {
        var order = PendingOrder(6);
        order.Status = PaymentStatus.Paid;

        var confirmed = await _service.ConfirmPaymentAsync(6, "val-4");

        Assert.True(confirmed);
        _gateway.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task ConfirmPaymentAsync_FailedOrRefunded_ReturnsFalse_SkipsGatewayCall(PaymentStatus status)
    {
        var order = PendingOrder(12);
        order.Status = status;

        Assert.False(await _service.ConfirmPaymentAsync(12, "val-12"));

        Assert.Equal(status, order.Status);
        _gateway.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_OrderNeverReachedGateway_ReturnsFalse()
    {
        var order = PendingOrder(13);
        order.TransactionId = null;

        Assert.False(await _service.ConfirmPaymentAsync(13, "val-13"));
        _gateway.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_UnknownOrder_ReturnsFalse()
    {
        Assert.False(await _service.ConfirmPaymentAsync(404, "val"));
    }

    // AUD-4/AUD-11: a racing confirmation already saved this order; ours loses
    // the RowVersion check. The payment is still genuine, so report success,
    // and send no second notification.
    [Fact]
    public async Task ConfirmPaymentAsync_ConcurrencyConflict_ReturnsValidationOutcome_WithoutNotifying()
    {
        PendingOrder(14, tranId: "MA-14-x", purpose: PaymentPurpose.Honorarium, caseId: 5, lawyerId: 42);
        _caseRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new Case { CaseId = 5 });
        GatewayValidates("val-14", "MA-14-x", 500m);
        _orderRepo.Setup(r => r.SaveChangesAsync()).ThrowsAsync(new ConcurrencyConflictException("stale"));

        Assert.True(await _service.ConfirmPaymentAsync(14, "val-14"));

        _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
    }

    // ---- fail / cancel ------------------------------------------------------

    [Fact]
    public async Task MarkFailedAsync_MatchingTransactionId_MarksFailed()
    {
        var order = PendingOrder(20, tranId: "MA-20-x");

        Assert.True(await _service.MarkFailedAsync(20, "MA-20-x"));
        Assert.Equal(PaymentStatus.Failed, order.Status);
    }

    // A stranger who only knows the order id cannot fail someone's payment.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MA-guess")]
    public async Task MarkFailedAsync_WrongTransactionId_LeavesOrderAlone(string? tranId)
    {
        var order = PendingOrder(21, tranId: "MA-21-x");

        Assert.False(await _service.MarkFailedAsync(21, tranId));
        Assert.Equal(PaymentStatus.Pending, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task MarkFailedAsync_PaidOrder_IsNotDowngraded()
    {
        var order = PendingOrder(22, tranId: "MA-22-x");
        order.Status = PaymentStatus.Paid;

        Assert.False(await _service.MarkFailedAsync(22, "MA-22-x"));
        Assert.Equal(PaymentStatus.Paid, order.Status);
    }

    // ---- refund (AUD-11) ----------------------------------------------------

    [Fact]
    public async Task RefundAsync_NotPaid_ThrowsInvalidOperationException()
    {
        var order = PendingOrder(3);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RefundAsync(3));

        Assert.Equal(PaymentStatus.Pending, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RefundAsync_AlreadyRefunded_Throws()
    {
        var order = PendingOrder(3);
        order.Status = PaymentStatus.Refunded;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RefundAsync(3));
    }

    [Fact]
    public async Task RefundAsync_UnknownOrder_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RefundAsync(404));
    }

    [Fact]
    public async Task RefundAsync_PaidOrder_RefundsReversesLedgerAndAudits()
    {
        var order = PendingOrder(4, purpose: PaymentPurpose.Honorarium, caseId: 10);
        order.Status = PaymentStatus.Paid;
        var c = new Case { CaseId = 10, HonorariumPaid = true };
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(c);

        await _service.RefundAsync(4, actingAdminId: 1);

        Assert.Equal(PaymentStatus.Refunded, order.Status);
        Assert.NotNull(order.RefundedAt);
        Assert.False(c.HonorariumPaid);
        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "RefundOrder", null, 4, It.IsAny<string?>()), Times.Once);
    }

    [Theory]
    [InlineData(50, 10)]
    [InlineData(100, 20)]
    [InlineData(55, 11)]
    public async Task CreateTopUpOrderAsync_SetsChatCreditsFromPrice(decimal amount, int credits)
    {
        var order = await _service.CreateTopUpOrderAsync(42, amount);

        Assert.Equal(credits, order.ChatCredits);
        Assert.Equal(PaymentPurpose.TopUp, order.Purpose);
        Assert.Equal(PaymentStatus.Pending, order.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]   // below the minimum
    [InlineData(52)]   // not a multiple of the price
    public async Task CreateTopUpOrderAsync_InvalidAmount_ThrowsWithoutCreatingOrder(decimal amount)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateTopUpOrderAsync(42, amount));

        _orderRepo.Verify(r => r.AddAsync(It.IsAny<PaymentOrder>()), Times.Never);
    }

    [Fact]
    public async Task RefundAsync_TopUp_RevokesOnlyUnusedCredits()
    {
        var order = PendingOrder(5, amount: 100m);
        order.Status = PaymentStatus.Paid;
        order.UserId = 42;
        order.ChatCredits = 20;
        _chatTurns.Setup(s => s.GetCreditBalanceAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(8);

        await _service.RefundAsync(5, actingAdminId: 1);

        // 8 unused credits taken back; the 12 already spent stay counted.
        Assert.Equal(12, order.ChatCredits);
        Assert.Equal(PaymentStatus.Refunded, order.Status);
        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "RefundOrder", null, 5, It.Is<string?>(d => d!.Contains("8 chat credits revoked"))), Times.Once);
    }

    [Fact]
    public async Task RefundAsync_TopUp_BalanceAboveOrderCredits_RevokesAllOrderCredits()
    {
        var order = PendingOrder(6, amount: 50m);
        order.Status = PaymentStatus.Paid;
        order.UserId = 42;
        order.ChatCredits = 10;
        _chatTurns.Setup(s => s.GetCreditBalanceAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(35);

        await _service.RefundAsync(6);

        Assert.Equal(0, order.ChatCredits);
    }

    [Fact]
    public async Task RefundAsync_Honorarium_DoesNotTouchChatCredits()
    {
        var order = PendingOrder(7, purpose: PaymentPurpose.Honorarium, caseId: 10);
        order.Status = PaymentStatus.Paid;
        order.UserId = 42;
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(new Case { CaseId = 10 });

        await _service.RefundAsync(7);

        _chatTurns.Verify(s => s.GetCreditBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
