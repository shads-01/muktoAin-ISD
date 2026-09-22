using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.Web.Controllers;

// Hosted checkout pages of the built-in SimulatedGateway (Payments:Mode =
// Simulator). This stands in for an external gateway site, so it knows
// nothing about MuktoAin users or orders: it only drives the gateway session
// and, when the session finishes, posts the browser back to the merchant URL
// the session was created with. Anonymous, like a real gateway page.
[AllowAnonymous]
[Route("GatewaySim")]
public class GatewaySimulatorController : Controller
{
    private readonly SimulatedGateway _gateway;

    public GatewaySimulatorController(SimulatedGateway gateway) => _gateway = gateway;

    // method: preselects that tab (the merchant's method picker sends it).
    [HttpGet("Checkout/{key}")]
    public IActionResult Checkout(string key, string? method = null)
    {
        var session = _gateway.Find(key);
        if (session == null) return View("Expired");
        return session.IsFinished
            ? View("AutoPost", SimulatedGateway.CallbackFor(session))
            : View("Checkout", new GatewaySimulatorViewModel(session, PreferredMethod: method?.ToLowerInvariant()));
    }

    [HttpPost("Checkout/{key}")]
    [ValidateAntiForgeryToken]
    public IActionResult Pay(string key, string? method, string? account, string? secret, string? expiry) =>
        Handle(key, _gateway.SubmitCredentials(key, method ?? "", account ?? "", secret ?? "", expiry));

    [HttpPost("Otp/{key}")]
    [ValidateAntiForgeryToken]
    public IActionResult Otp(string key, string? otp) =>
        Handle(key, _gateway.SubmitOtp(key, otp ?? ""));

    [HttpPost("Cancel/{key}")]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(string key) =>
        Handle(key, _gateway.Cancel(key));

    private IActionResult Handle(string key, SimulatedStep step)
    {
        var session = _gateway.Find(key);
        if (step.Outcome == SimulatedStepOutcome.NotFound || session == null) return View("Expired");

        return step.Outcome switch
        {
            SimulatedStepOutcome.Completed => View("AutoPost", SimulatedGateway.CallbackFor(session)),
            SimulatedStepOutcome.Retry => View("Checkout", new GatewaySimulatorViewModel(session, step.MessageBn, step.MessageEn)),
            // Continue (next step) or Invalid (stale form): show the current step.
            _ => RedirectToAction(nameof(Checkout), new { key }),
        };
    }
}

public record GatewaySimulatorViewModel(
    SimulatedGatewaySession Session, string? ErrorBn = null, string? ErrorEn = null, string? PreferredMethod = null);
