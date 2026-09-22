using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Payments;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Browser;

// Browser end-to-end: real Chromium clicks through the real pages. Case page
// honorarium modal (or the chat top-up modal) -> simulated gateway checkout
// -> credentials + OTP (or cancel) -> auto-submit back to MuktoAin -> result
// page -> database. Skips when Chromium is not installed (see
// BrowserAppFixture).
public class PaymentBrowserE2ETests : IClassFixture<BrowserAppFixture>
{
    private static readonly float Timeout = 15_000;
    private readonly BrowserAppFixture _fx;

    public PaymentBrowserE2ETests(BrowserAppFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Honorarium_PaidWithBkash_EndToEnd()
    {
        Skip.If(_fx.SkipReason != null, _fx.SkipReason);
        var (citizenId, caseId) = await SeedApprovedCaseAsync();
        var page = await NewPageAsync(citizenId);

        await page.GotoAsync($"/Case/Result/{caseId}");
        await page.ClickAsync("[data-open-modal='#honorarium-modal']");
        await page.FillAsync("#honorarium-amount", "600");
        await page.ClickAsync("#btn-submit-honorarium");

        await page.WaitForURLAsync("**/GatewaySim/Checkout/**");
        Assert.Equal("৳600.00", (await page.TextContentAsync("[data-testid=sim-amount]"))!.Trim());
        await page.FillAsync("#wallet-account", "01711111111");
        await page.FillAsync("#wallet-pin", SimulatedGateway.WalletPin);
        await page.ClickAsync("#sim-pay");
        await page.FillAsync("#sim-otp", SimulatedGateway.Otp);
        await page.ClickAsync("#sim-confirm");

        await page.WaitForURLAsync("**/Payment/Result?orderId=*");
        await page.Locator("[data-payment-outcome=success]").WaitForAsync();
        var gatewayRef = (await page.TextContentAsync("[data-testid=gateway-ref]"))!.Trim();
        Assert.StartsWith("BKS", gatewayRef);

        // Back on the case page, the honorarium now shows as paid.
        await page.ClickAsync($"a[href='/Case/Result/{caseId}']");
        await page.GetByText("আইনজীবীর স্বেচ্ছাসেবী সম্মানী প্রদান করা হয়েছে").WaitForAsync();

        var order = await SingleOrderAsync(citizenId);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.Equal(600m, order.Amount);
        Assert.Equal(gatewayRef, order.GatewayRef);
    }

    [SkippableFact]
    public async Task TopUp_CardWithOneWrongCvv_ThenPaid()
    {
        Skip.If(_fx.SkipReason != null, _fx.SkipReason);
        var citizenId = await SeedCitizenAsync();
        var page = await NewPageAsync(citizenId);

        await OpenTopUpAndSubmitAsync(page, "150");

        await page.ClickAsync("label:has(input[name=method][value=card])");
        await page.FillAsync("#card-number", "4111 1111 1111 1111");
        await page.FillAsync("#card-expiry", "12/35");
        await page.FillAsync("#card-cvv", "999");
        await page.ClickAsync("#sim-pay");
        await page.Locator("[data-testid=sim-error]").WaitForAsync();

        // Retry keeps the card method selected.
        await page.FillAsync("#card-number", "4111 1111 1111 1111");
        await page.FillAsync("#card-expiry", "12/35");
        await page.FillAsync("#card-cvv", SimulatedGateway.TestCardCvv);
        await page.ClickAsync("#sim-pay");
        await page.FillAsync("#sim-otp", SimulatedGateway.Otp);
        await page.ClickAsync("#sim-confirm");

        await page.WaitForURLAsync("**/Payment/Result?orderId=*");
        await page.Locator("[data-payment-outcome=success]").WaitForAsync();
        var order = await SingleOrderAsync(citizenId);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.StartsWith("CRD", order.GatewayRef);
    }

    [SkippableFact]
    public async Task TopUp_Cancelled_EndToEnd()
    {
        Skip.If(_fx.SkipReason != null, _fx.SkipReason);
        var citizenId = await SeedCitizenAsync();
        var page = await NewPageAsync(citizenId);

        await OpenTopUpAndSubmitAsync(page, "200");
        await page.ClickAsync("#sim-cancel");

        await page.WaitForURLAsync("**/Payment/Result?orderId=*");
        await page.Locator("[data-payment-outcome=cancelled]").WaitForAsync();
        var order = await SingleOrderAsync(citizenId);
        Assert.Equal(PaymentStatus.Failed, order.Status);
        Assert.Null(order.GatewayRef);
    }

    // Recharge from the profile page: ৳100 buys 20 chat credits, shown on the
    // result page and then as the profile balance.
    [SkippableFact]
    public async Task TopUp_FromProfile_AddsChatCredits()
    {
        Skip.If(_fx.SkipReason != null, _fx.SkipReason);
        var citizenId = await SeedCitizenAsync();
        var page = await NewPageAsync(citizenId);

        await page.GotoAsync("/Account/Profile");
        Assert.Equal("0", (await page.TextContentAsync("[data-testid=chat-credits-balance]"))!.Trim());
        await page.ClickAsync("[data-testid=chat-credits-card] [data-open-modal='#topup-modal']");
        await page.FillAsync("#topup-amount", "100");
        await page.ClickAsync("#btn-submit-topup");

        await page.WaitForURLAsync("**/GatewaySim/Checkout/**");
        await page.FillAsync("#wallet-account", "01711111111");
        await page.FillAsync("#wallet-pin", SimulatedGateway.WalletPin);
        await page.ClickAsync("#sim-pay");
        await page.FillAsync("#sim-otp", SimulatedGateway.Otp);
        await page.ClickAsync("#sim-confirm");

        await page.WaitForURLAsync("**/Payment/Result?orderId=*");
        await page.Locator("[data-payment-outcome=success]").WaitForAsync();
        Assert.Equal("+20", (await page.TextContentAsync("[data-testid=chat-credits]"))!.Trim());

        await page.GotoAsync("/Account/Profile");
        Assert.Equal("20", (await page.TextContentAsync("[data-testid=chat-credits-balance]"))!.Trim());
    }

    // ---- helpers ------------------------------------------------------------

    // The top-up modal normally opens from the quota wall in the chat thread;
    // open it directly, then submit it as a user would.
    private static async Task OpenTopUpAndSubmitAsync(IPage page, string amount)
    {
        await page.GotoAsync("/");
        await page.EvaluateAsync("document.getElementById('topup-modal').classList.add('open')");
        await page.FillAsync("#topup-amount", amount);
        await page.ClickAsync("#btn-submit-topup");
        await page.WaitForURLAsync("**/GatewaySim/Checkout/**");
    }

    private async Task<IPage> NewPageAsync(int userId)
    {
        var context = await _fx.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _fx.App.BaseAddress,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestAuthHandler.UserIdHeader] = userId.ToString(),
                [TestAuthHandler.RoleHeader] = UserRole.Citizen.ToString(),
            },
        });
        context.SetDefaultTimeout(Timeout);
        // Keep the run offline and deterministic: third-party CDNs (icons,
        // fonts) are not needed for the payment flow.
        await context.RouteAsync("**/*", route =>
            route.Request.Url.StartsWith(_fx.App.BaseAddress) ? route.ContinueAsync() : route.AbortAsync());
        return await context.NewPageAsync();
    }

    private async Task<int> SeedCitizenAsync()
    {
        using var scope = _fx.App.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        return (await TestData.NewUserAsync(db, "Browser Payer", UserRole.Citizen)).Id;
    }

    // A case whose document a lawyer approved: its page offers the honorarium.
    private async Task<(int CitizenId, int CaseId)> SeedApprovedCaseAsync()
    {
        using var scope = _fx.App.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "Browser Citizen", UserRole.Citizen);
        var lawyer = await TestData.NewUserAsync(db, "Browser Lawyer", UserRole.Lawyer);
        var profile = new LawyerProfile
        {
            UserId = lawyer.Id,
            BarRegistrationNumber = $"BAR-{Guid.NewGuid():N}"[..16],
            VerificationStatus = VerificationStatus.Approved,
        };
        db.LawyerProfiles.Add(profile);
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        var doc = TestData.NewDocument(c.CaseId, DocumentStatus.Approved);
        doc.AssignedLawyerProfileId = profile.LawyerProfileId;
        db.GeneratedDocuments.Add(doc);
        await db.SaveChangesAsync();
        return (citizen.Id, c.CaseId);
    }

    private async Task<PaymentOrder> SingleOrderAsync(int userId)
    {
        using var scope = _fx.App.AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.UserId == userId);
    }
}
