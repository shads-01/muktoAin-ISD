# MuktoAin — Full-Repository Static Audit Report

**Date:** 2026-09-12
**Scope:** Non-destructive static review of the entire repository. No files were edited, refactored, or deleted as part of this audit.
**Stack confirmed:** ASP.NET Core MVC (.NET 8), Razor Views + Bootstrap 5 + vanilla JS (no SPA framework — "Frontend" below means Views/ViewModels/wwwroot JS, "Backend" means Controllers/Application Services/Infrastructure/Domain).
**Method:** `dotnet build` (clean, 0 warnings under default project settings) + a full-analyzer rebuild (`-p:AnalysisLevel=latest-all -p:EnableNETAnalyzers=true -p:AnalysisMode=AllEnabledByDefault`, 296 warnings surfaced) + manual read-through of every controller, the services/repositories they call, `Program.cs`, and the client JS that drives Chat/Payment. Severity is called out per finding; nothing here was auto-fixed.

---

## Executive Summary

The most serious problems are three **broken-access-control / IDOR** paths that let one citizen read or corrupt another citizen's private legal data, one that lets an anonymous visitor read admin-only infrastructure telemetry, and one correctness bug that breaks the app's core feature outright for most users. All five are concrete today, not theoretical:

| # | Where | Impact |
|---|---|---|
| 1 | `ChatController.Ask` / `Commit` | Any user can pass another citizen's `chatSessionId` and read/hijack/steal their private legal chat, or "commit" it into a case under the attacker's own account. |
| 2 | `DocumentController.Preview` / `Download` | Zero auth, zero ownership check — any `id` returns any citizen's legal draft or approved PDF. |
| 3 | `PaymentController.Honorarium` / `Status` | Any user can create a paid honorarium order against an arbitrary `CaseId`, or read any other user's payment details by id. |
| 4 | `AdminController.EmbeddingProgress` / `GeminiKeyStatus` | Marked `[AllowAnonymous]` on an `[Authorize(Roles="Admin")]` controller — leaks Gemini key usage and embedding internals to the public internet. |
| 5 | `DocumentGenerator` / `Program.cs` template registration | 3 of the app's 4 case categories (General Diary, RTI Request, Consumer Complaint) cannot generate a document draft at all — the flagship feature only works for Labour Complaints. |

*(Added post-publish, prompted by a direct question — see Backend/Citizen #1a below for detail and root cause.)*

Also confirmed: the CSRF gap the analyzer flags (CA5391) is real end-to-end — the JS in `chat.js`/`main.js` sends no anti-forgery token to match. A field-level encryption bug tracked from an earlier session (`[[rag-pipeline-latent-bugs-2026-08-30]]`) still reproduces from the current `Program.cs`/`EncryptionService` wiring — `AddDataProtection()` has no persisted key ring, so encrypted `Case.Title`/`Description` become permanently unreadable across restarts/containers. No entity in the 14-entity schema carries a concurrency token, so every "claim" or status-transition flow (lawyer document claim, AI quota reservation) is a textbook TOCTOU race under real concurrency.

---

## Frontend

### Citizen Scope

- **[Medium] No CSRF token sent on JSON POSTs.** `wwwroot/assets/js/chat.js` (lines ~239, ~289, ~473) calls `fetch("/Chat/Ask", …)`, `fetch("/Chat/Commit", …)`, and `fetch("/Payment/TopUp", …)` with only `Content-Type: application/json` — no `RequestVerificationToken`/custom header is attached. This is the client half of the backend CSRF gap below; fixing only the backend without also wiring a token here would break these calls.
- **[Low] Silent failure on chat load.** `chat.js` catch blocks for `/Chat/Recent`, `/Chat/New`, `/Chat/Quota` (`.catch(function () {})`) swallow all errors with no user-facing message — a citizen mid-conversation who loses connectivity gets no indication the composer is now non-functional.
- **[Low] No client-side guard against resubmitting `Ask` while a request is in flight** (not verified to be debounced) — combined with the backend quota race below, a fast double-click can consume more than one "turn" for a single question before the UI updates the counter.
- **[Info] No "unsaved changes" warning** when a citizen navigates away from an in-progress draft edit (`Case/Result.cshtml` `SaveDraft` flow) — not destructive, but edits can be silently lost.

### Lawyer Scope

- **[Low] `Queue` view has no pagination controls** — consistent with the backend (`GetQueueAsync` loads the entire under-review pool with no paging), so as the review backlog grows the page will render an ever-larger unpaginated table. `History` (same controller) does paginate — this view was simply never brought in line.
- **[Info] `LawyerReviewViewModel` review form is fine** (comments mandatory, edited text required for `EditedApproved`) — no missing-validation issue found here.

### Admin Scope

- **[Medium] `Admin/Users` view has no pagination** — the controller (`AdminController.Users`) returns every user unfiltered by default; the view must render the full table, which will not scale past a few hundred users.
- **[Low] `Admin/AiLogs` view has no page control** — backed by a hardcoded `Take(200)` in the controller; older log entries are simply unreachable from the UI, not paged to.
- **[Info] Confirmation dialogs are present** where expected — `Admin/Users.cshtml`, `Admin/Lawyers.cshtml`, `Admin/Scenarios.cshtml`, and `Admin/Transactions.cshtml` all gate their destructive actions (Suspend, Verify/Reject, Delete Mapping, Refund/Mark Paid) behind a JS `confirm()`. No gap found here.

---

## Backend

### Citizen Scope

1. **[Critical] Broken access control / IDOR — `ChatController.Ask` and `Commit` never verify session ownership.**
   `ChatController.Messages` (the one endpoint that gets it right) checks:
   ```csharp
   var allowed = session.UserId == userId || (session.UserId == null && session.SessionKey == key);
   if (!allowed) return Forbid();
   ```
   `Ask` (line 53) and `Commit` (line 197) take a client-supplied `ChatSessionId` (a plain auto-increment `int`) and pass it straight into `ChatService.AskAsync`/`CommitToCaseAsync` with **no equivalent check**. Consequences:
   - `Ask`: an attacker who knows or enumerates another citizen's `chatSessionId` gets `AppendMessageAsync` called against *that* session — their message is injected into a stranger's private legal conversation.
   - `Commit`: `CommitToCaseAsync` (`ChatService.cs:314`) reads *all* messages of the target session (`GetMessagesAsync`) — including the victim's original private legal description — concatenates them into `unifiedDescription`, and creates a new `Case` **owned by the attacker's own `userId`**. The victim's session is simultaneously flipped to `ChatSessionStatus.Committed`, silently breaking their ability to continue it (`GetOrCreateSessionAsync` only resumes `InProgress` sessions).
   This is a full cross-account confidentiality + integrity break for the single most sensitive data type in the app (a citizen's legal problem description), reachable by both anonymous and authenticated users. Fix: mirror `Messages`'s ownership check in both `Ask` and `Commit`.

2. **[Critical] Broken access control / IDOR — `DocumentController.Preview` and `Download` have no authorization or ownership check at all.**
   Neither action carries `[Authorize]`, and neither cross-references `GeneratedDocument.CaseId` against the caller (no call to `CaseService.GetCaseDetailAsync` the way `CaseController` does for the same underlying data). `Preview(int id)` and `Download(int id)` (`DocumentController.cs:27,71`) load the document purely by its own PK and render/stream it to whoever asks. Any citizen (or anonymous visitor) can enumerate `id` and read — or, once a document is `Approved`, download the final PDF of — any other citizen's labour complaint, GD application, RTI request, or consumer complaint. `GeneratedDocument.CaseId` exists specifically to make the ownership check possible; it's simply never used here.

3. **[Critical] Document draft generation is broken for 3 of the app's 4 case categories.**
   `Program.cs:200` registers exactly one template: `builder.Services.AddScoped<IDocumentTemplate, LabourComplaintTemplate>();` — and `Documents/Templates/` contains exactly that one file. `DocumentGenerator` maps `Case.CategoryId` to a `DocumentType` (1=Labour, 2=GeneralDiary, 3=RtiRequest, 4=ConsumerComplaint) and does:
   ```csharp
   if (!_templates.TryGetValue(docType, out var template))
       throw new InvalidOperationException($"No template found for document type {docType}");
   ```
   Only `DocumentType.LabourComplaint` resolves. Any General Diary, RTI Request, or Consumer Complaint case throws here, unhandled, every time `DocumentService.GenerateDocumentAsync` is called. Blast radius:
   - `ChatController.Commit` catches the exception but leaks `ex.Message` (`"No template found for document type GeneralDiary"`) straight to the citizen as JSON.
   - The legacy `CaseController.Submit` fallback swallows the same exception from `ChatService.CommitToCaseAsync`, silently creating a case with **no document at all**.
   - `CaseController.Result`'s "generate on first view" fallback (for exactly that no-document case) hits the same throw **uncaught** — an unhandled 500 for the citizen viewing their own case.
   The flagship feature — AI drafts a structured legal document — only works for one of the four case types the intake form itself offers. Fix: add `GeneralDiaryTemplate`, `RtiRequestTemplate`, `ConsumerComplaintTemplate` (same shape as `LabourComplaintTemplate`) and register all four.
   *Note: this gap was already known — `docs/superpowers/specs/2026-09-10-document-styling-language-toggle-design.md` lists it as an explicit out-of-scope item ("tracked already as an open gap in DocumentGenerator"). It was never given its own entry in `plans/Dependency_plan.md`, which is why it didn't surface as a completed/tracked task — flagging it here formally.*

4. **[Low] Dead parameter — `documentType` is threaded through the commit flow and never read.**
   `ChatCommitRequest.DocumentType` (client-supplied, defaults to `"LabourComplaint"`) flows `ChatController.Commit` → `ChatService.CommitToCaseAsync(..., string documentType, ...)` — the parameter is never referenced in the method body. `DocumentService`/`DocumentGenerator` independently re-derive the real type from `Case.CategoryId`, which is correct, but the request contract lies about what it does: a client can send any `documentType` value and it has zero effect. Harmless today only because the real value happens to be recomputed correctly elsewhere; worth removing so a future reader doesn't assume the client's value is honored.

5. **[High] CSRF — `ChatController` and `PaymentController` POST actions carry no `[ValidateAntiForgeryToken]`.**
   Confirmed both by the Roslyn security analyzer (`CA5391`, 5 hits: `ChatController.New/Ask/Commit`, `PaymentController.Honorarium/TopUp`) and by the client JS sending no matching token (see Frontend/Citizen above). Every other POST-handling controller in the app (`Case`, `Account`, `Admin`, `Lawyer`) does this correctly — the gap is specific to the two `[ApiController]`-style controllers, which appear to have been written under the (incorrect, for cookie-auth JSON endpoints) assumption that `[ApiController]` alone is CSRF-safe.

6. **[High] IDOR in `PaymentController.Honorarium` — no case-ownership check.**
   `Honorarium([FromBody] HonorariumPaymentRequest body)` (`PaymentController.cs:34`) takes `body.CaseId` from the client and calls `CreateHonorariumOrderAsync(body.CaseId, userId, body.Amount)` with no check that `userId` (which can also be `null`, i.e. anonymous) owns that case. Any caller can mark **any** case's `HonorariumPaid = true` for a self-chosen `amount` (only constrained to `> 0`), since the sandbox flow auto-marks the order `Paid` immediately. `PaymentController.Status(int id)` has the same gap — any id returns another user's order amount, commission, and gateway reference.

7. **[High] Data Protection key ring not persisted — breaks field-level PII encryption across restarts.**
   `Program.cs:159` calls bare `builder.Services.AddDataProtection();` with no `.PersistKeysToFileSystem(...)`, `.PersistKeysToDbContext<AppDbContext>()`, or `.SetApplicationName(...)`. Combined with the project shipping a `Dockerfile`, any redeploy/restart of a container that doesn't mount a persistent volume for the default key-storage path silently rotates the encryption key. `CaseService.SafeDecrypt` / `LawyerReviewService.SafeDecrypt` then catch the resulting `CryptographicException` and **return the raw ciphertext as if it were the plaintext title/description** — a citizen or lawyer would see a garbled base64-ish blob rendered as their case title with no error surfaced. This reproduces the encryption failure mode already logged in memory as `[[rag-pipeline-latent-bugs-2026-08-30]]`; it does not appear to have been fixed since.

8. **[Medium] Race condition — `AiBudgetService.TryReserveTurnAsync` is not atomic.**
   ```csharp
   public async Task<bool> TryReserveTurnAsync(int? userId, string? sessionKey) {
       var snapshot = await GetRemainingToday(userId, sessionKey);
       return snapshot.RemainingToday > 0;
   }
   ```
   This only counts existing `AI_LOG` rows; it does not itself write a reservation row. Two concurrent `Ask` requests (double-click, two tabs) can both read `RemainingToday == 1`, both pass, and both proceed to call the metered Gemini pipeline — a genuine TOCTOU quota-bypass, not just a UI cosmetic issue.

9. **[Medium] Mock data silently substituted for real data on failure.** `DocumentController.Preview`/`Download` accept `IRepository<GeneratedDocument>? docRepo = null` / `DocumentService? documentService = null` as **optional, nullable** constructor parameters, and on any exception from the real repository call (`catch (Exception ex) { LogWarning; }`) fall through to `GetMockDocument(id)` — a hardcoded fabricated legal complaint. In a legal-aid product, silently showing a citizen fabricated document text in place of a real DB error is a materially dangerous failure mode, not just a code smell.

10. **[Low] `CA1031` broad `catch (Exception)` blocks** in `CaseController.Submit` (falls back to a legacy submission path on *any* exception from `CommitToCaseAsync`, masking real bugs as "use the old path"), `DocumentController.Preview/Download`, and `PaymentController.Honorarium/TopUp` (returns `ex.Message` directly to the client in a 500 body — minor information disclosure of internal exception text).

11. **[Missing feature] No email verification on registration.** `AccountController.Register` activates the account immediately (`AccountStatus.Active`) with no confirmation link — anyone can register with an email address they don't control. Combined with `Case.NotificationEmail`, this also means case-status notifications could be configured to an address the registrant doesn't own.

12. **[Missing feature] No rate limiting anywhere** (see Cross-Cutting) compounds finding #1/#6 above — nothing throttles the `chatSessionId`/`CaseId` enumeration needed to exploit them at scale.

### Lawyer Scope

1. **[Medium] Race condition on document claim — no DB-level concurrency control.**
   `LawyerReviewService.ClaimAsync` (`LawyerReviewService.cs:100`) and `SubmitReviewAsync` both do a plain read-check-write:
   ```csharp
   var d = await _docRepo.GetByIdAsync(documentId);
   if (d == null || d.Status != DocumentStatus.UnderReview) return false;
   if (d.AssignedLawyerProfileId.HasValue && d.AssignedLawyerProfileId != lawyerProfileId) return false;
   d.AssignedLawyerProfileId = lawyerProfileId;
   await _docRepo.SaveChangesAsync();
   ```
   The comment above the class calls this "claim-based optimistic lock", but there is no `RowVersion`/`[Timestamp]`/`[ConcurrencyCheck]` column anywhere in the schema (confirmed — no entity or EF configuration references one). Under real concurrent load, two lawyers opening the same queued document in the same instant can both pass the `HasValue` check before either write commits, both proceeding into the review workspace believing they hold an exclusive claim.
2. **[Low] `LawyerController.Resubmit` loses its validation error on redirect.** On a blank bar number it calls `ModelState.AddModelError(...)` and then `return RedirectToAction(nameof(Status))` — `ModelState` does not survive a redirect (no `TempData`/`ITempDataDictionary` bridge for it here, unlike every other validation-failure path in this controller which uses `TempData["Error"]`), so the lawyer is bounced back to `Status` with no visible explanation of why nothing changed.
3. **[Low] `RequestPayoutAsync` has no duplicate/pending-request guard.** A lawyer can call `RequestPayout` repeatedly; each call unconditionally inserts a new `PayoutRequest` for the *current* `Balance` (which does not itself decrease until an admin actually approves a payout), so multiple pending payout requests can be queued against the same underlying balance before an admin catches up.
4. **[Missing feature] No pagination on the review queue.** `LawyerReviewService.GetQueueAsync` / `LawyerController.Queue` load every `UnderReview` document into memory on every page view — `History` on the same controller paginates correctly, so this is an inconsistency, not a design constraint.

### Admin Scope

1. **[Critical] Broken access control — two Admin-only endpoints are marked `[AllowAnonymous]`.**
   `AdminController` is class-decorated `[Authorize(Roles = "Admin")]`, but:
   ```csharp
   [HttpGet] [AllowAnonymous]
   public async Task<IActionResult> EmbeddingProgress() { ... }

   [HttpGet] [AllowAnonymous]
   public IActionResult GeminiKeyStatus() { ... }
   ```
   Both explicitly opt back out of the class-level authorization. `GeminiKeyStatus` in particular returns, to any unauthenticated caller, every Gemini API key's label, per-minute token usage, and rate-limit "parked" status — internal operational telemetry that should never be public. `EmbeddingProgress` similarly exposes ingestion/embedding pipeline internals. If these were intended for a public status widget, that call needs to be re-scoped (e.g., a deliberately reduced public DTO), not the whole admin action.
2. **[Medium] No idempotency guard on financial admin actions.** `PaymentService.MarkPaidAsync` unconditionally overwrites `Status/GatewayRef/PaidAt` regardless of the order's current status (no check that it isn't already `Paid`/`Refunded`); `AdminController.MarkOrderPaid`/`RefundOrder`/`ApprovePayout` all call through with no double-submission protection. Two admin clicks (or a resent form) can double-process the same order, and since `GetLawyerEarningsAsync` sums *all* `Paid` orders, a duplicate mark-paid inflates a lawyer's payable balance.
3. **[Low] Dashboard failures are logged below their real severity.** `BuildAdminDashboardViewModelAsync`'s outer `catch (Exception ex)` calls `_logger.LogInformation(...)` (not `LogError`/`LogWarning`) when the aggregate-stat queries fail — a genuine DB/query failure on the admin dashboard will not trip any error-level alerting.
4. **[Missing feature] No pagination.** `Users(string? role)` returns every user in the system on one page; `AiLogs` hardcodes `Take(200)` with no page parameter. Both will degrade as the user/log tables grow.
5. **[Missing feature] No real administrative audit log.** The "Audit Logs" panel on the Admin dashboard (`AdminDashboardViewModel.AuditLogs`) is populated entirely from `AI_LOG` rows (`AdminController.cs:571`) — it records AI calls, not admin actions. There is no entity or service anywhere in the codebase that records *who* suspended a user, approved/rejected a lawyer, refunded a payment, or deleted a scenario mapping, and no timestamp/actor is persisted for those actions beyond what a citizen/lawyer might separately observe as a side effect. For a platform whose core value proposition is a human-in-the-loop legal safeguard, the absence of a real audit trail for the humans overseeing that safeguard is a significant gap.
6. **[Low] `DeleteScenario` is a hard delete** with no soft-delete flag and, per #5 above, no audit record of the deletion.

---

## Cross-Cutting Security & Architectural Concerns

1. **No rate limiting anywhere in `Program.cs`.** There is no `AddRateLimiter`/`UseRateLimiter` (or any third-party equivalent) registered. Beyond ASP.NET Identity's built-in 5-failed-attempt/15-minute lockout on `PasswordSignInAsync`, nothing throttles `Login`, `Register`, `ForgotPassword` (email-existence/reset-flood risk once SMTP is wired up), `Chat/Ask` (each miss costs a metered Gemini call), or the payment endpoints. This directly amplifies the enumeration-based IDORs above (#1/#4 under Citizen Backend).
2. **No security-header middleware.** No CSP, `X-Content-Type-Options`, `X-Frame-Options`/`frame-ancestors`, or `Referrer-Policy` is set anywhere in the pipeline — standard clickjacking/MIME-sniffing hardening is entirely absent.
3. **CSRF protection is inconsistently applied by controller style, not by policy.** The traditional Razor-form controllers (`Case`, `Account`, `Admin`, `Lawyer`) all correctly carry `[ValidateAntiForgeryToken]`; the two `[ApiController]`-attributed, JSON-body controllers (`Chat`, `Payment`) carry none, and nothing in the codebase (a base class, a filter, a policy convention) enforces the same rule across both styles. This is an architectural gap as much as a per-endpoint bug: the next JSON controller added will most likely repeat it.
4. **No optimistic-concurrency columns anywhere in the 14-entity schema.** Confirmed via `grep` across `Entities/` and `Data/Configurations/` — no `RowVersion`, `[Timestamp]`, or `[ConcurrencyCheck]` usage at all. Every check-then-write flow in the app (`LawyerReviewService.ClaimAsync`, `CaseService.TransitionStatusAsync`, `AiBudgetService.TryReserveTurnAsync`, `PaymentService.MarkPaidAsync`) is therefore a potential lost-update/race under real concurrent traffic, not just the ones called out individually above.
5. **Data Protection key persistence is unconfigured** (see Citizen Backend #5) — a deployment-environment problem (Docker/multi-instance) with an application-level consequence (silent, irrecoverable PII corruption) that spans the whole encrypted-field surface (`Case.Title`, `Case.Description`, anything else routed through `IEncryptionService`).
6. **Full-analyzer sweep found 296 warnings** that the default project configuration does not surface (`AnalysisLevel`/`EnableNETAnalyzers` are not set in any `.csproj`, so `dotnet build` normally reports 0 warnings). Of those, the security-relevant ones (`CA5391` × 5 CSRF gaps, `CA2000` × 1 undisposed `QdrantClient` in `AdminController.cs:652`, `CA1031` × 11 overly broad catches) are exactly the class of bug this audit is being asked to find — recommend turning on at least `AnalysisLevel=latest` with the security rule category enabled in CI, so gaps like the CSRF one are caught automatically going forward rather than only via a manual audit like this one.
7. **Session/auth token lifetime is reasonable but has no absolute cap.** `ConfigureApplicationCookie` sets `ExpireTimeSpan = 8h` with `SlidingExpiration = true` and no `MaxAge`/absolute-expiration policy — acceptable for the current academic scope, noted here only because "token expiration/refresh handling" was explicitly in scope for this audit.

---

## Notes on Method

- `dotnet build MuktoAin.slnx` — succeeded, 0 warnings/errors under default settings.
- `dotnet build MuktoAin.slnx -p:AnalysisLevel=latest-all -p:EnableNETAnalyzers=true -p:AnalysisMode=AllEnabledByDefault` — succeeded, 296 warnings; the security/reliability-relevant subset is quoted inline above (full raw log was not committed to the repo, per the non-destructive/no-artifact-litter scope of this audit — rerun the command above to reproduce it).
- No automated frontend linter/typechecker applies here (no `package.json`/JS build step in this project — the two JS files under `wwwroot/assets/js` are hand-authored and unbundled), so the Frontend findings above are from manual read-through of `chat.js`/`main.js` and the Razor views, cross-referenced against the actions they call.
- This report does not re-derive the previously-logged RAG/embedding-dimension and keyword-fallback issues in full; see `[[rag-pipeline-latent-bugs-2026-08-30]]` for that history. The encryption half of that finding is re-confirmed above because it traces directly to a `Program.cs`/`EncryptionService` wiring gap this audit was asked to check.
