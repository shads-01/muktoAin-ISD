using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Identity;

namespace MuktoAin.Application.Services;

// FR-24: payments + commission ledger + lawyer payouts.
// Commission rate is a configurable constant (spec: 10%, appsettings override
// later). Orders go Pending -> gateway checkout -> Paid only after the
// gateway's server-to-server validation (ConfirmPaymentAsync); Failed on
// gateway fail/cancel; Refunded by admin with ledger reversal.
public class PaymentService
{
    public const decimal DefaultCommissionRate = 0.10m; // 10%

    // TopUp buys chat credits: ChatCreditPrice BDT per chat turn, used after
    // the free daily turns run out (AiBudgetService).
    public const decimal ChatCreditPrice = 5m;
    public const decimal MinTopUpAmount = 50m;

    private readonly IRepository<PaymentOrder> _orderRepo;
    private readonly IRepository<PayoutRequest> _payoutRepo;
    private readonly IRepository<LawyerProfile> _lawyerRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly UserManager<User> _userManager;
    private readonly IAdminAuditService _audit;
    private readonly IRepository<Notification> _notificationRepo;
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IAiTurnReservationStore _chatTurns;

    public PaymentService(
        IRepository<PaymentOrder> orderRepo,
        IRepository<PayoutRequest> payoutRepo,
        IRepository<LawyerProfile> lawyerRepo,
        ICaseRepository caseRepo,
        UserManager<User> userManager,
        IAdminAuditService audit,
        IRepository<Notification> notificationRepo,
        IPaymentGatewayResolver gateways,
        IAiTurnReservationStore chatTurns)
    {
        _orderRepo = orderRepo;
        _payoutRepo = payoutRepo;
        _lawyerRepo = lawyerRepo;
        _caseRepo = caseRepo;
        _userManager = userManager;
        _audit = audit;
        _notificationRepo = notificationRepo;
        _gateways = gateways;
        _chatTurns = chatTurns;
    }

    public async Task<PaymentOrder> CreateHonorariumOrderAsync(
        int caseId, int? userId, decimal amount)
    {
        // GetWithDocumentsAsync (not the generic GetByIdAsync) -- Documents
        // must be eager-loaded so AssignedLawyerProfileId below actually
        // resolves; the plain FindAsync-backed GetByIdAsync always leaves
        // Documents empty, silently orphaning every honorarium payment from
        // the lawyer who reviewed it.
        var c = await _caseRepo.GetWithDocumentsAsync(caseId)
                ?? throw new ArgumentException("Case not found");

        var assignedLawyerProfileId = c.Documents?
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault(d => d.AssignedLawyerProfileId.HasValue)?
            .AssignedLawyerProfileId;

        var order = new PaymentOrder
        {
            UserId = userId,
            CaseId = caseId,
            LawyerProfileId = assignedLawyerProfileId,
            Purpose = PaymentPurpose.Honorarium,
            Amount = amount,
            Commission = amount * DefaultCommissionRate,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();
        return order;
    }

    public static bool IsValidTopUpAmount(decimal amount) =>
        amount >= MinTopUpAmount && amount % ChatCreditPrice == 0;

    public async Task<PaymentOrder> CreateTopUpOrderAsync(
        int userId, decimal amount)
    {
        if (!IsValidTopUpAmount(amount))
            throw new ArgumentException(
                $"Top-up must be at least {MinTopUpAmount:0} BDT and a multiple of {ChatCreditPrice:0} BDT.");

        var order = new PaymentOrder
        {
            UserId = userId,
            CaseId = null,
            LawyerProfileId = null,
            Purpose = PaymentPurpose.TopUp,
            Amount = amount,
            ChatCredits = (int)(amount / ChatCreditPrice),
            Commission = 0m,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();
        return order;
    }

    // The gateway that serves this payment method under the current
    // Payments:Mode. The controller needs it to pick the callback URLs.
    public PaymentGateway GatewayFor(PaymentMethod method) => _gateways.ForMethod(method);

    // Starts the gateway checkout for a new Pending order. On success the
    // order carries our tran_id, which the gateway's validation must echo back
    // before ConfirmPaymentAsync marks it Paid, plus the gateway it went to.
    // If the gateway cannot start a session, the order is closed as Failed at
    // once.
    public async Task<GatewaySessionResult> CreateCheckoutSessionAsync(
        PaymentOrder order, PaymentGateway gateway, string successUrl, string failUrl, string cancelUrl)
    {
        var tranId = $"MA-{order.PaymentOrderId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        order.Gateway = gateway;
        var user = order.UserId is int userId ? await _userManager.FindByIdAsync(userId.ToString()) : null;
        var customer = user is null ? null : new GatewayCustomer(user.FullName, user.Email, user.PhoneNumber);
        var result = await _gateways.Get(gateway).InitSessionAsync(
            tranId, order.Amount, order.Purpose.ToString(), successUrl, failUrl, cancelUrl, customer);

        if (result.Success)
        {
            order.TransactionId = tranId;
            order.GatewaySessionId = result.SessionId;
        }
        else
        {
            order.Status = PaymentStatus.Failed;
        }
        await _orderRepo.SaveChangesAsync();
        return result;
    }

    // The only way an order becomes Paid. Called when the gateway posts the
    // browser back to our success URL. The callback proves nothing by itself
    // (it arrives through the browser), so the val_id is checked
    // server-to-server, and the validated tran_id and amount must both match
    // this order. Idempotent: a replayed callback on a Paid order returns true
    // without calling the gateway again (AUD-11). The order's own gateway
    // validates it. valId is the SSLCommerz/simulator val_id or the bKash
    // paymentID; a bKash paymentID must be the one issued for this order, so
    // a stranger cannot fail the order with some other payment's id.
    public async Task<bool> ConfirmPaymentAsync(int paymentOrderId, string valId)
    {
        var order = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (order == null) return false;
        if (order.Status == PaymentStatus.Paid) return true;
        if (order.Status != PaymentStatus.Pending || string.IsNullOrEmpty(order.TransactionId)) return false;
        if (order.GatewaySessionId != null && order.GatewaySessionId != valId) return false;

        var validation = await _gateways.Get(order.Gateway).ValidateAsync(valId);
        // No answer at all (gateway unreachable / timed out): the citizen may
        // well have paid, so don't fail the order; leave it Pending.
        if (!validation.Success && validation.Status == null) return false;

        var matches = validation.Success
            && validation.TransactionId == order.TransactionId
            && validation.Amount == order.Amount;

        try
        {
            if (matches)
            {
                await MarkPaidAsync(order, validation.BankTransactionId ?? valId);
            }
            else
            {
                order.Status = PaymentStatus.Failed;
                await _orderRepo.SaveChangesAsync();
            }
        }
        catch (ConcurrencyConflictException)
        {
            // A racing callback for this order saved first (AUD-4). It saw the
            // same gateway answer, so report that answer; the winner already
            // sent the notification.
        }
        return matches;
    }

    // AUD-11 guard: an order already Paid or Refunded is never processed
    // again -- GetLawyerEarningsAsync sums every Paid row, so a duplicate
    // would inflate the lawyer's balance. Order and case flag save in one
    // SaveChanges (shared DbContext), so a RowVersion conflict keeps neither.
    private async Task MarkPaidAsync(PaymentOrder o, string gatewayRef)
    {
        if (o.Status is PaymentStatus.Paid or PaymentStatus.Refunded)
            throw new InvalidOperationException(
                $"Order {o.PaymentOrderId} is already {o.Status} and cannot be marked Paid again.");

        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;

        if (o.Purpose == PaymentPurpose.Honorarium && o.CaseId.HasValue)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null) c.HonorariumPaid = true;
        }

        await _orderRepo.SaveChangesAsync();

        // Tell the lawyer only once the money is confirmed, not at order creation.
        if (o.Purpose == PaymentPurpose.Honorarium && o.LawyerProfileId.HasValue)
            await NotifyLawyerOfPaymentAsync(o);
    }

    private async Task NotifyLawyerOfPaymentAsync(PaymentOrder o)
    {
        try
        {
            var lawyerProfile = await _lawyerRepo.GetByIdAsync(o.LawyerProfileId!.Value);
            if (lawyerProfile == null) return;
            await _notificationRepo.AddAsync(new Notification
            {
                UserId = lawyerProfile.UserId,
                Type = NotificationType.PaymentReceived,
                RelatedCaseId = o.CaseId,
                RelatedLawyerProfileId = lawyerProfile.LawyerProfileId,
                CreatedAt = DateTime.UtcNow
            });
            await _notificationRepo.SaveChangesAsync();
        }
        catch
        {
            // A notification-write failure must not fail the payment confirmation.
        }
    }

    // Gateway fail/cancel callback. Only a Pending order whose tran_id (or,
    // for bKash, paymentID) the caller knows can be failed: the callback URL
    // is public, so an order id alone must not be enough to cancel someone
    // else's payment.
    public async Task<bool> MarkFailedAsync(
        int paymentOrderId, string? transactionId, string? gatewaySessionId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        var knowsOrder =
            (!string.IsNullOrEmpty(transactionId) && o?.TransactionId == transactionId)
            || (!string.IsNullOrEmpty(gatewaySessionId) && o?.GatewaySessionId == gatewaySessionId);
        if (o == null || o.Status != PaymentStatus.Pending || !knowsOrder)
        {
            return false;
        }

        o.Status = PaymentStatus.Failed;
        try
        {
            await _orderRepo.SaveChangesAsync();
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            return false; // a racing callback changed the order first
        }
    }

    // Admin refund: a ledger reversal (no gateway refund API is called).
    // AUD-11: only a Paid order can be refunded; anything else throws so the
    // admin sees why nothing happened. AUD-7: the action is audited.
    public async Task RefundAsync(int paymentOrderId, int? actingAdminId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new InvalidOperationException($"Order {paymentOrderId} not found.");
        if (o.Status != PaymentStatus.Paid)
            throw new InvalidOperationException(
                $"Order {paymentOrderId} is {o.Status}; only Paid orders can be refunded.");

        o.Status = PaymentStatus.Refunded;
        o.RefundedAt = DateTime.UtcNow;

        if (o.CaseId.HasValue && o.Purpose == PaymentPurpose.Honorarium)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null) c.HonorariumPaid = false; // ledger reversed
        }

        // TopUp: take back the credits still unused; credits already spent
        // stay spent (the order keeps counting them into the balance).
        var creditsRevoked = 0;
        if (o.Purpose == PaymentPurpose.TopUp && o.UserId.HasValue && o.ChatCredits > 0)
        {
            var balance = await _chatTurns.GetCreditBalanceAsync(o.UserId.Value);
            creditsRevoked = Math.Min(o.ChatCredits, Math.Max(0, balance));
            o.ChatCredits -= creditsRevoked;
        }

        // Order and case flag save together (shared DbContext).
        await _orderRepo.SaveChangesAsync();

        if (actingAdminId.HasValue)
        {
            await _audit.LogAdminActionAsync(
                actingAdminId.Value, "RefundOrder",
                targetEntityId: paymentOrderId,
                details: o.Purpose == PaymentPurpose.TopUp
                    ? $"{o.Amount:0.00} BDT ledger reversed, {creditsRevoked} chat credits revoked"
                    : $"{o.Amount:0.00} BDT ledger reversed");
        }
    }

    public async Task<IReadOnlyList<PaymentOrderDto>> GetOrdersAsync()
    {
        var orders = (await _orderRepo.GetAllAsync())
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        var result = new List<PaymentOrderDto>();
        foreach (var o in orders)
        {
            string? lawyerName = null;
            if (o.LawyerProfileId.HasValue)
            {
                var p = await _lawyerRepo.GetByIdAsync(o.LawyerProfileId.Value);
                if (p != null)
                {
                    var u = await _userManager.FindByIdAsync(p.UserId.ToString());
                    lawyerName = u?.FullName;
                }
            }
            result.Add(new PaymentOrderDto(
                o.PaymentOrderId, o.CaseId, o.Purpose.ToString(), o.Status.ToString(),
                o.Amount, o.Commission, o.NetToLawyer, o.GatewayRef,
                o.CreatedAt, o.PaidAt, o.RefundedAt,
                UserEmail: null, LawyerName: lawyerName));
        }
        return result;
    }

    public async Task<LawyerEarningsDto> GetLawyerEarningsAsync(int lawyerProfileId)
    {
        var all = await _orderRepo.GetAllAsync();
        var paid = all.Where(o => o.LawyerProfileId == lawyerProfileId
                               && o.Purpose == PaymentPurpose.Honorarium
                               && o.Status == PaymentStatus.Paid)
                      .OrderByDescending(o => o.PaidAt)
                      .ToList();

        var payouts = (await _payoutRepo.GetAllAsync())
            .Where(p => p.LawyerProfileId == lawyerProfileId && p.IsPaid)
            .ToList();

        var balance = paid.Sum(o => o.NetToLawyer) - payouts.Sum(p => p.Amount);

        return new LawyerEarningsDto(
            balance,
            paid.Select(o => new EarningRowDto(
                o.PaymentOrderId, o.CaseId ?? 0, o.Amount, o.Commission, o.NetToLawyer,
                o.PaidAt ?? o.CreatedAt)).ToList());
    }

    public async Task RequestPayoutAsync(int lawyerProfileId, decimal amount)
    {
        await _payoutRepo.AddAsync(new PayoutRequest
        {
            LawyerProfileId = lawyerProfileId,
            Amount = amount,
            IsPaid = false,
            RequestedAt = DateTime.UtcNow
        });
        await _payoutRepo.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<PayoutRequest>> GetPendingPayoutsAsync()
    {
        var all = await _payoutRepo.GetAllAsync();
        return all.Where(p => !p.IsPaid).OrderBy(p => p.RequestedAt).ToList();
    }

    public async Task ApprovePayoutAsync(int payoutRequestId)
    {
        var p = await _payoutRepo.GetByIdAsync(payoutRequestId);
        if (p == null) return;
        p.IsPaid = true;
        p.PaidAt = DateTime.UtcNow;
        await _payoutRepo.SaveChangesAsync();
    }
}
