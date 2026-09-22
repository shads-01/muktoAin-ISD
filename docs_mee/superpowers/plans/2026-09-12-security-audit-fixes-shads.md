# Security & Robustness Audit Fixes (Shads) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the 8 Shads-assigned findings from `docs/PROJECT_AUDIT_REPORT.md` (AUD-1, AUD-2, AUD-3, AUD-5, AUD-6, AUD-9, AUD-10, AUD-12) — CSRF protection on the JSON controllers, Data Protection key persistence, an atomic AI-quota reservation, rate limiting, security headers, and three dead-code/severity cleanups.

**Architecture:** All changes live in the existing Clean Architecture boundaries: Razor/MVC changes in `src/MuktoAin.Web` (controllers, `Program.cs`, `_Layout.cshtml`, `wwwroot` JS), the quota-reservation seam in `src/MuktoAin.Application` (new service interface + `AiBudgetService`), and its SQL implementation in `src/MuktoAin.Infrastructure` (raw parameterized T-SQL against the SSMS-authored `[dbo].[AI_LOG]` table — no schema change, no EF migration). Unit tests in `tests/MuktoAin.UnitTests` (xUnit + Moq) follow the existing per-controller/per-service test-file convention.

**Tech Stack:** ASP.NET Core MVC (.NET 8), built-in Antiforgery + Rate Limiting (`Microsoft.AspNetCore.RateLimiting`) + Data Protection APIs, EF Core `ExecuteSqlAsync` for the atomic reservation INSERT, vanilla JS (`chat.js`), Bootstrap 5 views. No new NuGet packages.

**Spec:** `docs/PROJECT_AUDIT_REPORT.md` — findings referenced per task below (Frontend/Citizen #1, Backend/Citizen #4/#5/#8/#9, Admin Scope #3, Cross-Cutting #1/#2/#5).

## Global Constraints

- **NO git commits.** Per `AGENTS.md` §6, Shads is the sole committer. Every "commit" step from the skill default is replaced by: record completion in `plans/Dependency_plan.md` (checkbox `[x]` + `~~strikethrough~~`).
- **No EF migrations.** Schema is authored in SSMS via `scripts/*.sql`. No task in this plan changes the schema (AUD-3's reservation row reuses the existing `[dbo].[AI_LOG]` table).
- **No SPA frameworks.** All client changes are vanilla JS in `src/MuktoAin.Web/wwwroot/assets/js/` or inline `<script>` in Razor views.
- **No new NuGet packages.** Everything uses .NET 8 in-box APIs.
- **Solution file is `MuktoAin.slnx`** — build with `dotnet build MuktoAin.slnx`, test with `dotnet test tests/MuktoAin.UnitTests` (integration tests require a live SQL Server; run only when available).
- **Match existing code conventions exactly** — explanatory block comments above non-obvious code, 4-space indent, file-scoped namespaces, constructor injection via readonly fields.
- **Do NOT touch findings assigned to other workers:** AUD-4 (Hrittika), AUD-7/AUD-8/AUD-11 (Arpita).
- **R-28 already landed in the working tree** (chat/payment/document ownership checks, template registrations, `GetMockDocument` removal). All line numbers below refer to the CURRENT working-tree state, verified 2026-09-12.

---

### Task 1: AUD-1 — CSRF Antiforgery on Chat/Payment POST Endpoints + Client Token Wiring

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/ChatController.cs:43-53,203` (POST actions `New`, `Ask`, `Commit`)
- Modify: `src/MuktoAin.Web/Controllers/PaymentController.cs:36,88` (POST actions `Honorarium`, `TopUp`)
- Modify: `src/MuktoAin.Web/Views/Shared/_Layout.cshtml` (inject `IAntiforgery`, emit `<meta name="csrf-token">`)
- Modify: `src/MuktoAin.Web/wwwroot/assets/js/chat.js:239-247,289-301,381-385,473-477` (4 POST fetches)
- Modify: `src/MuktoAin.Web/Views/Case/Result.cshtml:377-381` (inline Honorarium fetch)
- Test: `tests/MuktoAin.UnitTests/Controllers/ChatControllerTests.cs`, `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs`

**Interfaces:**
- Consumes: ASP.NET Core antiforgery default header name `RequestVerificationToken` (default; no config change needed).
- Produces: `<meta name="csrf-token" content="...">` in every page's `<head>` (consumed by `chat.js` and the `Case/Result.cshtml` inline script). No C# signatures change.

**Audit basis:** Backend/Citizen #5 — CA5391 × 5 (`ChatController.New/Ask/Commit`, `PaymentController.Honorarium/TopUp`); Frontend/Citizen #1 — client JS sends no token.

- [ ] **Step 1: Write the failing attribute tests**

Append to `tests/MuktoAin.UnitTests/Controllers/ChatControllerTests.cs` (inside the `ChatControllerTests` class; `Microsoft.AspNetCore.Mvc` is already imported at the top of the file):

```csharp
    // AUD-1 (CA5391): the JSON-body POST actions must be CSRF-protected like
    // every other POST controller in the app. Reflection pins the attribute so
    // a refactor that silently drops it fails the suite.
    [Theory]
    [InlineData(nameof(ChatController.New))]
    [InlineData(nameof(ChatController.Ask))]
    [InlineData(nameof(ChatController.Commit))]
    public void PostActions_CarryValidateAntiForgeryToken(string actionName)
    {
        var method = typeof(ChatController).GetMethod(actionName)!;

        Assert.True(
            method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: false).Any(),
            $"ChatController.{actionName} is missing [ValidateAntiForgeryToken].");
    }
```

Append to `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` (inside the `PaymentControllerTests` class; add `using System.Reflection;` and `using Xunit;` at the top if not present):

```csharp
    // AUD-1 (CA5391): see ChatControllerTests for rationale.
    [Theory]
    [InlineData(nameof(PaymentController.Honorarium))]
    [InlineData(nameof(PaymentController.TopUp))]
    public void PostActions_CarryValidateAntiForgeryToken(string actionName)
    {
        var method = typeof(PaymentController).GetMethod(actionName)!;

        Assert.True(
            method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: false).Any(),
            $"PaymentController.{actionName} is missing [ValidateAntiForgeryToken].");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ValidateAntiForgeryToken"`
Expected: FAIL — 5 failing cases ("...is missing [ValidateAntiForgeryToken]").

- [ ] **Step 3: Add `[ValidateAntiForgeryToken]` to both controllers**

In `src/MuktoAin.Web/Controllers/ChatController.cs`, add the attribute to all three POST actions (keep the existing `[HttpPost]` lines; add the attribute directly beneath each, matching the convention in `AccountController.cs:39-40`):

```csharp
    // Start (or resume) a session. Body: { "firstMessage": "..." } (optional)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New([FromBody] ChatNewRequest? body)
```

```csharp
    // Ask a question. Body: { chatSessionId, question, language? }
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest? body)
```

```csharp
    // Generate Draft commit. Body: all modal fields.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit([FromBody] ChatCommitRequest? body)
```

In `src/MuktoAin.Web/Controllers/PaymentController.cs`:

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Honorarium([FromBody] HonorariumPaymentRequest body)
```

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TopUp([FromBody] TopUpPaymentRequest body)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ValidateAntiForgeryToken"`
Expected: PASS (all 5).

- [ ] **Step 5: Emit the token to the client via `_Layout.cshtml`**

In `src/MuktoAin.Web/Views/Shared/_Layout.cshtml`, add this line at the very top of the file (above the existing `@{ ... }` / `@using` block at the top of the file):

```
@inject Microsoft.AspNetCore.Antiforgery.IAntiforgery Antiforgery
```

Then immediately after the existing top-of-file C# block (before `<!DOCTYPE html>`), add:

```
@{
    // AUD-1: single antiforgery token per page, exposed to the vanilla-JS
    // fetch() calls (chat.js + inline scripts) via the meta tag in <head>.
    var antiforgeryToken = Antiforgery.GetAndStoreTokens(Context).RequestToken;
}
```

In the `<head>` (after line 48, the `meta name="description"` line), add:

```html
    <meta name="csrf-token" content="@antiforgeryToken" />
```

(`GetAndStoreTokens` also sets the antiforgery cookie, so anonymous guests — who use `/Chat/New`, `/Chat/Ask` — are covered too.)

- [ ] **Step 6: Wire the token into `chat.js` POST fetches**

In `src/MuktoAin.Web/wwwroot/assets/js/chat.js`, add this helper directly below the existing `function el(id)` helper (line 11):

```js
    // AUD-1: antiforgery token emitted by _Layout.cshtml on every page.
    function csrfToken() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute("content") : "";
    }
```

Update the four POST fetch headers:

`ask()` — line 239:
```js
        fetch("/Chat/Ask", {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
```

`submitDraft()` — line 289:
```js
        fetch("/Chat/Commit", {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
```

`ensureSession()` — line 381:
```js
        fetch("/Chat/New", {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
            body: "{}"
        })
```

Top-up handler — line 473:
```js
                    var res = await fetch("/Payment/TopUp", {
                        method: "POST",
                        headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
                        body: JSON.stringify({ amount: amount })
                    });
```

- [ ] **Step 7: Wire the token into the Honorarium fetch (`Case/Result.cshtml`)**

In `src/MuktoAin.Web/Views/Case/Result.cshtml`, inside the inline `<script>` (line 355 onward), update the fetch at line 377. Note: `main.js` itself has **no POST fetches** (its only call is `GET /Admin/HealthStatus` at `main.js:1035`, which needs no token) — the Honorarium call lives in this view's inline script, so this is where the fourth client call gets wired:

```js
        try {
            var csrfMeta = document.querySelector('meta[name="csrf-token"]');
            var res = await fetch("/Payment/Honorarium", {
                method: "POST",
                headers: { "Content-Type": "application/json",
                           "RequestVerificationToken": (csrfMeta ? csrfMeta.getAttribute("content") : "") },
                body: JSON.stringify({ caseId: @Model.CaseId, amount: amount, trackingCode: '@Model.TrackingCode' })
            });
```

- [ ] **Step 8: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, 0 errors; all unit tests pass (the controller tests exercise actions directly and are unaffected by the antiforgery filter, which only runs in the MVC pipeline).

- [ ] **Step 9: Manual end-to-end verification (browser)**

Run: `dotnet run --project src/MuktoAin.Web`
Then in a browser (guest mode, no sign-in):
1. Open DevTools → Network. Ask a chat question — the `POST /Chat/Ask` request must include a `RequestVerificationToken` header, and the server must return 200 (not 400 with "antiforgery" in the body).
2. Open the Generate Draft modal and submit — `POST /Chat/Commit` returns 200.
3. Top-up modal — `POST /Payment/TopUp` returns 200.
Expected: all three succeed; repeating them with the `RequestVerificationToken` header removed (DevTools → "Edit and Resend") returns 400 Antiforgery Validation failure.

- [ ] **Step 10: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-1]**` line (line 228) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 2: AUD-2 — Data Protection Key Ring Persistence

**Files:**
- Modify: `src/MuktoAin.Web/Program.cs:158-160` (the `// S-1.7` Data Protection block)
- Modify: `.gitignore` (add `keys/` under the "MuktoAin Project Specific Ignores" section)

**Interfaces:**
- Consumes: nothing new. Produces: a stable Data Protection key ring at `src/MuktoAin.Web/keys/` shared by `EncryptionService` (field-level PII on `Case.Title`/`Case.Description`).

**Audit basis:** Backend/Citizen #7 + Cross-Cutting #5 — bare `AddDataProtection()` rotates keys on every container redeploy; `SafeDecrypt` then renders raw ciphertext as case titles.

- [ ] **Step 1: Configure key persistence in `Program.cs`**

Replace lines 158-160 of `src/MuktoAin.Web/Program.cs`:

```csharp
// S-1.7: Data Protection + field-level PII encryption.
builder.Services.AddDataProtection();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
```

with:

```csharp
// S-1.7: Data Protection + field-level PII encryption.
// AUD-2: persist the key ring under ContentRootPath/keys and pin the
// application name. Without this, every container redeploy rotates the key
// (the default path is ephemeral in the shipped Dockerfile) and encrypted
// Case.Title/Description become permanently unreadable ciphertext.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("MuktoAin.Web");
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
```

- [ ] **Step 2: Ignore the keys directory in `.gitignore`**

Add to the end of the "MuktoAin Project Specific Ignores" section of `.gitignore` (after the `# Scratch files` block):

```
# Data Protection key ring (AUD-2) — never commit encryption keys
keys/
```

- [ ] **Step 3: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, all tests pass. (Config-only change — **no unit test applies**; verification is inspection + the runtime smoke test below.)

- [ ] **Step 4: Runtime smoke test — key file is created and reused across restarts**

Run: `dotnet run --project src/MuktoAin.Web`
1. After startup, confirm `src/MuktoAin.Web/keys/` exists and contains a `key-*.xml` file.
2. Note the XML filename, stop the app (Ctrl+C), start it again.
3. Confirm the SAME `key-*.xml` file is still the only one (no new key generated).
4. Confirm in `git status` that `keys/` does NOT appear as untracked.

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-2]**` line (line 229) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 3: AUD-3 — Atomic AI Quota Reservation (TOCTOU Race Fix)

**Files:**
- Create: `src/MuktoAin.Application/Services/IAiTurnReservationStore.cs`
- Create: `src/MuktoAin.Infrastructure/Data/AiTurnReservationStore.cs`
- Modify: `src/MuktoAin.Application/Services/AiBudgetService.cs:20-23,55-59` (constructor + `TryReserveTurnAsync` + new `ReleaseReservationAsync`)
- Modify: `src/MuktoAin.Web/Controllers/ChatController.cs:62-141` (`Ask` quota flow: reserve BEFORE the model call, release on cache/retrieval-only)
- Modify: `src/MuktoAin.Web/Program.cs` (DI registration, near line 233-235)
- Test: `tests/MuktoAin.UnitTests/Services/AiBudgetServiceTests.cs` (new), `tests/MuktoAin.UnitTests/Controllers/ChatControllerTests.cs` (constructor + new wall test)

**Interfaces:**
- Consumes: existing `GetRemainingToday(int? userId, string? sessionKey)` / `RecordTurnUsed(...)` / `DailyLimitFor(bool isLoggedIn)` / `PacificMidnightUtc()` in `AiBudgetService` (unchanged).
- Produces:
  - `IAiTurnReservationStore` (namespace `MuktoAin.Application.Services`):
    ```csharp
    Task<bool> TryReserveAsync(DateTime sinceUtc, int limit, CancellationToken ct = default);
    Task ReleaseOneAsync(CancellationToken ct = default);
    ```
  - `AiBudgetService` public surface: `Task<bool> TryReserveTurnAsync(int? userId, string? sessionKey)` (signature unchanged) and new `Task ReleaseReservationAsync()`.
  - DI: `builder.Services.AddScoped<IAiTurnReservationStore, AiTurnReservationStore>();`

**Audit basis:** Backend/Citizen #8 — `TryReserveTurnAsync` (line 55) is a read-only count check; two concurrent `Ask` requests both see `RemainingToday == 1` and both burn a metered Gemini call.

**Design note (important):** The current controller flow calls `AskAsync` FIRST (the pipeline logs the real `AI_LOG` row inside `AskAsync`) and only *checks* quota afterwards. The fix reserves a countable row atomically BEFORE the model call: the reservation IS an `AI_LOG` row (`RequestType = RightsExplanation`, `CaseId = NULL`, `ModelUsed = "(reserved)"`), so `GetRemainingToday`'s existing count sees it the instant it lands. Cache hits and retrieval-only turns (which consume no model call) release the row afterwards, preserving FR-19's "cache hits stretch the quota" semantics. If `AskAsync` throws, the reservation stays — one turn lost, the conservative direction.

- [ ] **Step 1: Write the failing `AiBudgetServiceTests`**

Create `tests/MuktoAin.UnitTests/Services/AiBudgetServiceTests.cs`:

```csharp
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

// AUD-3: TryReserveTurnAsync now delegates to IAiTurnReservationStore, whose
// single-statement SQL makes the check+insert atomic. These tests pin the
// Application-layer contract: the daily window/limit derivation and the
// reservation/release choreography.
public class AiBudgetServiceTests
{
    private readonly Mock<IRepository<AiLog>> _logRepo = new();
    private readonly Mock<IAiTurnReservationStore> _store = new();

    private AiBudgetService CreateService() => new(_logRepo.Object, _store.Object);

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreReserves_ReturnsTrue()
    {
        _store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var reserved = await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        Assert.True(reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreDeniesQuotaExceeded_ReturnsFalse()
    {
        _store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var reserved = await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        Assert.False(reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_SignedInUser_ReservesWithSignedInDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        _store.Verify(s => s.TryReserveAsync(
            It.IsAny<DateTime>(), 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryReserveTurnAsync_Guest_ReservesWithGuestDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        _store.Verify(s => s.TryReserveAsync(
            It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseReservationAsync_DelegatesToStore()
    {
        await CreateService().ReleaseReservationAsync();

        _store.Verify(s => s.ReleaseOneAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AiBudgetServiceTests"`
Expected: BUILD FAILURE — `IAiTurnReservationStore` and the 2-arg `AiBudgetService` constructor do not exist yet.

- [ ] **Step 3: Create the reservation-store interface**

Create `src/MuktoAin.Application/Services/IAiTurnReservationStore.cs`:

```csharp
namespace MuktoAin.Application.Services;

// AUD-3 (TOCTOU quota race): the old TryReserveTurnAsync only COUNTED existing
// AI_LOG rows, so two concurrent Ask requests could both pass with one turn
// left. Implementations must perform the quota check and the reservation
// insert in a SINGLE atomic SQL statement so exactly one of two simultaneous
// requests can reserve the last turn. The reservation row is an AI_LOG row
// that GetRemainingToday already counts (RequestType = RightsExplanation,
// CaseId = NULL); ReleaseOneAsync removes it when the turn turned out to be
// cache-served or retrieval-only (no model call was made).
public interface IAiTurnReservationStore
{
    Task<bool> TryReserveAsync(DateTime sinceUtc, int limit, CancellationToken ct = default);

    Task ReleaseOneAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create the SQL Server implementation**

Create `src/MuktoAin.Infrastructure/Data/AiTurnReservationStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Infrastructure.Data;

// AUD-3: atomic chat-turn reservation. The reservation is an AI_LOG row
// (RequestType = RightsExplanation, CaseId = NULL, ModelUsed = marker), so
// AiBudgetService.GetRemainingToday counts it the moment it lands.
//
// The UPDLOCK/HOLDLOCK range lock on the counting subquery serializes the
// read-check-write inside ONE statement: when exactly one turn remains, two
// concurrent inserts cannot both see count < limit — the second blocks on the
// range lock, re-evaluates after the first commits, and inserts 0 rows.
public class AiTurnReservationStore : IAiTurnReservationStore
{
    // Sentinel in ModelUsed — a real pipeline log row always carries the
    // actual model name (e.g. "gemini-2.5-flash"), so this never collides.
    public const string ReservedModelMarker = "(reserved)";

    private readonly AppDbContext _db;

    public AiTurnReservationStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryReserveAsync(DateTime sinceUtc, int limit, CancellationToken ct = default)
    {
        var requestType = (int)AiRequestType.RightsExplanation;

        var inserted = await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO [dbo].[AI_LOG]
                (CaseId, RequestType, PromptText, ResponseText, ModelUsed, TokensUsed, LatencyMs, CreatedAt)
            SELECT NULL, {requestType}, N'', N'', {ReservedModelMarker}, 0, 0, SYSUTCDATETIME()
            WHERE (
                SELECT COUNT_BIG(*)
                FROM [dbo].[AI_LOG] WITH (UPDLOCK, HOLDLOCK)
                WHERE RequestType = {requestType} AND CaseId IS NULL AND CreatedAt >= {sinceUtc}
            ) < {limit}
            """, ct);

        return inserted > 0;
    }

    public async Task ReleaseOneAsync(CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlAsync($"""
            DELETE FROM [dbo].[AI_LOG]
            WHERE LogId = (
                SELECT TOP (1) LogId
                FROM [dbo].[AI_LOG]
                WHERE ModelUsed = {ReservedModelMarker}
                ORDER BY LogId DESC)
            """, ct);
    }
}
```

- [ ] **Step 5: Update `AiBudgetService`**

In `src/MuktoAin.Application/Services/AiBudgetService.cs`, replace the constructor and the `TryReserveTurnAsync` method (lines 18-23 and 55-59):

```csharp
    private readonly IRepository<AiLog> _logRepo;
    private readonly IAiTurnReservationStore _reservationStore;

    public AiBudgetService(IRepository<AiLog> logRepo, IAiTurnReservationStore reservationStore)
    {
        _logRepo = logRepo;
        _reservationStore = reservationStore;
    }
```

```csharp
    // AUD-3: atomic reserve-before-call. The reservation row (written by the
    // store in one T-SQL statement) is an AI_LOG row, so GetRemainingToday
    // counts it immediately — a second concurrent request hits the wall here
    // instead of after both Gemini calls have already fired.
    public async Task<bool> TryReserveTurnAsync(int? userId, string? sessionKey)
    {
        var since = PacificMidnightUtc();
        var limit = DailyLimitFor(userId.HasValue);
        return await _reservationStore.TryReserveAsync(since, limit);
    }

    // Gives a reserved turn back when the turn turned out to be free
    // (cache hit / retrieval-only — no model call was made).
    public Task ReleaseReservationAsync() => _reservationStore.ReleaseOneAsync();
```

`GetRemainingToday`, `RecordTurnUsed`, `DailyLimitFor`, and `PacificMidnightUtc` are unchanged.

- [ ] **Step 6: Run the service tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AiBudgetServiceTests"`
Expected: PASS (5 tests). (`ChatControllerTests` will not compile yet — the `AiBudgetService` constructor changed; fixed in Step 8.)

- [ ] **Step 7: Restructure `ChatController.Ask` — reserve BEFORE the model call**

In `src/MuktoAin.Web/Controllers/ChatController.cs`, replace the quota block of `Ask` (currently lines 62-141, from `var userId = CurrentUserId();` through the closing of the final `return Json(...)` and its closing brace) with:

```csharp
        var userId = CurrentUserId();
        var key = SessionKey();
        var allowed = session.UserId == userId
                      || (session.UserId == null && session.SessionKey == key);
        if (!allowed) return Forbid();

        // Cache-first (spec: cache hits stretch the daily quota — a repeated
        // question must NOT be charged a turn or walled).
        var language = string.IsNullOrWhiteSpace(body.Language) ? "bn" : body.Language;

        // AUD-3: reserve BEFORE the metered call so two concurrent asks
        // cannot both pass a stale count (TOCTOU). The reservation IS an
        // AI_LOG row that GetRemainingToday counts; cache hits and
        // retrieval-only turns release it right after AskAsync.
        if (!await _budgetService.TryReserveTurnAsync(userId, key))
        {
            var wall = await _budgetService.GetRemainingToday(userId, key);
            return Json(new
            {
                tier = "wall",
                remainingToday = wall.RemainingToday,
                dailyLimit = wall.DailyLimit
            });
        }

        var turn = await _chatService.AskAsync(body.ChatSessionId, body.Question, language, allowCapped: false);

        if (turn.FromCache)
        {
            // No model call was made — give the reserved turn back.
            await _budgetService.ReleaseReservationAsync();

            var cacheQuota = await _budgetService.GetRemainingToday(userId, key);
            await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
            await _chatService.AppendMessageAsync(
                body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));
            return Json(new
            {
                tier = turn.Tier,
                answer = turn.Answer,
                disclaimer = turn.Disclaimer,
                fromCache = true,
                retrievalOnly = turn.RetrievalOnly,
                citedSections = turn.CitedSections.Select(s => new
                {
                    sectionId = s.SectionId,
                    actTitle = s.ActTitle,
                    sectionNumber = s.SectionNumber,
                    sectionText = s.SectionText,
                    relevance = Math.Round(s.RelevanceScore * 100) + "%"
                }),
                remainingToday = cacheQuota.RemainingToday,
                dailyLimit = cacheQuota.DailyLimit
            });
        }

        // Retrieval-only answers also make no model call (they're the
        // ladder's free tier), so only a real AI turn keeps its reservation.
        if (turn.RetrievalOnly)
        {
            await _budgetService.ReleaseReservationAsync();
        }

        var quota = turn.RetrievalOnly
            ? await _budgetService.GetRemainingToday(userId, key)
            : await _budgetService.RecordTurnUsed(userId, key);

        await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
        await _chatService.AppendMessageAsync(
            body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));

        return Json(new
        {
            tier = turn.Tier,
            answer = turn.Answer,
            disclaimer = turn.Disclaimer,
            fromCache = turn.FromCache,
            retrievalOnly = turn.RetrievalOnly,
            citedSections = turn.CitedSections.Select(s => new
            {
                sectionId = s.SectionId,
                actTitle = s.ActTitle,
                sectionNumber = s.SectionNumber,
                sectionText = s.SectionText,
                relevance = Math.Round(s.RelevanceScore * 100) + "%"
            }),
            remainingToday = quota.RemainingToday,
            dailyLimit = quota.DailyLimit
        });
    }
```

(The `wall` JSON shape is unchanged, so `chat.js`'s `if (data.tier === "wall") quotaWallCard();` keeps working.)

- [ ] **Step 8: Update `ChatControllerTests` and add the wall-path controller test**

In `tests/MuktoAin.UnitTests/Controllers/ChatControllerTests.cs`, add `using Moq;` is already present; add the helper + constructor change so the controller gets a default reservation store that succeeds:

```csharp
    private static Mock<IAiTurnReservationStore> DefaultReservationStore()
    {
        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        return store;
    }
```

Change the constructor line `var budgetService = new AiBudgetService(Mock.Of<IRepository<AiLog>>());` to:

```csharp
        var budgetService = new AiBudgetService(
            Mock.Of<IRepository<AiLog>>(), DefaultReservationStore().Object);
```

Then add the new test (the wall now returns BEFORE `AskAsync`, so no pipeline stubbing is needed):

```csharp
    // AUD-3: a denied reservation walls the request BEFORE the AI pipeline runs.
    [Fact]
    public async Task Ask_WhenQuotaReservationFails_ReturnsWallWithoutModelCall()
    {
        var session = new ChatSession
        {
            ChatSessionId = 15,
            UserId = 42, // same as the authenticated test user
            Title = "My Chat"
        };
        _sessionRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(session);

        var store = new Mock<IAiTurnReservationStore>();
        store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        var chatService = new ChatService(
            _sessionRepo.Object,
            Mock.Of<IRepository<ChatMessage>>(),
            Mock.Of<IRepository<Case>>(),
            Mock.Of<ICaseRepository>(),
            Mock.Of<IRepository<AnswerCache>>(),
            Mock.Of<IRightsExplanationService>(),
            null!,
            Mock.Of<IEncryptionService>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IKeywordSectionSearch>());
        var controller = new ChatController(chatService, new AiBudgetService(Mock.Of<IRepository<AiLog>>(), store.Object))
        {
            ControllerContext = _controller.ControllerContext
        };

        var result = await controller.Ask(new ChatAskRequest { ChatSessionId = 15, Question = "Why?" });

        var json = Assert.IsType<JsonResult>(result);
        var tier = (string)json.Value!.GetType().GetProperty("tier")!.GetValue(json.Value)!;
        Assert.Equal("wall", tier);
    }
```

- [ ] **Step 9: Register the store in DI**

In `src/MuktoAin.Web/Program.cs`, in the "Frontend redesign 2026-09" block (around line 233), add the registration directly above `AddScoped<AiBudgetService>()`:

```csharp
// Frontend redesign 2026-09: chat-first home + AI budget
builder.Services.AddScoped<IAiTurnReservationStore, AiTurnReservationStore>();
builder.Services.AddScoped<AiBudgetService>();
builder.Services.AddScoped<ChatService>();
```

(`AiTurnReservationStore` is in `MuktoAin.Infrastructure.Data` — already imported via `using MuktoAin.Infrastructure.Data;` at the top of `Program.cs`.)

- [ ] **Step 10: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, all tests pass (including the pre-existing `ChatControllerTests` and `PaymentControllerTests`).

- [ ] **Step 11: Live smoke verification (requires SQL Server + LocalDB running)**

Run: `dotnet run --project src/MuktoAin.Web`
1. Ask a normal chat question — answer returns and `AI_LOG` gains one RightsExplanation row with `ModelUsed = 'gemini-2.5-flash'` (or the configured model), NOT `(reserved)`.
2. Ask a question twice (cache hit on the repeat) — quota counter does NOT double-decrease (the released reservation row is deleted).
3. Double-click send rapidly with 1 turn remaining — exactly ONE request proceeds; the other receives `tier = "wall"`. Verify with:
   ```sql
   SELECT ModelUsed, COUNT(*) FROM [dbo].[AI_LOG]
   WHERE RequestType = 1 AND CaseId IS NULL
     AND CreatedAt >= DATEADD(day, DATEDIFF(day, 0, SYSUTCDATETIME()), 0)
   GROUP BY ModelUsed;
   ```
   Expected: no lingering `(reserved)` rows, and the total RightsExplanation/NULL-Case count never exceeds the daily limit.

- [ ] **Step 12: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-3]**` line (line 230) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 4: AUD-5 — Rate Limiting Middleware

**Files:**
- Modify: `src/MuktoAin.Web/Program.cs` (usings + `AddRateLimiter` service config + `UseRateLimiter` in pipeline + `PartitionKey` local function)
- Modify: `src/MuktoAin.Web/Controllers/AccountController.cs:39,97` (add `[EnableRateLimiting("auth")]` to POST `Login` and POST `Register`)
- Modify: `src/MuktoAin.Web/Controllers/ChatController.cs:52` (add `[EnableRateLimiting("chat")]` to `Ask`)
- Modify: `src/MuktoAin.Web/Controllers/PaymentController.cs:36,88` (add `[EnableRateLimiting("payment")]` to `Honorarium` and `TopUp`)
- Test: `tests/MuktoAin.UnitTests/Controllers/RateLimitingPolicyTests.cs` (new)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: named rate-limit policies `"auth"` (5 req/min per IP), `"chat"` (10 req/min per user/IP), `"payment"` (5 req/min per user/IP) — referenced only by the `[EnableRateLimiting]` attributes on those five actions.

**Audit basis:** Cross-Cutting #1 + Backend/Citizen #12 — nothing throttles Login/Register/Chat/Ask/Payment; compounds the IDOR findings.

- [ ] **Step 1: Write the failing policy tests**

Create `tests/MuktoAin.UnitTests/Controllers/RateLimitingPolicyTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

// AUD-5: throttling is wired via [EnableRateLimiting] attributes. Reflection
// pins the policy name on every throttled action so a refactor that silently
// drops the attribute fails the suite. The limiter mechanics themselves are
// the framework's own (fixed window), so no behavioral test is needed here.
public class RateLimitingPolicyTests
{
    [Fact]
    public void AccountLogin_IsRateLimitedByAuthPolicy()
    {
        var method = typeof(AccountController).GetMethod(
            nameof(AccountController.Login), new[] { typeof(LoginViewModel), typeof(string) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("auth", attr!.PolicyName);
    }

    [Fact]
    public void AccountRegister_IsRateLimitedByAuthPolicy()
    {
        var method = typeof(AccountController).GetMethod(
            nameof(AccountController.Register), new[] { typeof(RegisterViewModel) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("auth", attr!.PolicyName);
    }

    [Fact]
    public void ChatAsk_IsRateLimitedByChatPolicy()
    {
        var method = typeof(ChatController).GetMethod(
            nameof(ChatController.Ask), new[] { typeof(ChatAskRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("chat", attr!.PolicyName);
    }

    [Fact]
    public void PaymentHonorarium_IsRateLimitedByPaymentPolicy()
    {
        var method = typeof(PaymentController).GetMethod(
            nameof(PaymentController.Honorarium), new[] { typeof(HonorariumPaymentRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("payment", attr!.PolicyName);
    }

    [Fact]
    public void PaymentTopUp_IsRateLimitedByPaymentPolicy()
    {
        var method = typeof(PaymentController).GetMethod(
            nameof(PaymentController.TopUp), new[] { typeof(TopUpPaymentRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("payment", attr!.PolicyName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~RateLimitingPolicyTests"`
Expected: FAIL — 5 cases, "Assert.NotNull() Failure" (no attributes yet).

- [ ] **Step 3: Configure the limiter in `Program.cs`**

In `src/MuktoAin.Web/Program.cs`, add usings at the top (with the existing using block):

```csharp
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
```

Add the service registration after the `ConfigureApplicationCookie` block (around line 83):

```csharp
// AUD-5: per-route throttling (audit: nothing limits Login/Register brute
// force, Chat/Ask metered-model spam, or payment endpoints). Fixed windows,
// partitioned per IP for auth and per user (falling back to IP for guests)
// for chat/payment. Rejections return the default 429 via UseStatusCodePages.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("payment", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

In the pipeline, insert `app.UseRateLimiter();` immediately AFTER `app.UseRouting();` (line 337) and BEFORE `app.UseAuthentication();`:

```csharp
app.UseRouting();

// AUD-5: endpoint-scoped limiter policies require UseRouting to have matched
// the endpoint first; run before auth so throttled floods never reach Identity.
app.UseRateLimiter();

// S-1.1: authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();
```

At the bottom of `Program.cs` (after `app.Run();`), add the partition-key helper (top-level statements allow trailing local functions):

```csharp
// AUD-5: per-user partition for the chat/payment policies; guests fall back
// to their remote IP so anonymous floods are still bounded.
static string PartitionKey(HttpContext httpContext) =>
    httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
```

- [ ] **Step 4: Apply the attributes to the five actions**

`src/MuktoAin.Web/Controllers/AccountController.cs` (POST overloads at lines 39-41 and 97-99):

```csharp
    [HttpPost]
    [EnableRateLimiting("auth")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
```

```csharp
    [HttpPost]
    [EnableRateLimiting("auth")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
```

(needs `using Microsoft.AspNetCore.RateLimiting;` added to the controller's usings)

`src/MuktoAin.Web/Controllers/ChatController.cs` (only `Ask` — the highest-cost endpoint; `New`/`Commit` are low-frequency and covered by CSRF + ownership checks):

```csharp
    // Ask a question. Body: { chatSessionId, question, language? }
    [HttpPost]
    [EnableRateLimiting("chat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest? body)
```

`src/MuktoAin.Web/Controllers/PaymentController.cs`:

```csharp
    [HttpPost]
    [EnableRateLimiting("payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Honorarium([FromBody] HonorariumPaymentRequest body)
```

```csharp
    [HttpPost]
    [EnableRateLimiting("payment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TopUp([FromBody] TopUpPaymentRequest body)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~RateLimitingPolicyTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, all tests pass.

- [ ] **Step 7: Live verification (optional, requires running app)**

Run: `dotnet run --project src/MuktoAin.Web`, then (PowerShell):

```powershell
1..7 | ForEach-Object {
    try { Invoke-WebRequest -Uri "http://localhost:<PORT>/Account/Login" -Method POST -SkipHttpErrorCheck | Select-Object StatusCode } 
    catch { $_.Exception.Response.StatusCode.value__ }
}
```
Expected: the 6th/7th requests report `429`. (Login POST without an antiforgery token would 400 first — if so, verify via the chat flow instead: send 11 rapid `POST /Chat/Ask` requests with a valid token from DevTools and observe a 429 on the 11th.)

- [ ] **Step 8: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-5]**` line (line 232) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 5: AUD-6 — Security Headers Middleware

**Files:**
- Create: `src/MuktoAin.Web/Middleware/SecurityHeadersMiddleware.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (register via `app.UseMiddleware<SecurityHeadersMiddleware>();`)

**Interfaces:**
- Consumes: nothing. Produces: four response headers on every response — `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Content-Security-Policy` (see Step 1).

**Audit basis:** Cross-Cutting #2 — no CSP, nosniff, frame-ancestors, or Referrer-Policy anywhere in the pipeline.

**Verification mode:** config/middleware change — **no unit test applies**; verification is inspection + build + live header check.

- [ ] **Step 1: Create the middleware**

Create `src/MuktoAin.Web/Middleware/SecurityHeadersMiddleware.cs`:

```csharp
// AUD-6: clickjacking / MIME-sniffing / referrer hardening on EVERY response
// (via Response.OnStarting, so error pages and static files are covered too).
// The CSP allowlist mirrors _Layout.cshtml's actual third-party surface:
//   - scripts: wwwroot/lib + https://unpkg.com (lucide icons) + inline theme
//     init scripts (hence 'unsafe-inline' on script-src)
//   - styles: wwwroot/assets/css + inline style="" attributes + Google Fonts CSS
//   - fonts: Google Fonts (fonts.gstatic.com)
public class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://unpkg.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
```

- [ ] **Step 2: Register it in `Program.cs`**

In `src/MuktoAin.Web/Program.cs`, add `using MuktoAin.Web.Middleware;` to the using block, then register the middleware immediately after `app.UseStatusCodePagesWithReExecute(...)` (line 254) and BEFORE `app.UseSession();`:

```csharp
app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

// AUD-6: security headers on every response (incl. static files + error pages).
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseSession();
```

- [ ] **Step 3: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, all tests pass.

- [ ] **Step 4: Live header verification**

Run: `dotnet run --project src/MuktoAin.Web`, then:

```powershell
(Invoke-WebRequest -Uri "http://localhost:<PORT>/" -UseBasicParsing).Headers
```
Expected keys/values: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and `Content-Security-Policy` matching the constant above.

Then open the site in a browser and confirm the console shows NO CSP violation errors — the home page loads Google Fonts, unpkg's lucide, inline scripts, and inline `style=""` attributes, all of which are covered by the allowlist. If a violation appears for a legitimately-used origin, extend ONLY that directive.

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-6]**` line (line 233) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 6: AUD-9 — Dead `documentType` Parameter Cleanup

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/ChatController.cs:276-285` (remove `ChatCommitRequest.DocumentType`) and `:223-232` (drop the argument)
- Modify: `src/MuktoAin.Application/Services/ChatService.cs:314-323` (drop the `string documentType` parameter — the parameter is never read in the method body)
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs:106-128` (remove the dead category→documentType derivation and the argument)
- Modify: `src/MuktoAin.Web/wwwroot/assets/js/chat.js:289-300` (drop `documentType` from the `/Chat/Commit` payload)
- Modify: `src/MuktoAin.Web/Views/Home/Index.cshtml:93-102` (remove the now-dead `#draft-doc-type` select)
- Test: `tests/MuktoAin.UnitTests/Controllers/ChatControllerTests.cs` (existing tests compile unchanged — they never set `DocumentType`)

**Interfaces:**
- Produces: `ChatService.CommitToCaseAsync` new signature (drops 8th parameter):
  ```csharp
  public async Task<ChatCommitResultDto> CommitToCaseAsync(
      int chatSessionId, int categoryId, byte districtId, string title,
      string? notificationEmail, bool isAnonymous, int? userId,
      CancellationToken ct = default)
  ```
  and `ChatCommitRequest` without the `DocumentType` property. DocumentGenerator already re-derives the true type from `Case.CategoryId` (the correct behavior per the audit).

**Audit basis:** Backend/Citizen #4 — the client-supplied `documentType` flows through the commit contract and is never read; a future reader would wrongly assume the client's value is honored.

- [ ] **Step 1: Remove the parameter from `ChatService.CommitToCaseAsync`**

In `src/MuktoAin.Application/Services/ChatService.cs`, change the signature (lines 314-323) to:

```csharp
    public async Task<ChatCommitResultDto> CommitToCaseAsync(
        int chatSessionId,
        int categoryId,
        byte districtId,
        string title,
        string? notificationEmail,
        bool isAnonymous,
        int? userId,
        CancellationToken ct = default)
```

(The body is unchanged — `documentType` was never referenced in it; `DocumentGenerator` derives the type from `Case.CategoryId`.)

- [ ] **Step 2: Update `ChatController.Commit`**

In `src/MuktoAin.Web/Controllers/ChatController.cs`, remove `body.DocumentType` from the call (around line 223):

```csharp
            var result = await _chatService.CommitToCaseAsync(
                body.ChatSessionId,
                body.CategoryId,
                body.DistrictId,
                body.Title,
                body.NotificationEmail,
                body.IsAnonymous,
                CurrentUserId(),
                HttpContext.RequestAborted);
```

And remove the `DocumentType` property from `ChatCommitRequest` (line 284) so the class reads:

```csharp
public class ChatCommitRequest
{
    public int ChatSessionId { get; set; }
    public int CategoryId { get; set; }
    public byte DistrictId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? NotificationEmail { get; set; }
    public bool IsAnonymous { get; set; }
}
```

- [ ] **Step 3: Update the legacy `CaseController.Submit` call**

In `src/MuktoAin.Web/Controllers/CaseController.cs`, the `documentType` derivation (lines 106-115) exists only to feed the dead parameter — remove it together with the `_categoryRepo` lookup it depends on, and drop the argument from the call (lines 120-128). The section becomes:

```csharp
        // Form answers become the case's transcript (unification rule: every
        // case = chat transcript + draft chain, regardless of entry path).
        var chatService = HttpContext.RequestServices
            .GetRequiredService<MuktoAin.Application.Services.ChatService>();
        var session = await chatService.GetOrCreateSessionAsync(currentUserId, null, vm.Title);
        await chatService.AppendMessageAsync(session.ChatSessionId, "user",
            $"বিষয়: {vm.Title}\nবিভাগ: {vm.Categories.FirstOrDefault(c => c.Value == vm.CategoryId.ToString())?.Text ?? vm.CategoryId.ToString()}\nবিবরণ: {vm.Description}", null);

        MuktoAin.Application.DTOs.ChatCommitResultDto result;
        try
        {
            // AUD-9: documentType was threaded here but never read — the
            // DocumentGenerator re-derives the real type from Case.CategoryId.
            result = await chatService.CommitToCaseAsync(
                session.ChatSessionId,
                vm.CategoryId,
                vm.DistrictId,
                vm.Title,
                notificationEmail: null,
                vm.IsAnonymous,
                currentUserId);
        }
```

(If `_categoryRepo` is still used elsewhere in `CaseController` — e.g. populating `vm.Categories` — keep the field and its DI registration; only remove the lines inside `Submit` shown above.)

- [ ] **Step 4: Update the client**

In `src/MuktoAin.Web/wwwroot/assets/js/chat.js`, `submitDraft()` (line 289) — remove the `documentType` line from the body:

```js
            body: JSON.stringify({
                chatSessionId: state.chatSessionId,
                categoryId: parseInt(el("draft-category").value, 10),
                districtId: parseInt(el("draft-district").value, 10),
                title: el("draft-title-input").value,
                notificationEmail: el("draft-email").value || null,
                isAnonymous: el("draft-anonymous").checked
            })
```

In `src/MuktoAin.Web/Views/Home/Index.cshtml` (lines 93-102), remove the dead "দলিলের ধরন / Document type" select block so the modal starts with the Category field:

```html
        <div style="display:flex; flex-direction:column; gap:14px">
            <div>
                <label class="form-label" for="draft-category" data-bn="বিভাগ" data-en="Category">বিভাগ</label>
```

(The real document type is derived server-side from the selected category — offering a client-side selector that is ignored is the "contract lies" problem the audit flagged.)

- [ ] **Step 5: Verify no other references remain**

Run: `rg -n "documentType|DocumentType" src/MuktoAin.Application/Services/ChatService.cs src/MuktoAin.Web/Controllers/ChatController.cs src/MuktoAin.Web/Controllers/CaseController.cs src/MuktoAin.Web/wwwroot/assets/js/chat.js src/MuktoAin.Web/Views/Home/Index.cshtml tests/MuktoAin.UnitTests`
Expected: no hits in those files (note: `DocumentType` legitimately remains on `GeneratedDocument`/`DocumentType` enum and templates — only the commit-flow threading is removed).

- [ ] **Step 6: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean (compiler catches any missed call site), all tests pass.

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-9]**` line (line 236) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 7: AUD-10 — Remove Remaining Optional Nullable DI Constructor Params + Dead MockData

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/DocumentController.cs:13-28,33,44-54,75,86-96` (required non-nullable constructor; drop the `== null` guards)
- Modify: `src/MuktoAin.Web/Controllers/PaymentController.cs:15-28,46-61` (required `ICaseRepository`; drop the null guard)
- Delete: `src/MuktoAin.Web/MockData.cs` (fully dead — grep shows zero references to `MockData.` outside the class definition)
- Test: `tests/MuktoAin.UnitTests/Controllers/DocumentControllerTests.cs`, `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` (constructor call sites)

**Interfaces:**
- Produces:
  - `DocumentController` constructor (all params required, ownership check always enforced):
    ```csharp
    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument> docRepo,
        DocumentService documentService,
        CaseService caseService)
    ```
  - `PaymentController` constructor:
    ```csharp
    public PaymentController(
        PaymentService paymentService,
        IRepository<PaymentOrder> orderRepo,
        ILogger<PaymentController> logger,
        ICaseRepository caseRepo)
    ```
  No DI registration changes needed — `IRepository<GeneratedDocument>`, `DocumentService`, `CaseService`, and `ICaseRepository` are all already registered in `Program.cs`.

**Audit basis:** Backend/Citizen #9 — silent mock-data substitution is gone (R-28 removed `GetMockDocument` and the catch-fallback), but the optional nullable constructor params (`DocumentController`'s `docRepo = null` / `documentService = null` / `caseService = null`, `PaymentController`'s `caseRepo = null`) still let the ownership/security checks be silently skipped, and `MockData.cs` remains as dead fabricated-legal-text weight.

- [ ] **Step 1: Verify `MockData` is unreferenced, then delete it**

Run: `rg -n "MockData" src tests`
Expected: only the class definition in `src/MuktoAin.Web/MockData.cs`. If any other hit appears, STOP and re-scope. Then delete the file: `Remove-Item src/MuktoAin.Web/MockData.cs`

- [ ] **Step 2: Update the failing tests first (constructor hardening)**

In `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs`, change the constructor call (line 32) to pass the required repository:

```csharp
        _controller = new PaymentController(
            paymentService,
            _orderRepo.Object,
            Mock.Of<ILogger<PaymentController>>(),
            Mock.Of<ICaseRepository>());
```

Also update any OTHER `new PaymentController(...)` call sites in the file (there are more in the later ownership tests) the same way — find them with `rg -n "new PaymentController" tests`.

In `tests/MuktoAin.UnitTests/Controllers/DocumentControllerTests.cs`, replace the constructor (lines 15-31) so the controller always has a real `CaseService` (mocked repos) and an authenticated owner matching the test cases. Add usings `using System.Security.Claims;`, `using MuktoAin.Application.Services;`, `using MuktoAin.Domain.Interfaces;`, `using MuktoAin.Domain.Interfaces.Services;` at the top, and replace the class fields + constructor with:

```csharp
public class DocumentControllerTests
{
    private readonly Mock<IRepository<GeneratedDocument>> _docRepo;
    private readonly Mock<ICaseRepository> _caseRepo;
    private readonly DocumentController _controller;

    public DocumentControllerTests()
    {
        _docRepo = new Mock<IRepository<GeneratedDocument>>();
        _caseRepo = new Mock<ICaseRepository>();

        // AUD-10: the controller now ALWAYS runs the ownership check through
        // CaseService, so the fixture authenticates user 42 and backs the
        // service with a mock case repo the tests point at per-case.
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<string>()))
                  .Returns<string>(s => s ?? string.Empty);
        var categoryRepo = new Mock<IRepository<CaseCategory>>();
        categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                    .ReturnsAsync(new CaseCategory { CategoryId = 1, NameBn = "শ্রম", NameEn = "Labour" });
        var districtRepo = new Mock<IRepository<District>>();
        districtRepo.Setup(r => r.GetByIdAsync(It.IsAny<byte>()))
                    .ReturnsAsync(new District { DistrictId = 1, Name = "Dhaka" });
        var caseService = new CaseService(
            _caseRepo.Object, categoryRepo.Object, districtRepo.Object, encryption.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42")
        }));

        _controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            null!, // DocumentService — only touched on the approved-PDF stream path
            caseService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    // Grants the authenticated user (42) ownership of the given case so the
    // always-on CaseService ownership check in Preview/Download passes.
    private void SetupOwnedCase(int caseId)
    {
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(caseId)).ReturnsAsync(new Case
        {
            CaseId = caseId,
            UserId = 42,
            CategoryId = 1,
            DistrictId = 1,
            Status = CaseStatus.Submitted
        });
    }
```

(`null!` for `DocumentService` matches the existing test-suite precedent — `ChatControllerTests` passes `null!` for `DocumentService` — and is only valid because Preview and the not-approved Download path never touch it. The two explicit-Forbid tests below build their own controller.)

Then update the affected tests:
- `Preview_WhenInvalidId_ReturnsNotFound` — unchanged.
- `Preview_WhenDocumentFoundInRepo_ReturnsViewWithDocumentData` — add `SetupOwnedCase(42);` as its first line.
- `Preview_WhenApproved_AllowsPdfDownload` — add `SetupOwnedCase(42);` as its first line.
- `Download_WhenNotApproved_BlocksDownloadAndRedirectsWithWarning` — add `SetupOwnedCase(55);` as its first line.
- `Download_WhenInvalidId_ReturnsNotFound` — unchanged.
- `Preview_WhenDocumentNotFoundInRepo_ReturnsNotFound_WithoutMockFallback` — unchanged.
- `Preview_WhenCaseServicePresentAndUserUnauthorized_ReturnsForbid` and `Download_WhenCaseServicePresentAndUserUnauthorized_ReturnsForbid` — replace their local `new DocumentController(..., null, caseService)` calls (lines 145-152 / 179-186) to drop the now-invalid `null` third argument; the `caseService` local variable stays:

```csharp
        var controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            null!, // DocumentService — not reached on the Forbid path
            caseService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
```

- [ ] **Step 3: Harden the `DocumentController` constructor**

In `src/MuktoAin.Web/Controllers/DocumentController.cs`, replace lines 11-28:

```csharp
public class DocumentController : Controller
{
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly DocumentService _documentService;
    private readonly CaseService _caseService;
    private readonly ILogger<DocumentController> _logger;

    // AUD-10: all dependencies are required — the old optional/nullable
    // parameters let the ownership check silently disappear from Preview and
    // Download depending on what the DI container happened to provide.
    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument> docRepo,
        DocumentService documentService,
        CaseService caseService)
    {
        _logger = logger;
        _docRepo = docRepo;
        _documentService = documentService;
        _caseService = caseService;
    }
```

Then simplify `Preview` (remove the `_docRepo == null` guard and the `_caseService != null` wrapper — the ownership check now always runs):

```csharp
    [HttpGet]
    public async Task<IActionResult> Preview(int id, string? code = null)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var doc = await _docRepo.GetByIdAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        var currentUserId = GetCurrentUserId();
        var currentRole = GetCurrentUserRole();
        var trackingCode = ResolveTrackingCode(doc.CaseId, code);
        var caseDetail = await _caseService.GetCaseDetailAsync(doc.CaseId, currentUserId, currentRole, trackingCode);
        if (caseDetail == null)
        {
            return Forbid();
        }
```

Make the identical two changes in `Download` (drop the `_docRepo == null` guard at line 75; unwrap the `_caseService != null` block at lines 86-96 into an unconditional ownership check):

```csharp
    [HttpGet]
    public async Task<IActionResult> Download(int id, string? code = null)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var doc = await _docRepo.GetByIdAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        var currentUserId = GetCurrentUserId();
        var currentRole = GetCurrentUserRole();
        var trackingCode = ResolveTrackingCode(doc.CaseId, code);
        var caseDetail = await _caseService.GetCaseDetailAsync(doc.CaseId, currentUserId, currentRole, trackingCode);
        if (caseDetail == null)
        {
            return Forbid();
        }
```

- [ ] **Step 4: Harden the `PaymentController` constructor**

In `src/MuktoAin.Web/Controllers/PaymentController.cs`, replace lines 13-28:

```csharp
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
```

And unwrap the ownership check in `Honorarium` (lines 46-61) — same logic, no null guard:

```csharp
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
```

- [ ] **Step 5: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean (compiler verifies no call sites were missed), all tests pass — including the existing IDOR regression tests (`Honorarium_WhenUserDoesNotOwnCase_ReturnsForbid`, `Preview_WhenCaseServicePresentAndUserUnauthorized_ReturnsForbid`).

- [ ] **Step 6: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-10]**` line (line 237) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

### Task 8: AUD-12 — Dashboard Log Severity Fix

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs:584` (one word: `LogInformation` → `LogError`)

**Interfaces:**
- Consumes/produces: nothing — pure log-level correction inside the private `BuildAdminDashboardViewModelAsync` catch block.

**Audit basis:** Admin Scope #3 — a genuine DB/query failure building the admin dashboard is logged at Information level, so it never trips error-level alerting.

**Verification mode:** one-line severity fix — **no unit test applies** (the method is private and reachable only through the Dashboard action, which requires the full DB-backed fixture; the existing `AdminControllerTests` + `dotnet build` + inspection cover it).

- [ ] **Step 1: Change the severity**

In `src/MuktoAin.Web/Controllers/AdminController.cs`, line 584, change:

```csharp
            _logger.LogInformation("Dashboard aggregate build failed: {Message}", ex.Message);
```

to:

```csharp
            // AUD-12: an aggregate-query failure is a real error — log it at
            // error level so it trips alerting instead of disappearing.
            _logger.LogError(ex, "Dashboard aggregate build failed");
```

(Note the deliberate upgrade from `ex.Message` to structured `ex` logging so the stack trace is preserved — matches the pattern used in `PaymentController.Honorarium`'s catch.)

Out of scope on purpose: the `LogInformation` calls at lines 627 and 661 are the *health-probe* catches (Dashboard displays their status as a UI widget by design) — the audit flags only the dashboard-aggregate catch at line 584.

- [ ] **Step 2: Build and run the full unit suite**

Run: `dotnet build MuktoAin.slnx && dotnet test tests/MuktoAin.UnitTests`
Expected: build clean, all tests pass.

- [ ] **Step 3: Record completion in plans/Dependency_plan.md**

Flip the `- [ ] **[AUD-12]**` line (line 239) to `- [x]` and wrap the entire line in `~~strikethrough~~`.

---

## Final Verification (all tasks complete)

- [ ] **Step 1: Full clean build**

Run: `dotnet build MuktoAin.slnx`
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Full unit suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: all pass.

- [ ] **Step 3: Security-analyzer spot check (optional but recommended)**

Run: `dotnet build MuktoAin.slnx -p:AnalysisLevel=latest-all -p:EnableNETAnalyzers=true -p:AnalysisMode=AllEnabledByDefault`
Expected: the 5 `CA5391` CSRF warnings from the audit no longer appear.

- [ ] **Step 4: Confirm every task recorded its completion**

Verify `plans/Dependency_plan.md` lines for AUD-1, AUD-2, AUD-3, AUD-5, AUD-6, AUD-9, AUD-10, AUD-12 are `- [x]` and struck through. Leave all changes uncommitted in the working tree — Shads commits.
