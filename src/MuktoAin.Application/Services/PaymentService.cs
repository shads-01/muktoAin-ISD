using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;

namespace MuktoAin.Application.Services;

// FR-24: sandbox payments + commission ledger + lawyer payouts.
// Commission rate is a configurable constant (spec: 10%, appsettings override
// later). Nothing here talks to a real gateway — sandbox mode means orders are
// marked Paid by an explicit sandbox action (admin verify / citizen confirm
// stub), Failed on cancel, Refunded by admin with ledger reversal.
public class PaymentService
{
    public const decimal DefaultCommissionRate = 0.10m; // 10%

    private readonly IRepository<PaymentOrder> _orderRepo;
    private readonly IRepository<PayoutRequest> _payoutRepo;
    private readonly IRepository<LawyerProfile> _lawyerRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly UserManager<User> _userManager;
    private readonly IAdminAuditService _audit;
    private readonly IRepository<Notification> _notificationRepo;

    public PaymentService(
        IRepository<PaymentOrder> orderRepo,
        IRepository<PayoutRequest> payoutRepo,
        IRepository<LawyerProfile> lawyerRepo,
        ICaseRepository caseRepo,
        UserManager<User> userManager,
        IAdminAuditService audit,
        IRepository<Notification> notificationRepo)
    {
        _orderRepo = orderRepo;
        _payoutRepo = payoutRepo;
        _lawyerRepo = lawyerRepo;
        _caseRepo = caseRepo;
        _userManager = userManager;
        _audit = audit;
        _notificationRepo = notificationRepo;
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

    public async Task<PaymentOrder> CreateTopUpOrderAsync(
        int? userId, decimal amount)
    {
        var order = new PaymentOrder
        {
            UserId = userId,
            CaseId = null,
            LawyerProfileId = null,
            Purpose = PaymentPurpose.TopUp,
            Amount = amount,
            Commission = 0m,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();
        return order;
    }

    // Sandbox "IPN confirmed" action.
    // AUD-7: when an admin triggers this (AdminController.MarkOrderPaid), the
    // action is recorded; the sandbox citizen-side auto-mark passes no admin id
    // and correctly produces no admin-audit row.
    public async Task MarkPaidAsync(int paymentOrderId, string gatewayRef, int? actingAdminId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        var wasAlreadyPaid = o.Status == PaymentStatus.Paid;
        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        // Tell the lawyer only once the money is actually confirmed (not when
        // the order is merely created), and only on the first confirmation.
        if (!wasAlreadyPaid && o.Purpose == PaymentPurpose.Honorarium && o.LawyerProfileId.HasValue)
            await NotifyLawyerOfPaymentAsync(o);

        if (actingAdminId.HasValue)
        {
            await _audit.LogAdminActionAsync(
                actingAdminId.Value, "MarkOrderPaid",
                targetEntityId: paymentOrderId,
                details: $"GatewayRef {gatewayRef} · {o.Amount:0.00} BDT");
        }
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

    public async Task MarkFailedAsync(int paymentOrderId)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (o == null) return;
        o.Status = PaymentStatus.Failed;
        await _orderRepo.SaveChangesAsync();
    }

    // AUD-7: admin-triggered refunds are recorded (ledger reversal noted).
    public async Task RefundAsync(int paymentOrderId, int? actingAdminId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (o == null || o.Status != PaymentStatus.Paid) return;
        o.Status = PaymentStatus.Refunded;
        o.RefundedAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        if (o.CaseId.HasValue && o.Purpose == PaymentPurpose.Honorarium)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null)
            {
                c.HonorariumPaid = false; // ledger reversed
                await _caseRepo.SaveChangesAsync();
            }
        }

        if (actingAdminId.HasValue)
        {
            await _audit.LogAdminActionAsync(
                actingAdminId.Value, "RefundOrder",
                targetEntityId: paymentOrderId,
                details: $"{o.Amount:0.00} BDT ledger reversed");
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
