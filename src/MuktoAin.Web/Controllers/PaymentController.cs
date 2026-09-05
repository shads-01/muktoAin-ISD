using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Web.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class PaymentController : Controller
{
    private readonly PaymentService _paymentService;
    private readonly IRepository<PaymentOrder> _orderRepo;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        PaymentService paymentService,
        IRepository<PaymentOrder> orderRepo,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _orderRepo = orderRepo;
        _logger = logger;
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    [HttpPost]
    public async Task<IActionResult> Honorarium([FromBody] HonorariumPaymentRequest body)
    {
        if (body == null || body.CaseId <= 0 || body.Amount <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid case ID or amount" });
        }

        try
        {
            var userId = CurrentUserId();
            var order = await _paymentService.CreateHonorariumOrderAsync(body.CaseId, userId, body.Amount);

            // In sandbox mode, immediately mark Paid with sandbox reference
            var sandboxRef = $"SANDBOX-HON-{order.PaymentOrderId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            await _paymentService.MarkPaidAsync(order.PaymentOrderId, sandboxRef);

            return Json(new
            {
                success = true,
                orderId = order.PaymentOrderId,
                amount = order.Amount,
                status = "Paid",
                gatewayRef = sandboxRef,
                message = "সম্মানী সফলভাবে প্রদান করা হয়েছে / Honorarium paid successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process honorarium payment for case {CaseId}", body.CaseId);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> TopUp([FromBody] TopUpPaymentRequest body)
    {
        if (body == null || body.Amount <= 0)
        {
            return BadRequest(new { success = false, message = "Invalid top-up amount" });
        }

        try
        {
            var userId = CurrentUserId();
            var order = await _paymentService.CreateTopUpOrderAsync(userId, body.Amount);

            // In sandbox mode, immediately mark Paid with sandbox reference
            var sandboxRef = $"SANDBOX-TOP-{order.PaymentOrderId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            await _paymentService.MarkPaidAsync(order.PaymentOrderId, sandboxRef);

            return Json(new
            {
                success = true,
                orderId = order.PaymentOrderId,
                amount = order.Amount,
                status = "Paid",
                gatewayRef = sandboxRef,
                message = "টপ-আপ সফল হয়েছে (স্যান্ডবক্স) / Top-up successful (Sandbox stub)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process top-up payment");
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Status(int id)
    {
        var order = await _orderRepo.GetByIdAsync(id);
        if (order == null)
        {
            return NotFound(new { success = false, message = "Order not found" });
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
}

public class TopUpPaymentRequest
{
    public decimal Amount { get; set; }
}
