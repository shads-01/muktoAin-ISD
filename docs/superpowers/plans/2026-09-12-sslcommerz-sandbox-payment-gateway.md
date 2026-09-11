# SSLCommerz Sandbox Payment Gateway Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the instant-`Paid` sandbox stub in `PaymentController`/`PaymentService` with a real SSLCommerz sandbox integration, so Honorarium and Top-Up payments actually redirect through SSLCommerz's sandbox checkout (real bKash/Rocket/Nagad/card test screens) and get confirmed server-side instead of being marked paid unconditionally.

**Architecture:** A new `IPaymentGatewayClient` port (Domain) with an `SslCommerzGatewayClient` adapter (Infrastructure) wraps SSLCommerz's Session-Init and Validation APIs behind two calls: `InitSessionAsync` (get a `GatewayPageUrl` to redirect the browser to) and `ValidateAsync` (server-to-server confirmation after SSLCommerz redirects back). `PaymentService` gains `CreateCheckoutSessionAsync`/`ConfirmPaymentAsync` on top of the existing `PaymentOrder` ledger; `PaymentController` gains `Success`/`Fail`/`Cancel`/`Result` actions and its `Honorarium`/`TopUp` actions now return a `gatewayUrl` for the frontend to navigate to instead of an instant success toast.

**Tech Stack:** ASP.NET Core MVC (.NET 8), `HttpClient` (typed client via `IHttpClientFactory`), `System.Net.Http.Json`, xUnit + Moq (existing test stack), manual idempotent SQL scripts (no EF migrations in this repo).

**Spec:** No separate spec/design doc was written — the design was proposed and approved inline in chat during brainstorming (SSLCommerz chosen over a direct single-provider bKash sandbox and over a fully custom mock, because it is the only option that gives real sandbox screens for bKash **and** Rocket **and** Nagad in one integration, is free/self-service, and needs no business documents). This plan is the durable record of that design; the summary above and the task rationale below carry the decisions a spec would otherwise hold.

## Global Constraints

- **Never commit, stage, push, or amend git history** (`AGENTS.md` §6 — the human, Shads, is the sole committer). Every task below ends with running tests, not with a git step. Leave all changes in the working tree.
- **Clean Architecture boundaries** (`AGENTS.md` §3): `MuktoAin.Domain` has zero external dependencies — the gateway *interface* and its result records live there; the SSLCommerz-specific HTTP implementation lives in `MuktoAin.Infrastructure`.
- **No EF Core migrations** — schema changes are hand-written idempotent SQL scripts under `scripts/`, numbered sequentially, safe to re-run (see `scripts/10_fix_case_title_column_width.sql` for the pattern this repo uses).
- **Bilingual user-facing text** — every new citizen-facing string needs a Bangla and an English form, matching the existing `data-bn`/`data-en` pattern in `Result.cshtml` and the bilingual literal strings already in `PaymentController`.
- **No real secrets committed** — `SslCommerz:StoreId`/`StorePassword` are left as empty placeholders in `appsettings.Development.json` (this repo already commits other dev-only keys in that file, e.g. `Gemini:ApiKeys`, but those are pre-existing choices, not ones to extend here — SSLCommerz sandbox credentials are personal to whoever registers, so they stay blank until filled in locally).
- **Update `plans/Dependency_plan.md`** on completion of the whole feature (project-wide mandatory rule in this repo's root `CLAUDE.md`) — this is the last step of the last task, not a per-task step.
- Amounts are BDT `decimal`, matching every existing `PaymentOrder` field.

---

### Task 1: `IPaymentGatewayClient` port + `SslCommerzGatewayClient` adapter

**Files:**
- Create: `src/MuktoAin.Domain/Interfaces/Services/IPaymentGatewayClient.cs`
- Create: `src/MuktoAin.Infrastructure/Payments/SslCommerzOptions.cs`
- Create: `src/MuktoAin.Infrastructure/Payments/SslCommerzGatewayClient.cs`
- Test: `tests/MuktoAin.UnitTests/Infrastructure/SslCommerzGatewayClientTests.cs`

**Interfaces:**
- Produces: `MuktoAin.Domain.Interfaces.Services.IPaymentGatewayClient` with
  `Task<GatewaySessionResult> InitSessionAsync(string transactionId, decimal amount, string purposeLabel, string successUrl, string failUrl, string cancelUrl)`
  and `Task<GatewayValidationResult> ValidateAsync(string valId)`.
  `GatewaySessionResult(bool Success, string? GatewayPageUrl, string? ErrorMessage)`.
  `GatewayValidationResult(bool Success, string? TransactionId, string? BankTransactionId, string? Status, decimal? Amount, string? ErrorMessage)`.
  Task 2 (`PaymentService`) consumes this interface directly.

- [ ] **Step 1: Write the interface and result records**

```csharp
// src/MuktoAin.Domain/Interfaces/Services/IPaymentGatewayClient.cs
namespace MuktoAin.Domain.Interfaces.Services;

// Sandbox payment gateway port (SSLCommerz today — see SslCommerzGatewayClient
// in Infrastructure). Two calls mirror SSLCommerz's own two-step flow:
// InitSessionAsync gets a hosted checkout URL to redirect the browser to;
// ValidateAsync is the server-to-server confirmation call made after
// SSLCommerz redirects the browser back to our success/fail/cancel URLs.
public interface IPaymentGatewayClient
{
    Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl);

    Task<GatewayValidationResult> ValidateAsync(string valId);
}

public record GatewaySessionResult(bool Success, string? GatewayPageUrl, string? ErrorMessage);

public record GatewayValidationResult(
    bool Success,
    string? TransactionId,
    string? BankTransactionId,
    string? Status,
    decimal? Amount,
    string? ErrorMessage);
```

- [ ] **Step 2: Write the options class**

```csharp
// src/MuktoAin.Infrastructure/Payments/SslCommerzOptions.cs
namespace MuktoAin.Infrastructure.Payments;

public class SslCommerzOptions
{
    public const string SectionName = "SslCommerz";

    public string StoreId { get; set; } = "";

    public string StorePassword { get; set; } = "";

    // Sandbox by default. Live merchants would point this at
    // https://securepay.sslcommerz.com — never used for this project.
    public string BaseUrl { get; set; } = "https://sandbox.sslcommerz.com";

    public double RequestTimeoutSeconds { get; set; } = 30;
}
```

- [ ] **Step 3: Write the failing tests for `SslCommerzGatewayClient`**

```csharp
// tests/MuktoAin.UnitTests/Infrastructure/SslCommerzGatewayClientTests.cs
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.UnitTests.Infrastructure;

public class SslCommerzGatewayClientTests
{
    private static SslCommerzGatewayClient NewClient(FakeHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SslCommerzOptions
        {
            StoreId = "testbox",
            StorePassword = "testpass",
            BaseUrl = "https://sandbox.sslcommerz.com",
        }));

    [Fact]
    public async Task InitSessionAsync_GatewayReturnsSuccess_ReturnsGatewayPageUrl()
    {
        var handler = new FakeHandler(new StringContent(
            JsonSerializer.Serialize(new
            {
                status = "SUCCESS",
                GatewayPageURL = "https://sandbox.sslcommerz.com/EasyCheckOut/testabc123",
            }), Encoding.UTF8, "application/json"));
        var client = NewClient(handler);

        var result = await client.InitSessionAsync(
            "MA-1-20260912", 500m, "Honorarium",
            "https://localhost/Payment/Success?orderId=1",
            "https://localhost/Payment/Fail?orderId=1",
            "https://localhost/Payment/Cancel?orderId=1");

        Assert.True(result.Success);
        Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/testabc123", result.GatewayPageUrl);
        Assert.Contains("store_id=testbox", handler.LastRequestBody);
        Assert.Contains("tran_id=MA-1-20260912", handler.LastRequestBody);
    }

    [Fact]
    public async Task InitSessionAsync_GatewayReturnsFailed_ReturnsFailureWithReason()
    {
        var handler = new FakeHandler(new StringContent(
            JsonSerializer.Serialize(new { status = "FAILED", failedreason = "Invalid Store Id" }),
            Encoding.UTF8, "application/json"));
        var client = NewClient(handler);

        var result = await client.InitSessionAsync(
            "MA-2-20260912", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Null(result.GatewayPageUrl);
        Assert.Equal("Invalid Store Id", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_StatusValid_ReturnsSuccessWithAmountAndBankTranId()
    {
        var handler = new FakeHandler(new StringContent(
            JsonSerializer.Serialize(new
            {
                status = "VALID",
                tran_id = "MA-1-20260912",
                bank_tran_id = "BKS99881",
                amount = "500.00",
            }), Encoding.UTF8, "application/json"));
        var client = NewClient(handler);

        var result = await client.ValidateAsync("val-123");

        Assert.True(result.Success);
        Assert.Equal("MA-1-20260912", result.TransactionId);
        Assert.Equal("BKS99881", result.BankTransactionId);
        Assert.Equal(500.00m, result.Amount);
    }

    [Fact]
    public async Task ValidateAsync_StatusFailed_ReturnsFailure()
    {
        var handler = new FakeHandler(new StringContent(
            JsonSerializer.Serialize(new { status = "FAILED" }), Encoding.UTF8, "application/json"));
        var client = NewClient(handler);

        var result = await client.ValidateAsync("val-999");

        Assert.False(result.Success);
    }

    // Captures the outgoing request body (Init POSTs form-urlencoded data) and
    // returns a fixed response, standing in for the real SSLCommerz endpoint.
    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpContent _response;
        public string LastRequestBody { get; private set; } = "";

        public FakeHandler(HttpContent response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = _response };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~SslCommerzGatewayClientTests"`
Expected: FAIL — compile error, `SslCommerzGatewayClient` does not exist yet.

- [ ] **Step 5: Implement `SslCommerzGatewayClient`**

```csharp
// src/MuktoAin.Infrastructure/Payments/SslCommerzGatewayClient.cs
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Payments;

// Sandbox integration against SSLCommerz's session-init + validation APIs
// (https://developer.sslcommerz.com/doc/v4/). Chosen over a direct bKash-only
// sandbox because SSLCommerz simulates bKash, Rocket, Nagad, and card
// checkouts behind one integration, using the same test PIN/OTP values as
// each wallet's own sandbox. No Polly pipeline here (unlike GeminiClient) —
// this is a synchronous, user-facing redirect flow, not a background batch
// job; a hung/failed call surfaces immediately as "gateway unreachable"
// rather than needing retry/circuit-breaker machinery.
public class SslCommerzGatewayClient : IPaymentGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly SslCommerzOptions _options;

    public SslCommerzGatewayClient(HttpClient httpClient, IOptions<SslCommerzOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    public async Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl)
    {
        var form = new Dictionary<string, string>
        {
            ["store_id"] = _options.StoreId,
            ["store_passwd"] = _options.StorePassword,
            ["total_amount"] = amount.ToString("0.00"),
            ["currency"] = "BDT",
            ["tran_id"] = transactionId,
            ["success_url"] = successUrl,
            ["fail_url"] = failUrl,
            ["cancel_url"] = cancelUrl,
            ["cus_name"] = "MuktoAin Citizen",
            ["cus_email"] = "citizen@muktoain.local",
            ["cus_add1"] = "Dhaka",
            ["cus_phone"] = "01700000000",
            ["shipping_method"] = "NO",
            ["product_name"] = purposeLabel,
            ["product_category"] = "Legal Service",
            ["product_profile"] = "general",
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(
                $"{_options.BaseUrl}/gwprocess/v4/api.php",
                new FormUrlEncodedContent(form));
        }
        catch (HttpRequestException ex)
        {
            return new GatewaySessionResult(false, null, $"Gateway unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new GatewaySessionResult(false, null, "Gateway request timed out");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new GatewaySessionResult(false, null, $"Gateway returned HTTP {(int)response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<InitResponse>();
        if (payload is null
            || !string.Equals(payload.status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.GatewayPageURL))
        {
            return new GatewaySessionResult(false, null, payload?.failedreason ?? "Gateway session init failed");
        }

        return new GatewaySessionResult(true, payload.GatewayPageURL, null);
    }

    public async Task<GatewayValidationResult> ValidateAsync(string valId)
    {
        var url = $"{_options.BaseUrl}/validator/api/validationserverAPI.php" +
                  $"?val_id={Uri.EscapeDataString(valId)}" +
                  $"&store_id={Uri.EscapeDataString(_options.StoreId)}" +
                  $"&store_passwd={Uri.EscapeDataString(_options.StorePassword)}" +
                  "&format=json";

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url);
        }
        catch (HttpRequestException ex)
        {
            return new GatewayValidationResult(false, null, null, null, null, $"Gateway unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return new GatewayValidationResult(false, null, null, null, null, "Gateway request timed out");
        }

        if (!response.IsSuccessStatusCode)
        {
            return new GatewayValidationResult(false, null, null, null, null, $"Gateway returned HTTP {(int)response.StatusCode}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ValidationResponse>();
        var isValid = payload is not null &&
            (string.Equals(payload.status, "VALID", StringComparison.OrdinalIgnoreCase)
             || string.Equals(payload.status, "VALIDATED", StringComparison.OrdinalIgnoreCase));

        if (!isValid)
        {
            return new GatewayValidationResult(
                false, payload?.tran_id, payload?.bank_tran_id, payload?.status, null, "Payment not valid");
        }

        decimal.TryParse(payload!.amount, out var parsedAmount);
        return new GatewayValidationResult(true, payload.tran_id, payload.bank_tran_id, payload.status, parsedAmount, null);
    }

    private class InitResponse
    {
        public string? status { get; set; }
        public string? GatewayPageURL { get; set; }
        public string? failedreason { get; set; }
    }

    private class ValidationResponse
    {
        public string? status { get; set; }
        public string? tran_id { get; set; }
        public string? bank_tran_id { get; set; }
        public string? amount { get; set; }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~SslCommerzGatewayClientTests"`
Expected: PASS (4/4)

---

### Task 2: `PaymentOrder.TransactionId` + `PaymentService` checkout/confirm flow

**Files:**
- Modify: `src/MuktoAin.Domain/Entities/PaymentOrder.cs`
- Create: `scripts/11_add_payment_order_transaction_id.sql`
- Modify: `src/MuktoAin.Application/Services/PaymentService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs`
- Modify: `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` (constructor only — new `PaymentService` dependency, no behavior change yet)

**Interfaces:**
- Consumes: `IPaymentGatewayClient.InitSessionAsync`/`ValidateAsync`, `GatewaySessionResult`, `GatewayValidationResult` (Task 1).
- Produces: `PaymentService.CreateCheckoutSessionAsync(PaymentOrder order, string successUrl, string failUrl, string cancelUrl) : Task<GatewaySessionResult>` and `PaymentService.ConfirmPaymentAsync(int paymentOrderId, string valId) : Task<bool>`. Task 3 (`PaymentController`) consumes both.

- [ ] **Step 1: Add `TransactionId` to the entity**

```csharp
// src/MuktoAin.Domain/Entities/PaymentOrder.cs — add alongside GatewayRef
public string? TransactionId { get; set; } // our tran_id, set before redirect to the gateway
```

- [ ] **Step 2: Add the idempotent column script**

```sql
-- scripts/11_add_payment_order_transaction_id.sql
/* Adds PAYMENT_ORDER.TransactionId (our tran_id, sent to the sandbox
   gateway at session-init, before any GatewayRef confirmation exists).
   Safe to re-run in SSMS. */
SET NOCOUNT ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
      AND name = N'TransactionId'
)
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD TransactionId NVARCHAR(100) NULL;
END
GO
```

- [ ] **Step 3: Write the failing tests**

Add to `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs` (also add the `_gatewayClient` mock field and pass it into the existing constructor call):

```csharp
using MuktoAin.Domain.Interfaces.Services; // add to usings

// add as a field alongside the other mocks:
private readonly Mock<IPaymentGatewayClient> _gatewayClient = new();

// update the constructor call:
_service = new PaymentService(
    _orderRepo.Object, _payoutRepo.Object, _lawyerRepo.Object, _caseRepo.Object,
    NewUserManager(), _gatewayClient.Object);

[Fact]
public async Task CreateCheckoutSessionAsync_GatewaySucceeds_StoresTransactionIdAndReturnsUrl()
{
    var order = new PaymentOrder { PaymentOrderId = 1, Amount = 500m, Purpose = PaymentPurpose.Honorarium };
    _gatewayClient
        .Setup(g => g.InitSessionAsync(It.IsAny<string>(), 500m, It.IsAny<string>(),
            "https://x/s", "https://x/f", "https://x/c"))
        .ReturnsAsync(new GatewaySessionResult(true, "https://sandbox.sslcommerz.com/EasyCheckOut/abc", null));

    var result = await _service.CreateCheckoutSessionAsync(order, "https://x/s", "https://x/f", "https://x/c");

    Assert.True(result.Success);
    Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/abc", result.GatewayPageUrl);
    Assert.False(string.IsNullOrEmpty(order.TransactionId));
    _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
}

[Fact]
public async Task CreateCheckoutSessionAsync_GatewayFails_DoesNotStoreTransactionId()
{
    var order = new PaymentOrder { PaymentOrderId = 2, Amount = 200m, Purpose = PaymentPurpose.TopUp };
    _gatewayClient
        .Setup(g => g.InitSessionAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new GatewaySessionResult(false, null, "Invalid Store Id"));

    var result = await _service.CreateCheckoutSessionAsync(order, "https://x/s", "https://x/f", "https://x/c");

    Assert.False(result.Success);
    Assert.Null(order.TransactionId);
}

[Fact]
public async Task ConfirmPaymentAsync_ValidMatchingValidation_MarksPaidWithBankTranId()
{
    var order = new PaymentOrder
    {
        PaymentOrderId = 3, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-3-x"
    };
    _orderRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(order);
    _gatewayClient.Setup(g => g.ValidateAsync("val-1"))
        .ReturnsAsync(new GatewayValidationResult(true, "MA-3-x", "BKS777", "VALID", 500m, null));

    var confirmed = await _service.ConfirmPaymentAsync(3, "val-1");

    Assert.True(confirmed);
    Assert.Equal(PaymentStatus.Paid, order.Status);
    Assert.Equal("BKS777", order.GatewayRef);
}

[Fact]
public async Task ConfirmPaymentAsync_TransactionIdMismatch_MarksFailed()
{
    var order = new PaymentOrder
    {
        PaymentOrderId = 4, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-4-x"
    };
    _orderRepo.Setup(r => r.GetByIdAsync(4)).ReturnsAsync(order);
    _gatewayClient.Setup(g => g.ValidateAsync("val-2"))
        .ReturnsAsync(new GatewayValidationResult(true, "SOME-OTHER-TRAN-ID", "BKS1", "VALID", 500m, null));

    var confirmed = await _service.ConfirmPaymentAsync(4, "val-2");

    Assert.False(confirmed);
    Assert.Equal(PaymentStatus.Failed, order.Status);
}

[Fact]
public async Task ConfirmPaymentAsync_AmountMismatch_MarksFailed()
{
    var order = new PaymentOrder
    {
        PaymentOrderId = 5, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-5-x"
    };
    _orderRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(order);
    _gatewayClient.Setup(g => g.ValidateAsync("val-3"))
        .ReturnsAsync(new GatewayValidationResult(true, "MA-5-x", "BKS2", "VALID", 100m, null)); // tampered amount

    var confirmed = await _service.ConfirmPaymentAsync(5, "val-3");

    Assert.False(confirmed);
    Assert.Equal(PaymentStatus.Failed, order.Status);
}

[Fact]
public async Task ConfirmPaymentAsync_AlreadyPaid_IsIdempotent_SkipsGatewayCall()
{
    var order = new PaymentOrder { PaymentOrderId = 6, Amount = 500m, Status = PaymentStatus.Paid };
    _orderRepo.Setup(r => r.GetByIdAsync(6)).ReturnsAsync(order);

    var confirmed = await _service.ConfirmPaymentAsync(6, "val-4");

    Assert.True(confirmed);
    _gatewayClient.Verify(g => g.ValidateAsync(It.IsAny<string>()), Times.Never);
}
```

Also fix the constructor helper in `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` so the suite still compiles (behavior for these tests is unchanged — this is Task 3's job):

```csharp
// PaymentControllerTests constructor — add Mock.Of<IPaymentGatewayClient>() as the new last arg
var paymentService = new PaymentService(
    _orderRepo.Object,
    Mock.Of<IRepository<PayoutRequest>>(),
    Mock.Of<IRepository<LawyerProfile>>(),
    Mock.Of<ICaseRepository>(),
    NewUserManager(),
    Mock.Of<IPaymentGatewayClient>());
```
(add `using MuktoAin.Domain.Interfaces.Services;` to that file's usings)

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"`
Expected: FAIL — compile error, `PaymentService` has no `IPaymentGatewayClient` constructor parameter and no `CreateCheckoutSessionAsync`/`ConfirmPaymentAsync` methods yet.

- [ ] **Step 5: Implement the service changes**

```csharp
// src/MuktoAin.Application/Services/PaymentService.cs
// add to usings:
using MuktoAin.Domain.Interfaces.Services;

// add field + constructor parameter:
private readonly IPaymentGatewayClient _gatewayClient;

public PaymentService(
    IRepository<PaymentOrder> orderRepo,
    IRepository<PayoutRequest> payoutRepo,
    IRepository<LawyerProfile> lawyerRepo,
    ICaseRepository caseRepo,
    UserManager<User> userManager,
    IPaymentGatewayClient gatewayClient)
{
    _orderRepo = orderRepo;
    _payoutRepo = payoutRepo;
    _lawyerRepo = lawyerRepo;
    _caseRepo = caseRepo;
    _userManager = userManager;
    _gatewayClient = gatewayClient;
}

// New: kicks off the real sandbox checkout. Called right after
// CreateHonorariumOrderAsync/CreateTopUpOrderAsync instead of the old
// instant MarkPaidAsync stub.
public async Task<GatewaySessionResult> CreateCheckoutSessionAsync(
    PaymentOrder order, string successUrl, string failUrl, string cancelUrl)
{
    var tranId = $"MA-{order.PaymentOrderId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    var result = await _gatewayClient.InitSessionAsync(
        tranId, order.Amount, order.Purpose.ToString(), successUrl, failUrl, cancelUrl);

    if (result.Success)
    {
        order.TransactionId = tranId;
        await _orderRepo.SaveChangesAsync();
    }
    return result;
}

// New: called from PaymentController.Success after SSLCommerz redirects the
// browser back. Idempotent -- SSLCommerz can redirect more than once (e.g.
// user hits back/refresh), and an already-Paid order must not be re-validated
// or re-marked. Also rejects a validation whose tran_id or amount doesn't
// match this order, since val_id alone is not proof it belongs to THIS order.
public async Task<bool> ConfirmPaymentAsync(int paymentOrderId, string valId)
{
    var order = await _orderRepo.GetByIdAsync(paymentOrderId);
    if (order == null) return false;
    if (order.Status == PaymentStatus.Paid) return true;

    var validation = await _gatewayClient.ValidateAsync(valId);
    var matches = validation.Success
        && validation.TransactionId == order.TransactionId
        && validation.Amount == order.Amount;

    if (matches)
    {
        await MarkPaidAsync(order.PaymentOrderId, validation.BankTransactionId ?? valId);
        return true;
    }

    await MarkFailedAsync(order.PaymentOrderId);
    return false;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests|FullyQualifiedName~PaymentControllerTests"`
Expected: PASS (all `PaymentServiceTests` including the 5 new ones; `PaymentControllerTests` still passes unchanged since only its constructor changed)

---

### Task 3: `PaymentController` — real checkout redirect + Success/Fail/Cancel/Result actions

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/PaymentController.cs`
- Create: `src/MuktoAin.Web/Views/Payment/Result.cshtml`
- Modify: `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs`

**Interfaces:**
- Consumes: `PaymentService.CreateCheckoutSessionAsync`, `PaymentService.ConfirmPaymentAsync`, `PaymentService.MarkFailedAsync` (existing + Task 2).
- Produces: `Honorarium`/`TopUp` JSON responses now shaped `{ success, orderId, gatewayUrl }` on success (was `{ success, orderId, amount, status, gatewayRef, message }`) — Task 4 (frontend JS) consumes `gatewayUrl`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` (add a `_gatewayClient` field used both by the constructor fix from Task 2 and these new tests):

```csharp
// replace the plain Mock.Of<IPaymentGatewayClient>() from Task 2 with a real field:
private readonly Mock<IPaymentGatewayClient> _gatewayClient = new();

// constructor now passes _gatewayClient.Object instead of Mock.Of<...>()

[Fact]
public async Task Honorarium_GatewaySucceeds_ReturnsGatewayUrl()
{
    _gatewayClient
        .Setup(g => g.InitSessionAsync(It.IsAny<string>(), 500m, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new GatewaySessionResult(true, "https://sandbox.sslcommerz.com/EasyCheckOut/xyz", null));

    var result = await _controller.Honorarium(new HonorariumPaymentRequest { CaseId = 5, Amount = 500m });

    var json = Assert.IsType<JsonResult>(result);
    var successProp = json.Value!.GetType().GetProperty("success")!.GetValue(json.Value);
    var urlProp = json.Value!.GetType().GetProperty("gatewayUrl")!.GetValue(json.Value);
    Assert.Equal(true, successProp);
    Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/xyz", urlProp);
}

[Fact]
public async Task Honorarium_GatewayFails_ReturnsFailureMessage()
{
    _gatewayClient
        .Setup(g => g.InitSessionAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
        .ReturnsAsync(new GatewaySessionResult(false, null, "Invalid Store Id"));

    var result = await _controller.Honorarium(new HonorariumPaymentRequest { CaseId = 5, Amount = 500m });

    var json = Assert.IsType<JsonResult>(result);
    var successProp = json.Value!.GetType().GetProperty("success")!.GetValue(json.Value);
    Assert.Equal(false, successProp);
}

[Fact]
public async Task Success_ConfirmedPayment_RedirectsToResultWithSuccessStatus()
{
    _orderRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(
        new PaymentOrder { PaymentOrderId = 7, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-7-x" });
    _gatewayClient.Setup(g => g.ValidateAsync("val-7"))
        .ReturnsAsync(new GatewayValidationResult(true, "MA-7-x", "BKS7", "VALID", 500m, null));

    var result = await _controller.Success(7, new SslCommerzCallbackForm { val_id = "val-7" });

    var redirect = Assert.IsType<RedirectResult>(result);
    Assert.Contains("status=success", redirect.Url);
}

[Fact]
public async Task Success_UnconfirmedPayment_RedirectsToResultWithFailedStatus()
{
    _orderRepo.Setup(r => r.GetByIdAsync(8)).ReturnsAsync(
        new PaymentOrder { PaymentOrderId = 8, Amount = 500m, Status = PaymentStatus.Pending, TransactionId = "MA-8-x" });
    _gatewayClient.Setup(g => g.ValidateAsync("val-8"))
        .ReturnsAsync(new GatewayValidationResult(false, null, null, "FAILED", null, "Payment not valid"));

    var result = await _controller.Success(8, new SslCommerzCallbackForm { val_id = "val-8" });

    var redirect = Assert.IsType<RedirectResult>(result);
    Assert.Contains("status=failed", redirect.Url);
}

[Fact]
public async Task Fail_MarksOrderFailed_RedirectsToResultWithFailedStatus()
{
    var result = await _controller.Fail(9);

    var redirect = Assert.IsType<RedirectResult>(result);
    Assert.Contains("status=failed", redirect.Url);
}

[Fact]
public async Task Cancel_MarksOrderFailed_RedirectsToResultWithCancelledStatus()
{
    var result = await _controller.Cancel(10);

    var redirect = Assert.IsType<RedirectResult>(result);
    Assert.Contains("status=cancelled", redirect.Url);
}
```

`GatewaySessionResult`/`GatewayValidationResult`/`IPaymentGatewayClient` live in `MuktoAin.Domain.Interfaces.Services` — if Task 2's constructor fix didn't already add it, add `using MuktoAin.Domain.Interfaces.Services;` to the top of the test file.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentControllerTests"`
Expected: FAIL — compile error, `PaymentController` has no `Success`/`Fail`/`Cancel` actions and `Honorarium`/`TopUp` don't return `gatewayUrl` yet.

- [ ] **Step 3: Implement the controller changes**

```csharp
// src/MuktoAin.Web/Controllers/PaymentController.cs — replace the body of
// Honorarium and TopUp, and add Success/Fail/Cancel/Result.

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

        var session = await StartCheckoutAsync(order);
        if (!session.Success)
        {
            return Json(new { success = false, message = session.ErrorMessage ?? "Gateway session could not be started" });
        }

        return Json(new
        {
            success = true,
            orderId = order.PaymentOrderId,
            gatewayUrl = session.GatewayPageUrl
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

        var session = await StartCheckoutAsync(order);
        if (!session.Success)
        {
            return Json(new { success = false, message = session.ErrorMessage ?? "Gateway session could not be started" });
        }

        return Json(new
        {
            success = true,
            orderId = order.PaymentOrderId,
            gatewayUrl = session.GatewayPageUrl
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to process top-up payment");
        return StatusCode(500, new { success = false, message = ex.Message });
    }
}

private async Task<GatewaySessionResult> StartCheckoutAsync(PaymentOrder order)
{
    var successUrl = Url.Action(nameof(Success), "Payment", new { orderId = order.PaymentOrderId }, Request.Scheme)!;
    var failUrl = Url.Action(nameof(Fail), "Payment", new { orderId = order.PaymentOrderId }, Request.Scheme)!;
    var cancelUrl = Url.Action(nameof(Cancel), "Payment", new { orderId = order.PaymentOrderId }, Request.Scheme)!;
    return await _paymentService.CreateCheckoutSessionAsync(order, successUrl, failUrl, cancelUrl);
}

// SSLCommerz posts these back as form fields when redirecting the browser to
// success_url/fail_url/cancel_url.
[HttpPost]
public async Task<IActionResult> Success([FromQuery] int orderId, [FromForm] SslCommerzCallbackForm form)
{
    var confirmed = await _paymentService.ConfirmPaymentAsync(orderId, form.val_id ?? "");
    return Redirect($"/Payment/Result?orderId={orderId}&status={(confirmed ? "success" : "failed")}");
}

[HttpPost]
public async Task<IActionResult> Fail([FromQuery] int orderId)
{
    await _paymentService.MarkFailedAsync(orderId);
    return Redirect($"/Payment/Result?orderId={orderId}&status=failed");
}

[HttpPost]
public async Task<IActionResult> Cancel([FromQuery] int orderId)
{
    await _paymentService.MarkFailedAsync(orderId);
    return Redirect($"/Payment/Result?orderId={orderId}&status=cancelled");
}

[HttpGet]
public IActionResult Result(int orderId, string status)
{
    ViewBag.OrderId = orderId;
    ViewBag.Status = status;
    return View();
}
```

Add near the bottom of the file, alongside `HonorariumPaymentRequest`/`TopUpPaymentRequest`:

```csharp
public class SslCommerzCallbackForm
{
    public string? tran_id { get; set; }
    public string? val_id { get; set; }
    public string? amount { get; set; }
    public string? status { get; set; }
}
```

Add to the top of the file: `using MuktoAin.Domain.Interfaces.Services;` (for `GatewaySessionResult`).

- [ ] **Step 4: Create the Result view**

```cshtml
@* src/MuktoAin.Web/Views/Payment/Result.cshtml *@
@{
    ViewData["Title"] = "Payment Result";
    string status = ViewBag.Status ?? "failed";
    int orderId = ViewBag.OrderId;
}
<div class="container" style="max-width: 480px; margin: 60px auto; text-align: center;">
    @if (status == "success")
    {
        <i data-lucide="check-circle" style="width:56px;height:56px;color:var(--gold,#b8860b);"></i>
        <h2 data-bn="পেমেন্ট সফল হয়েছে" data-en="Payment Successful">পেমেন্ট সফল হয়েছে</h2>
        <p class="muted" data-bn="আপনার লেনদেন সম্পন্ন হয়েছে।" data-en="Your transaction has been completed.">
            আপনার লেনদেন সম্পন্ন হয়েছে।
        </p>
    }
    else if (status == "cancelled")
    {
        <i data-lucide="x-circle" style="width:56px;height:56px;color:#999;"></i>
        <h2 data-bn="পেমেন্ট বাতিল করা হয়েছে" data-en="Payment Cancelled">পেমেন্ট বাতিল করা হয়েছে</h2>
        <p class="muted" data-bn="আপনি পেমেন্টটি বাতিল করেছেন।" data-en="You cancelled the payment.">
            আপনি পেমেন্টটি বাতিল করেছেন।
        </p>
    }
    else
    {
        <i data-lucide="alert-circle" style="width:56px;height:56px;color:#c0392b;"></i>
        <h2 data-bn="পেমেন্ট ব্যর্থ হয়েছে" data-en="Payment Failed">পেমেন্ট ব্যর্থ হয়েছে</h2>
        <p class="muted" data-bn="দুঃখিত, আপনার পেমেন্টটি সম্পন্ন করা যায়নি।" data-en="Sorry, your payment could not be completed.">
            দুঃখিত, আপনার পেমেন্টটি সম্পন্ন করা যায়নি।
        </p>
    }
    <p class="tiny muted">Order #@orderId</p>
    <a class="btn btn-gold" href="/" data-bn="হোমে ফিরে যান" data-en="Back to Home">হোমে ফিরে যান</a>
</div>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentControllerTests"`
Expected: PASS (all, including the 6 new tests; the existing zero/negative-amount and `Status_UnknownOrderId_ReturnsNotFound` tests keep passing unchanged)

---

### Task 4: Frontend — redirect to the real gateway page

**Files:**
- Modify: `src/MuktoAin.Web/Views/Case/Result.cshtml` (Honorarium submit handler)
- Modify: `src/MuktoAin.Web/wwwroot/assets/js/chat.js` (Top-Up submit handler)
- Modify: `docs/Testing_Plan.md`

**Interfaces:**
- Consumes: `{ success, orderId, gatewayUrl }` / `{ success: false, message }` JSON shape from Task 3's `Honorarium`/`TopUp` actions.

- [ ] **Step 1: Update the Honorarium modal's JS**

In `src/MuktoAin.Web/Views/Case/Result.cshtml`, replace the `if (data.success) { ... }` branch inside the `btn-submit-honorarium` click handler:

```javascript
if (data.success) {
    window.location.href = data.gatewayUrl;
} else {
    btn.disabled = false;
    btn.innerHTML = '<i data-lucide="credit-card"></i> সম্মানী পাঠান (স্যান্ডবক্স)';
    if (window.lucide) lucide.createIcons();
    if (feedback) {
        feedback.style.display = "block";
        feedback.className = "alert alert-error tiny";
        feedback.textContent = data.message || "পেমেন্ট ব্যর্থ হয়েছে / Payment failed.";
    }
}
```

(The `try`/`catch` connection-error branch stays as-is; no navigation happens there because the fetch itself failed.)

- [ ] **Step 2: Update the Top-Up modal's JS**

In `src/MuktoAin.Web/wwwroot/assets/js/chat.js`, replace the `if (data.success) { ... }` branch around line 479:

```javascript
if (data.success) {
    window.location.href = data.gatewayUrl;
} else {
    if (feedback) {
        feedback.style.display = "block";
        feedback.className = "alert alert-error tiny";
        feedback.textContent = data.message || "টপ-আপ ব্যর্থ হয়েছে / Top-up failed.";
    }
}
```

Since the page is about to navigate away on success, the `finally` block's button re-enable/relabel is now only reached on the failure path, which is already correct behavior for that case — no change needed there.

- [ ] **Step 3: Add a manual test case to the testing plan**

This repo has no JS test runner (`docs/Testing_Plan.md` covers this project's payment cases manually, e.g. `PAY-03`). Add a new case documenting the sandbox redirect flow:

```markdown
| PAY-04 | Honorarium/Top-Up now redirects to a real SSLCommerz sandbox checkout page (bKash/Rocket/Nagad/card test screens) instead of an instant "Paid" stub. Manual: submit the modal, confirm the browser navigates to a `sandbox.sslcommerz.com` URL, complete a test payment (PIN `12121`, OTP `123456` for the bKash option), confirm redirect lands on `/Payment/Result?status=success` and the order shows `Paid` with a `GatewayRef` on `/Payment/Status/{id}`. | Manual |
```

(Match this row to whatever table/column format `docs/Testing_Plan.md` already uses near `PAY-03` — read that section first and follow its exact structure rather than inventing a new one.)

- [ ] **Step 4: Manually verify (no automated JS test harness exists in this repo)**

Run: `dotnet run --project src/MuktoAin.Web`
Then in a browser: open a case's Result page, click "Send Lawyer Honorarium", submit an amount.
Expected: page navigates to a `sandbox.sslcommerz.com` URL showing SSLCommerz's payment-method picker (bKash/Rocket/Nagad/card) — **this step needs real `SslCommerz:StoreId`/`StorePassword` from Task 5 to actually reach a working sandbox session; until those are filled in, expect the JSON response's `success:false` path with a "Gateway session init failed" message, which is also worth confirming shows the right bilingual error text in the modal.**

---

### Task 5: Wiring — DI registration, config, docs, dependency tracking

**Files:**
- Modify: `src/MuktoAin.Web/Program.cs`
- Modify: `src/MuktoAin.Web/appsettings.json`
- Modify: `src/MuktoAin.Web/appsettings.Development.json`
- Modify: `docs/api-contracts.md`
- Modify: `plans/Dependency_plan.md`

**Interfaces:**
- Consumes: `SslCommerzOptions`, `SslCommerzGatewayClient` (Task 1), `IPaymentGatewayClient` (Task 1).
- Produces: nothing further downstream — this is the final task.

- [ ] **Step 1: Register the gateway client in DI**

In `src/MuktoAin.Web/Program.cs`, near the other external-service registrations (after the Gemini block is a reasonable spot):

```csharp
// Sandbox payment gateway (SSLCommerz). Typed HttpClient -- DI hands
// SslCommerzGatewayClient an already-configured HttpClient directly, no
// factory-string indirection needed since (unlike GeminiClient) there's only
// one credential set and no key rotation.
builder.Services.Configure<MuktoAin.Infrastructure.Payments.SslCommerzOptions>(
    builder.Configuration.GetSection(MuktoAin.Infrastructure.Payments.SslCommerzOptions.SectionName));

builder.Services.AddHttpClient<MuktoAin.Infrastructure.Payments.SslCommerzGatewayClient>();

builder.Services.AddTransient<MuktoAin.Domain.Interfaces.Services.IPaymentGatewayClient>(
    sp => sp.GetRequiredService<MuktoAin.Infrastructure.Payments.SslCommerzGatewayClient>());
```

- [ ] **Step 2: Add the config section**

In `src/MuktoAin.Web/appsettings.json`, `ConnectionStrings` is currently the last property in the file — add a comma after its closing `}` and then add `SslCommerz` as the new last property (no trailing comma on its own closing `}`):

```json
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MuktoAin;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
  },
  "SslCommerz": {
    "StoreId": "",
    "StorePassword": "",
    "BaseUrl": "https://sandbox.sslcommerz.com"
  }
```

In `src/MuktoAin.Web/appsettings.Development.json`, add alongside the other sections:

```json
  "SslCommerz": {
    "StoreId": "",
    "StorePassword": "",
    "StoreId_Comment": "Register free at https://developer.sslcommerz.com/registration/ and paste your sandbox Store ID/Password here. Leave blank and Honorarium/Top-Up will return a 'Gateway session could not be started' JSON error instead of crashing.",
    "BaseUrl": "https://sandbox.sslcommerz.com"
  },
```

- [ ] **Step 3: Document the endpoints**

Read `docs/api-contracts.md`'s existing structure first, then add a `Payment` section (or update the existing one if `PaymentController` is already documented there) covering: `POST /Payment/Honorarium` and `POST /Payment/TopUp` now return `{ success, orderId, gatewayUrl }` on success instead of an instant-paid stub; `POST /Payment/Success|Fail|Cancel?orderId={id}` are SSLCommerz's own redirect targets (not meant to be called directly); `GET /Payment/Result?orderId={id}&status={success|failed|cancelled}` is the page the citizen lands on; `GET /Payment/Status/{id}` is unchanged.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: PASS, full suite (the existing count was 187/187 before this feature per `plans/Dependency_plan.md` R-5 — confirm the new count includes this task's ~15 new tests with none failing).

- [ ] **Step 5: Record completion in `plans/Dependency_plan.md`**

Append a new entry under the `🎨 Redesign Wave` section (matching the existing `- [x] **[R-N]** ... — *Author*.` format used by R-1 through R-27):

```markdown
- [x] **[R-28]** Sandbox payments switched from an instant-`Paid` stub to a real SSLCommerz sandbox integration (FR-24) — Honorarium/Top-Up now redirect the citizen through SSLCommerz's actual sandbox checkout (real bKash/Rocket/Nagad/card test screens, same PIN `12121`/OTP `123456` as each wallet's own sandbox), confirmed server-side via `PaymentController.Success` calling SSLCommerz's Validation API before `PaymentService.ConfirmPaymentAsync` marks the order `Paid`; added `IPaymentGatewayClient`/`SslCommerzGatewayClient`, `PaymentOrder.TransactionId`, and `scripts/11_add_payment_order_transaction_id.sql`.
```

---

## Post-Implementation Note (not a task — informational)

The `SslCommerz:StoreId`/`StorePassword` values needed for the sandbox to actually work end-to-end are personal to whoever registers at `developer.sslcommerz.com/registration` — this plan cannot obtain them on your behalf. Until they're filled in, every task's automated tests still pass (they mock `IPaymentGatewayClient`), but a real browser run of Task 4's manual step will show the "Gateway session could not be started" failure path instead of an actual SSLCommerz checkout page.
