using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Payments;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

// End-to-end payment flow over HTTP, with nothing of ours mocked: the real
// PaymentController, PaymentService, SimulatedGateway and its checkout pages.
// Each test walks the browser's path by hand: create order -> open the
// gateway checkout page -> submit test credentials and OTP -> follow the
// gateway's auto-submit form back to /Payment/Success|Fail|Cancel -> land on
// /Payment/Result, then checks what the database says.
public class PaymentFlowE2ETests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public PaymentFlowE2ETests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    // ---- happy paths --------------------------------------------------------

    [Fact]
    public async Task Honorarium_PaidWithBkash_MarksOrderPaid_CaseHonorariumPaid_AndNotifiesLawyer()
    {
        var (citizen, caseId, lawyerUserId) = await SeedCaseReviewedByLawyerAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);

        var (orderId, gatewayUrl) = await StartAsync(client, "/Payment/Honorarium", new { caseId, amount = 750m });
        var checkout = await Browser().GetStringAsync(gatewayUrl);
        Assert.Contains("৳750.00", checkout);

        var key = gatewayUrl.Split('/').Last();
        await PostFormAsync($"/GatewaySim/Checkout/{key}",
            ("method", "bkash"), ("account", "01711111111"), ("secret", SimulatedGateway.WalletPin));
        var autoPost = await PostFormAsync($"/GatewaySim/Otp/{key}", ("otp", SimulatedGateway.Otp));

        var landing = await FollowGatewayReturnAsync(autoPost);
        Assert.Equal($"/Payment/Result?orderId={orderId}", landing);
        var resultPage = await client.GetStringAsync(landing);
        Assert.Contains("data-payment-outcome=\"success\"", resultPage);

        await using var scope = NewScope(out var db);
        var order = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.PaymentOrderId == orderId);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.StartsWith("BKS", order.GatewayRef);
        Assert.NotNull(order.PaidAt);
        Assert.Equal(75m, order.Commission);
        Assert.True((await db.Cases.AsNoTracking().SingleAsync(c => c.CaseId == caseId)).HonorariumPaid);
        Assert.Equal(1, await db.Notifications.CountAsync(n =>
            n.UserId == lawyerUserId && n.Type == NotificationType.PaymentReceived && n.RelatedCaseId == caseId));
    }

    [Fact]
    public async Task TopUp_PaidByCard_MarksOrderPaid()
    {
        var citizen = await NewUserAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);

        var (orderId, gatewayUrl) = await StartAsync(client, "/Payment/TopUp", new { amount = 300m });
        var key = gatewayUrl.Split('/').Last();
        await PostFormAsync($"/GatewaySim/Checkout/{key}",
            ("method", "card"), ("account", "4111 1111 1111 1111"), ("secret", SimulatedGateway.TestCardCvv), ("expiry", "12/35"));
        var autoPost = await PostFormAsync($"/GatewaySim/Otp/{key}", ("otp", SimulatedGateway.Otp));
        await FollowGatewayReturnAsync(autoPost);

        var order = await GetOrderAsync(orderId);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.StartsWith("CRD", order.GatewayRef);
    }

    // ---- unhappy paths ------------------------------------------------------

    [Fact]
    public async Task WrongPinThreeTimes_FailsOrder()
    {
        var (client, orderId, key) = await StartTopUpAsync();

        await PostFormAsync($"/GatewaySim/Checkout/{key}", ("method", "nagad"), ("account", "01711111111"), ("secret", "00000"));
        var second = await PostFormAsync($"/GatewaySim/Checkout/{key}", ("method", "nagad"), ("account", "01711111111"), ("secret", "00000"));
        Assert.Contains("data-testid=\"sim-error\"", await second.Content.ReadAsStringAsync());
        var third = await PostFormAsync($"/GatewaySim/Checkout/{key}", ("method", "nagad"), ("account", "01711111111"), ("secret", "00000"));

        var landing = await FollowGatewayReturnAsync(third);
        Assert.Contains("data-payment-outcome=\"failed\"", await client.GetStringAsync(landing));
        Assert.Equal(PaymentStatus.Failed, (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task InsufficientBalance_FailsOrder()
    {
        var (_, orderId, key) = await StartTopUpAsync();

        var autoPost = await PostFormAsync($"/GatewaySim/Checkout/{key}",
            ("method", "bkash"), ("account", SimulatedGateway.InsufficientBalanceWallet), ("secret", SimulatedGateway.WalletPin));
        await FollowGatewayReturnAsync(autoPost);

        Assert.Equal(PaymentStatus.Failed, (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task Cancel_FailsOrder_AndResultSaysCancelled()
    {
        var (client, orderId, key) = await StartTopUpAsync();

        var autoPost = await PostFormAsync($"/GatewaySim/Cancel/{key}");
        var landing = await FollowGatewayReturnAsync(autoPost);

        Assert.Equal($"/Payment/Result?orderId={orderId}&cancelled=True", landing);
        Assert.Contains("data-payment-outcome=\"cancelled\"", await client.GetStringAsync(landing));
        Assert.Equal(PaymentStatus.Failed, (await GetOrderAsync(orderId)).Status);
    }

    // ---- tampering and replay -----------------------------------------------

    [Fact]
    public async Task TamperedCallbackAmount_IsIgnored_ServerValidationDecides()
    {
        var (_, orderId, key) = await StartTopUpAsync();
        await PostFormAsync($"/GatewaySim/Checkout/{key}", ("method", "bkash"), ("account", "01711111111"), ("secret", SimulatedGateway.WalletPin));
        var autoPost = await PostFormAsync($"/GatewaySim/Otp/{key}", ("otp", SimulatedGateway.Otp));

        // The browser-posted amount is attacker-controlled; only the
        // server-to-server validation counts, and it reports the real amount.
        var (url, fields) = ParseReturnForm(await autoPost.Content.ReadAsStringAsync());
        fields["amount"] = "1.00";
        await Browser().PostAsync(url, new FormUrlEncodedContent(fields));

        Assert.Equal(PaymentStatus.Paid, (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task ValIdFromAnotherOrder_CannotPayThisOrder()
    {
        // Pay a cheap order, then present its val_id on an expensive one.
        var (_, cheapOrderId, cheapKey) = await StartTopUpAsync(amount: 50m);
        await PostFormAsync($"/GatewaySim/Checkout/{cheapKey}", ("method", "bkash"), ("account", "01711111111"), ("secret", SimulatedGateway.WalletPin));
        var cheapReturn = await PostFormAsync($"/GatewaySim/Otp/{cheapKey}", ("otp", SimulatedGateway.Otp));
        var (_, cheapFields) = ParseReturnForm(await cheapReturn.Content.ReadAsStringAsync());

        var (_, dearOrderId, _) = await StartTopUpAsync(amount: 5000m);
        await Browser().PostAsync($"/Payment/Success?orderId={dearOrderId}", new FormUrlEncodedContent(cheapFields));

        Assert.Equal(PaymentStatus.Failed, (await GetOrderAsync(dearOrderId)).Status);
        Assert.Equal(PaymentStatus.Pending, (await GetOrderAsync(cheapOrderId)).Status); // its own callback never ran
    }

    [Fact]
    public async Task ReplayedSuccessCallback_KeepsOnePaidOrder_AndOneNotification()
    {
        var (citizen, caseId, lawyerUserId) = await SeedCaseReviewedByLawyerAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);
        var (orderId, gatewayUrl) = await StartAsync(client, "/Payment/Honorarium", new { caseId, amount = 500m });
        var key = gatewayUrl.Split('/').Last();
        await PostFormAsync($"/GatewaySim/Checkout/{key}", ("method", "rocket"), ("account", "01711111111"), ("secret", SimulatedGateway.WalletPin));
        var autoPost = await PostFormAsync($"/GatewaySim/Otp/{key}", ("otp", SimulatedGateway.Otp));
        var html = await autoPost.Content.ReadAsStringAsync();

        await FollowGatewayReturnAsync(html);
        await FollowGatewayReturnAsync(html); // refresh / back-button resubmit

        var order = await GetOrderAsync(orderId);
        Assert.Equal(PaymentStatus.Paid, order.Status);
        await using var scope = NewScope(out var db);
        Assert.Equal(1, await db.Notifications.CountAsync(n =>
            n.UserId == lawyerUserId && n.Type == NotificationType.PaymentReceived && n.RelatedCaseId == caseId));
    }

    [Fact]
    public async Task StrangerPostingCancel_WithoutTranId_LeavesOrderPending()
    {
        var (_, orderId, _) = await StartTopUpAsync();

        await Browser().PostAsync($"/Payment/Cancel?orderId={orderId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = "MA-guess" }));

        Assert.Equal(PaymentStatus.Pending, (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task ResultPage_OfAnotherUsersOrder_ShowsNotFound()
    {
        var (_, orderId, _) = await StartTopUpAsync();
        var stranger = await NewUserAsync();
        var client = _factory.CreateAuthenticatedClient(stranger.Id, UserRole.Citizen);

        var page = await client.GetStringAsync($"/Payment/Result?orderId={orderId}");

        Assert.Contains("data-payment-outcome=\"notfound\"", page);
    }

    [Fact]
    public async Task UnknownGatewaySession_ShowsExpiredPage()
    {
        var page = await Browser().GetStringAsync("/GatewaySim/Checkout/does-not-exist");

        Assert.Contains("Session expired", page);
    }

    // ---- helpers ------------------------------------------------------------

    // Gateway pages and callbacks are anonymous: the gateway knows nothing of
    // our users, and its cross-site POST back carries no auth cookie.
    private HttpClient Browser() => _factory.CreateClientWithoutRedirects();

    private async Task<(HttpClient Client, int OrderId, string Key)> StartTopUpAsync(decimal amount = 300m)
    {
        var citizen = await NewUserAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);
        var (orderId, gatewayUrl) = await StartAsync(client, "/Payment/TopUp", new { amount });
        return (client, orderId, gatewayUrl.Split('/').Last());
    }

    private static async Task<(int OrderId, string GatewayUrl)> StartAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsync(path, JsonContent.Create(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        return (doc.RootElement.GetProperty("orderId").GetInt32(),
                doc.RootElement.GetProperty("gatewayUrl").GetString()!);
    }

    private async Task<HttpResponseMessage> PostFormAsync(string path, params (string Key, string Value)[] fields)
    {
        var response = await Browser().PostAsync(path,
            new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect,
            $"{path} returned {(int)response.StatusCode}");
        return response;
    }

    private Task<string> FollowGatewayReturnAsync(HttpResponseMessage autoPostPage) =>
        autoPostPage.Content.ReadAsStringAsync().ContinueWith(t => FollowGatewayReturnAsync(t.Result)).Unwrap();

    // Does what the auto-submit script does in a browser, then returns where
    // our callback redirected to.
    private async Task<string> FollowGatewayReturnAsync(string autoPostHtml)
    {
        var (url, fields) = ParseReturnForm(autoPostHtml);
        var response = await Browser().PostAsync(url, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    private static (string Url, Dictionary<string, string> Fields) ParseReturnForm(string html)
    {
        var form = Regex.Match(html, "<form method=\"post\" action=\"([^\"]+)\" id=\"sim-return-form\">");
        Assert.True(form.Success, "gateway did not render its return form");
        var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\"")
            .ToDictionary(m => m.Groups[1].Value, m => WebUtility.HtmlDecode(m.Groups[2].Value));
        return (WebUtility.HtmlDecode(form.Groups[1].Value), fields);
    }

    private async Task<User> NewUserAsync()
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        return await TestData.NewUserAsync(db, "Payer", UserRole.Citizen);
    }

    // A case whose document was claimed by a lawyer, so the honorarium is
    // attributed to that lawyer.
    private async Task<(User Citizen, int CaseId, int LawyerUserId)> SeedCaseReviewedByLawyerAsync()
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "Citizen", UserRole.Citizen);
        var lawyerUser = await TestData.NewUserAsync(db, "Lawyer", UserRole.Lawyer);
        var profile = new LawyerProfile
        {
            UserId = lawyerUser.Id,
            BarRegistrationNumber = $"BAR-{Guid.NewGuid():N}"[..16],
            VerificationStatus = VerificationStatus.Approved,
        };
        db.LawyerProfiles.Add(profile);
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        var doc = TestData.NewDocument(c.CaseId, DocumentStatus.Approved);
        doc.AssignedLawyerProfileId = profile.LawyerProfileId;
        db.GeneratedDocuments.Add(doc);
        await db.SaveChangesAsync();
        return (citizen, c.CaseId, lawyerUser.Id);
    }

    private async Task<PaymentOrder> GetOrderAsync(int orderId)
    {
        await using var scope = NewScope(out var db);
        return await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.PaymentOrderId == orderId);
    }

    private AsyncServiceScope NewScope(out AppDbContext db)
    {
        var scope = _factory.Services.CreateAsyncScope();
        db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return scope;
    }
}
