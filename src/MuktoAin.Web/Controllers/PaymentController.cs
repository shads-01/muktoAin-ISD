using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Web.Models;

namespace MuktoAin.Web.Controllers;

// FR-24 payments. Honorarium/TopUp create a Pending order and hand back the
// checkout URL of the gateway serving the chosen method; the browser pays
// there and comes back to Success/Fail/Cancel (SSLCommerz, simulator) or
// BkashCallback (bKash). Only success can lead to Paid, and only after
// PaymentService.ConfirmPaymentAsync validates the payment server-to-server.
[ApiController]
[Route("[controller]/[action]")]
public class PaymentController : Controller
{
    private readonly PaymentService _paymentService;
    private readonly IRepository<PaymentOrder> _orderRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly ILogger<PaymentController> _logger;

    // AUD-10: required dependency — the old optional `caseRepo = null` made
    // the Honorarium case-ownership check silently optional.
    public PaymentController(
        PaymentService paymentService,
        IRepository<PaymentOrder> orderRepo,
        ILogger<PaymentController> logger,
        ICaseRepository caseRepo)
    {
        _paymentService = paymentService;
        _orderRepo = orderRepo;
        _logger = logger;
        _caseRepo = caseRepo;
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    [HttpPost]
    [EnableRateLimiting("payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Honorarium([FromBody] HonorariumPaymentRequest body)
    {
        if (body == null || body.CaseId <= 0 || body.Amount <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid case ID or amount" });
        }

        var userId = CurrentUserId();

        var caseEntity = await _caseRepo.GetByIdAsync(body.CaseId);
        if (caseEntity == null)
        {
            return NotFound(new { success = false, message = "Case not found" });
        }

        var isAdmin = User.IsInRole("Admin");
        var isOwner = (caseEntity.UserId.HasValue && caseEntity.UserId == userId)
                      || (!caseEntity.UserId.HasValue && !string.IsNullOrEmpty(body.TrackingCode) && caseEntity.AnonymousTrackingCode == body.TrackingCode);
        if (!isAdmin && !isOwner)
        {
            return Forbid();
        }

        try
        {
            var order = await _paymentService.CreateHonorariumOrderAsync(body.CaseId, userId, body.Amount);
            return await StartCheckoutAsync(order, body.Method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start honorarium payment for case {CaseId}", body.CaseId);
            return PaymentError();
        }
    }

    [HttpPost]
    [EnableRateLimiting("payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TopUp([FromBody] TopUpPaymentRequest body)
    {
        // Chat credits belong to an account; guests share one free pool.
        var userId = CurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new
            {
                success = false,
                message = "টপ-আপ করতে লগ ইন করুন / Please log in to top up."
            });
        }

        // The chatbot is citizen intake; lawyers and admins never need credits
        // (an admin top-up would also put fake revenue in the ledger).
        if (!User.IsInRole(nameof(UserRole.Citizen)))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                success = false,
                message = "শুধু নাগরিকরা চ্যাট ক্রেডিট কিনতে পারেন / Only citizens can buy chat credits."
            });
        }

        if (body == null || !PaymentService.IsValidTopUpAmount(body.Amount))
        {
            return BadRequest(new
            {
                success = false,
                message = $"টপ-আপ কমপক্ষে ৳{PaymentService.MinTopUpAmount:0} এবং ৳{PaymentService.ChatCreditPrice:0}-এর গুণিতক হতে হবে / " +
                          $"Top-up must be at least ৳{PaymentService.MinTopUpAmount:0} and a multiple of ৳{PaymentService.ChatCreditPrice:0}."
            });
        }

        try
        {
            var order = await _paymentService.CreateTopUpOrderAsync(userId.Value, body.Amount);
            return await StartCheckoutAsync(order, body.Method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start top-up payment");
            return PaymentError();
        }
    }

    private async Task<IActionResult> StartCheckoutAsync(PaymentOrder order, PaymentMethod method)
    {
        var routeValues = new { orderId = order.PaymentOrderId };
        var gateway = _paymentService.GatewayFor(method);
        GatewaySessionResult session;
        if (gateway == PaymentGateway.Bkash)
        {
            // bKash has a single callback URL for every outcome.
            var callback = Url.Action(nameof(BkashCallback), "Payment", routeValues, Request.Scheme)!;
            session = await _paymentService.CreateCheckoutSessionAsync(order, gateway, callback, callback, callback);
        }
        else
        {
            session = await _paymentService.CreateCheckoutSessionAsync(
                order,
                gateway,
                Url.Action(nameof(Success), "Payment", routeValues, Request.Scheme)!,
                Url.Action(nameof(Fail), "Payment", routeValues, Request.Scheme)!,
                Url.Action(nameof(Cancel), "Payment", routeValues, Request.Scheme)!);
        }

        if (!session.Success)
        {
            _logger.LogWarning("Gateway session init failed for order {OrderId}: {Error}",
                order.PaymentOrderId, session.ErrorMessage);
            return Json(new
            {
                success = false,
                orderId = order.PaymentOrderId,
                message = "পেমেন্ট গেটওয়ে চালু করা যায়নি, পরে আবার চেষ্টা করুন / The payment gateway could not be started. Please try again later."
            });
        }

        // The simulator stands in for both methods; open it on the chosen one.
        var gatewayUrl = gateway == PaymentGateway.Simulator
            ? $"{session.GatewayPageUrl}?method={(method == PaymentMethod.Bkash ? "bkash" : "card")}"
            : session.GatewayPageUrl;
        return Json(new { success = true, orderId = order.PaymentOrderId, gatewayUrl });
    }

    private ObjectResult PaymentError() => StatusCode(500, new
    {
        success = false,
        message = "পেমেন্ট প্রক্রিয়াকরণে সমস্যা হয়েছে / The payment could not be processed.",
        error = ApiErrors.PaymentFailedEn,
        errorBn = ApiErrors.PaymentFailedBn,
    });

    // ---- gateway callbacks --------------------------------------------------
    // The gateway posts the browser here cross-site: no antiforgery token can
    // exist and the SameSite=Lax auth cookie is not sent, so these are
    // anonymous. Trust comes from PaymentService (server-to-server validation
    // for Success; tran_id match for Fail/Cancel), never from the caller.

    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Success([FromQuery] int orderId, [FromForm] GatewayCallbackForm form)
    {
        var confirmed = await _paymentService.ConfirmPaymentAsync(orderId, form.val_id ?? "");
        if (!confirmed)
        {
            _logger.LogWarning("Payment {OrderId} not confirmed by the gateway (tran_id {TranId})", orderId, form.tran_id);
        }
        return RedirectToAction(nameof(Result), new { orderId });
    }

    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Fail([FromQuery] int orderId, [FromForm] GatewayCallbackForm form)
    {
        await _paymentService.MarkFailedAsync(orderId, form.tran_id);
        return RedirectToAction(nameof(Result), new { orderId });
    }

    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Cancel([FromQuery] int orderId, [FromForm] GatewayCallbackForm form)
    {
        await _paymentService.MarkFailedAsync(orderId, form.tran_id);
        return RedirectToAction(nameof(Result), new { orderId, cancelled = true });
    }

    // bKash sends the browser back here (GET) for every outcome, with the
    // paymentID it issued at create time. success -> execute + validate;
    // failure/cancel -> Failed, but only for the paymentID stored on the order.
    [HttpGet("{orderId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> BkashCallback(int orderId, [FromQuery] string? paymentID, [FromQuery] string? status)
    {
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            var confirmed = await _paymentService.ConfirmPaymentAsync(orderId, paymentID ?? "");
            if (!confirmed)
            {
                _logger.LogWarning("bKash payment {OrderId} not confirmed (paymentID {PaymentId})", orderId, paymentID);
            }
            return RedirectToAction(nameof(Result), new { orderId });
        }

        await _paymentService.MarkFailedAsync(orderId, transactionId: null, gatewaySessionId: paymentID);
        var cancelled = string.Equals(status, "cancel", StringComparison.OrdinalIgnoreCase);
        return RedirectToAction(nameof(Result), new { orderId, cancelled });
    }

    // Where the citizen lands after the gateway. The outcome is read from the
    // database, never from the query string. `cancelled` only picks the
    // wording for a Failed order. Orders of another signed-in user show as
    // not found.
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Result([FromQuery] int orderId, [FromQuery] bool cancelled = false)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        var userId = CurrentUserId();
        var visible = order != null
                      && (!order.UserId.HasValue || order.UserId == userId || User.IsInRole("Admin"));

        return View(visible
            ? new PaymentResultViewModel
            {
                Found = true,
                OrderId = order!.PaymentOrderId,
                Status = order.Status,
                Purpose = order.Purpose,
                Amount = order.Amount,
                GatewayRef = order.GatewayRef,
                Cancelled = cancelled,
                CaseId = order.UserId.HasValue ? order.CaseId : null,
                ChatCredits = order.Purpose == PaymentPurpose.TopUp ? order.ChatCredits : 0,
            }
            : new PaymentResultViewModel { Found = false, OrderId = orderId });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Status(int id)
    {
        var order = await _orderRepo.GetByIdAsync(id);
        if (order == null)
        {
            return NotFound(new { success = false, message = "Order not found" });
        }

        var userId = CurrentUserId();
        var isAdmin = User.IsInRole("Admin");
        var isOrderOwner = order.UserId.HasValue && order.UserId == userId;
        if (!isAdmin && !isOrderOwner)
        {
            return Forbid();
        }

        return Json(new
        {
            success = true,
            orderId = order.PaymentOrderId,
            purpose = order.Purpose.ToString(),
            status = order.Status.ToString(),
            amount = order.Amount,
            netToLawyer = order.NetToLawyer,
            commission = order.Commission,
            gatewayRef = order.GatewayRef,
            paidAt = order.PaidAt
        });
    }
}

public class HonorariumPaymentRequest
{
    public int CaseId { get; set; }
    public decimal Amount { get; set; }
    public string? TrackingCode { get; set; }
    // "card" (default) or "bkash".
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
}

public class TopUpPaymentRequest
{
    public decimal Amount { get; set; }
    // "card" (default) or "bkash".
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
}

// Fields the gateway posts to success_url / fail_url / cancel_url
// (SSLCommerz names; the built-in simulator posts the same ones).
public class GatewayCallbackForm
{
    public string? tran_id { get; set; }
    public string? val_id { get; set; }
    public string? amount { get; set; }
    public string? status { get; set; }
    public string? bank_tran_id { get; set; }
}

public class PaymentResultViewModel
{
    public bool Found { get; set; }
    public int OrderId { get; set; }
    public PaymentStatus Status { get; set; }
    public PaymentPurpose Purpose { get; set; }
    public decimal Amount { get; set; }
    public string? GatewayRef { get; set; }
    public bool Cancelled { get; set; }
    // Link back to the case; only for signed-in owners (an anonymous case
    // page needs its tracking code, which this page never shows).
    public int? CaseId { get; set; }
    // TopUp: chat credits the order added.
    public int ChatCredits { get; set; }
}
