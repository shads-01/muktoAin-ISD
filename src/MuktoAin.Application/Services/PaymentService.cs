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
    private readonly IRepository<Case> _caseRepo;
    private readonly UserManager<User> _userManager;

    public PaymentService(
        IRepository<PaymentOrder> orderRepo,
        IRepository<PayoutRequest> payoutRepo,
        IRepository<LawyerProfile> lawyerRepo,
        IRepository<Case> caseRepo,
        UserManager<User> userManager)
    {
        _orderRepo = orderRepo;
        _payoutRepo = payoutRepo;
        _lawyerRepo = lawyerRepo;
        _caseRepo = caseRepo;
        _userManager = userManager;
    }

    public async Task<PaymentOrder> CreateHonorariumOrderAsync(
        int caseId, int? userId, decimal amount)
    {
        var c = await _caseRepo.GetByIdAsync(caseId)
                ?? throw new ArgumentException("Case not found");

        var commission = Math.Round(amount * DefaultCommissionRate, 2);
        var order = new PaymentOrder
        {
            UserId = userId,
            CaseId = caseId,
            // Lawyer id resolved from the case's claimed document
            LawyerProfileId = c.Documents?.LastOrDefault()?.AssignedLawyerProfileId,
            Purpose = PaymentPurpose.Honorarium,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Commission = commission,
            NetToLawyer = amount - commission,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();

        if (order.LawyerProfileId.HasValue)
        {
            c.HonorariumPaid = true; // optimistic flag; refund resets
            await _caseRepo.SaveChangesAsync();
        }
        return order;
    }

    public async Task<PaymentOrder> CreateTopUpOrderAsync(int? userId, decimal amount)
    {
        var order = new PaymentOrder
        {
            UserId = userId,
            Purpose = PaymentPurpose.TopUp,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Commission = 0,
            NetToLawyer = 0,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();
        return order;
    }

    // Sandbox "IPN confirmed" action.
    public async Task MarkPaidAsync(int paymentOrderId, string gatewayRef)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();
    }

    public async Task MarkFailedAsync(int paymentOrderId)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (o == null) return;
        o.Status = PaymentStatus.Failed;
        await _orderRepo.SaveChangesAsync();
    }

    public async Task RefundAsync(int paymentOrderId)
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
