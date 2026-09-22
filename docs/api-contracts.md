# MuktoAin (মুক্ত আইন) — API Contracts (Controller → Service Mapping)

This document specifies the implicit and explicit service contracts for the `MuktoAin.Web` presentation layer controllers. Each section defines the expected HTTP route, parameters, ViewModels, and injected Application/Domain services.

---

## 1. AccountController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Account/Login` | `GET` | AllowAnonymous | `string? returnUrl` | — | Renders login page with demo quick-fill buttons |
| `/Account/Login` | `POST` | AllowAnonymous | `LoginViewModel model, string? returnUrl` | `SignInManager<User>`, `UserManager<User>` | Authenticates user with lockout tracking and role-based redirect |
| `/Account/Register` | `GET` | AllowAnonymous | — | — | Renders registration page with citizen/lawyer role selection |
| `/Account/Register` | `POST` | AllowAnonymous | `RegisterViewModel model` | `UserManager<User>`, `IRepository<LawyerProfile>` | Creates user account with password complexity validation; creates `Pending` lawyer profile for lawyers |
| `/Account/Logout` | `POST` | AllowAnonymous | — | `SignInManager<User>` | Signs out user, invalidates session cookie, redirects to Home |

---

## 2. CaseController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Case/Submit` | `GET` | AllowAnonymous | — | `IRepository<CaseCategory>`, `IRepository<District>` | Renders citizen intake form with category & district dropdowns |
| `/Case/Submit` | `POST` | AllowAnonymous | `CaseSubmitViewModel model` | `ICaseService`, `IRightsExplanationService` | Submits citizen problem, encrypts PII, triggers RAG analysis, returns tracking code / case ID |
| `/Case/Result/{id}` | `GET` | AllowAnonymous | `int id` | `ICaseService`, `IAiOrchestrationService`, `IAiLogService` | Renders rights analysis, cited statutory sections, and AI draft document (cached in `AI_LOG` to prevent re-generation) |
| `/Case/Track` | `GET` | AllowAnonymous | `string? code` | `ICaseService` | Lists logged-in user cases or looks up single anonymous case via GUID tracking code (FR-8) |

---

## 3. SearchController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Search` | `GET` | AllowAnonymous | `string? q, int page = 1, int? actId` | `ISearchService` | Standalone full-text search across 1,484 Bangladesh Acts with pagination and section full-text reading modal |

---

## 4. CategoryController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Category` | `GET` | AllowAnonymous | — | `ICategoryService` | Lists 4 legal categories (Labour, GD, RTI, Consumer) with bilingual descriptions |
| `/Category/Details/{id}` | `GET` | AllowAnonymous | `int id` | `ICategoryService` | Displays category details, statutory basis, and common legal action steps |

---

## 5. DocumentController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Document/Preview/{id}` | `GET` | AllowAnonymous | `int id` | `IRepository<GeneratedDocument>`, `IDocumentService` | Full-page document preview with parchment styling, Surface 3 disclaimer stamp, and review status |
| `/Document/Download/{id}` | `GET` | AllowAnonymous | `int id` | `IDocumentService`, `IPdfExportService` | Generates PDF download using QuestPDF; strictly gated behind lawyer approval (`Approved` / `EditedApproved`) |

---

## 6. LawyerController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Lawyer/Apply` | `GET` | `[Authorize(Roles = "Lawyer")]` | — | — | Verification application form for bar registration |
| `/Lawyer/Apply` | `POST` | `[Authorize(Roles = "Lawyer")]` | `LawyerApplyViewModel model` | `ILawyerVerificationService` | Submits Bar Council registration number and specialization for admin review |
| `/Lawyer/Queue` | `GET` | `[Authorize(Roles = "Admin,Lawyer")]` | — | `ILawyerReviewService`, `IRepository<GeneratedDocument>` | Displays queue of AI drafts awaiting advocate review with claim lock |
| `/Lawyer/Review/{id}` | `GET` | `[Authorize(Roles = "Admin,Lawyer")]` | `int id` | `ILawyerReviewService`, `IRepository<GeneratedDocument>` | Side-by-side redline review interface comparing draft vs edited text |
| `/Lawyer/SubmitReview` | `POST` | `[Authorize(Roles = "Admin,Lawyer")]` | `LawyerReviewViewModel model` | `ILawyerReviewService` | Records lawyer decision (`Approved`, `EditedApproved`, `Rejected`), updates document status and case lifecycle |

---

## 7. AdminController

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Admin/Dashboard` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IAdminAnalyticsService`, `AppDbContext` | System metrics, category breakdowns, district distribution, AI failure rates |
| `/Admin/Analytics` | `GET` | `[Authorize(Roles = "Admin")]` | — | `AdminAnalyticsService`, `AppDbContext` | Analytics/KPI view (shares the dashboard model: case/district distributions, AI failure rates, verification queue) |
| `/Admin/Users` | `GET` | `[Authorize(Roles = "Admin")]` | `string? role, int page = 1` | `IUserManagementService`, `UserManager<User>` | User account administration with role filter (`All`/`Citizen`/`Lawyer`/`Admin`) and pagination (20/page). SuperAdmin viewers additionally get `CreateAdmin`/`SuspendAdmin`/`PromoteAdmin` controls |
| `/Admin/Suspend` | `POST` | `[Authorize(Roles = "Admin")]`, antiforgery | `int userId, bool suspend` | `IUserManagementService` | Sets account status to `Suspended` (`suspend=true`) or `Active` (`false`). Admins and self-suspension are protected inside the service |
| `/Admin/CreateAdmin` | `GET`/`POST` | `[Authorize(Policy = "SuperAdminOnly")]`, antiforgery | `CreateAdminViewModel` | `IUserManagementService` | Creates a new Admin/SuperAdmin and returns the password-reset link (relayed securely by the acting SuperAdmin) |
| `/Admin/SuspendAdmin` | `POST` | `[Authorize(Policy = "SuperAdminOnly")]`, antiforgery | `int userId, bool suspend` | `IUserManagementService` | Suspends/reactivates a non-SuperAdmin admin; SuperAdmin rows are protected |
| `/Admin/PromoteAdmin` | `POST` | `[Authorize(Policy = "SuperAdminOnly")]`, antiforgery | `int userId` | `IUserManagementService` | Promotes an Admin to SuperAdmin |
| `/Admin/Lawyers` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IRepository<LawyerProfile>`, `UserManager<User>` | Lawyer verification triage: pending/approved/rejected rows hydrated from real `LAWYER_PROFILE` data |
| `/Admin/VerifyLawyer` | `POST` | `[Authorize(Roles = "Admin")]`, antiforgery | `int lawyerProfileId, bool approve, string? reason` | `LawyerVerificationService` | Approves or rejects a bar-registration application; rejection requires a reason that is shown to the lawyer |
| `/Admin/Corpus` | `GET` | `[Authorize(Roles = "Admin")]` | — | `AppDbContext` | Acts corpus management (FR-17) with database-side section/chunk/embedded aggregates; replaces the earlier mock `/Admin/Acts` + `/Admin/Acts/Reindex/{id}` flow |
| `/Admin/Scenarios` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IScenarioMappingRepository`, `IActSectionRepository`, `IActRepository` | Keyword→section grounding boost list (FR-18); replaces the earlier mock `/Admin/ScenarioMappings` flow |
| `/Admin/AddScenario` | `POST` | `[Authorize(Roles = "Admin")]`, antiforgery | `int sectionId, string keyword, string? notes` | `IScenarioMappingRepository` | Creates a keyword→`SectionId` boost mapping |
| `/Admin/DeleteScenario` | `POST` | `[Authorize(Roles = "Admin")]`, antiforgery | `int mappingId` | `IScenarioMappingRepository`, `IAdminAuditService` | Deletes a boost mapping, writing an AUD-7 audit row |
| `/Admin/Categories` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IRepository<CaseCategory>` | Category listing with per-category template badges |
| `/Admin/AiLogs` | `GET` | `[Authorize(Roles = "Admin")]` | `string? type, int minLatency = 0, int page = 1` | `IRepository<AiLog>` | Paginated AI audit trail (50/page) with type/latency filters — filters apply to the full dataset (AUD-8) |
| `/Admin/Transactions` | `GET` | `[Authorize(Roles = "Admin")]` | — | `PaymentService` | Payment orders and pending lawyer payouts. Orders become Paid only through the gateway (§8) |
| `/Admin/RefundOrder` | `POST` | `[Authorize(Policy = "SuperAdminOnly")]`, antiforgery | `int orderId` | `PaymentService`, `IAdminAuditService` | Refunds a **Paid** order as a ledger reversal (clears `Case.HonorariumPaid`) and writes an audit row; a non-Paid order returns an error (AUD-11) |
| `/Admin/ApprovePayout` | `POST` | `[Authorize(Policy = "SuperAdminOnly")]`, antiforgery | `int payoutRequestId` | `PaymentService` | Marks a pending payout paid (sandbox) |
| `/Admin/HealthStatus` | `GET` | `[Authorize(Roles = "Admin")]` | — | `AppDbContext`, `IConfiguration` | JSON health snapshot (DB / Qdrant / Gemini) polled by the dashboard |
| `/Admin/EmbeddingProgress` | `GET` | `[Authorize(Roles = "Admin")]` | — | `EmbeddingProgressState` | JSON embedding-progress telemetry for the corpus background job |
| `/Admin/GeminiKeyStatus` | `GET` | `[Authorize(Roles = "Admin")]` | — | `GeminiClient` | JSON per-key token usage / park status for the dashboard key tracker |

---

## 8. PaymentController (FR-24)

Orders go `Pending` → gateway checkout → `Paid` / `Failed`. The citizen picks a method (`bkash` or `card`); `IPaymentGatewayResolver` maps it to a gateway using `Payments:Mode`. `Simulator` (default): both methods go to the built-in simulator (§9). `Sandbox`: `bkash` goes to the bKash tokenized-checkout sandbox (`Bkash` section), `card` to the SSLCommerz sandbox (`SslCommerz` section). The gateway is stored on the order (`PAYMENT_ORDER.Gateway`), and the same gateway validates it.

| Route | Method | Authorization | Parameters / ViewModel | Injected Services & Dependencies | Description |
|---|---|---|---|---|---|
| `/Payment/Honorarium` | `POST` | Case owner, tracking-code holder or Admin; antiforgery; `payment` rate limit | JSON `{ caseId, amount, trackingCode?, method? }` (`method`: `bkash` or `card`, default `card`) | `PaymentService`, `ICaseRepository` | Creates a Pending honorarium order (10% commission) and starts a gateway session. Returns `{ success: true, orderId, gatewayUrl }`; the page sends the browser to `gatewayUrl`. If the gateway can't start, the order is Failed and the response is `{ success: false, orderId, message }` |
| `/Payment/TopUp` | `POST` | Signed-in only (`401` for guests); antiforgery; `payment` rate limit | JSON `{ amount, method? }`; `amount` ≥ 50 and a multiple of 5 BDT, else `400` | `PaymentService` | Same as Honorarium, for a chat credit order worth `amount / 5` credits (`PAYMENT_ORDER.ChatCredits`) |
| `/Payment/Success?orderId={id}` | `POST` | AllowAnonymous, no antiforgery (gateway posts cross-site) | form `tran_id, val_id, amount, status, bank_tran_id` | `PaymentService` | Gateway return URL. `ConfirmPaymentAsync` validates `val_id` server-to-server; Paid only if the validated `tran_id` **and** amount match the order. Idempotent on replays. Redirects to `/Payment/Result` |
| `/Payment/Fail?orderId={id}` | `POST` | AllowAnonymous, no antiforgery | form (as above) | `PaymentService` | Marks the order Failed only if it is Pending and the posted `tran_id` matches. Redirects to `/Payment/Result` |
| `/Payment/Cancel?orderId={id}` | `POST` | AllowAnonymous, no antiforgery | form (as above) | `PaymentService` | As Fail; redirects to `/Payment/Result?cancelled=true` |
| `/Payment/BkashCallback/{orderId}` | `GET` | AllowAnonymous (bKash redirects the browser) | query `paymentID, status` (`success`, `failure`, `cancel`) | `PaymentService` | bKash return URL for every outcome. The `paymentID` must be the one stored on the order (`GatewaySessionId`), else nothing changes. `success`: `ConfirmPaymentAsync` runs bKash execute (or the status query if already executed); Paid only if the returned `merchantInvoiceNumber` **and** amount match. `failure`/`cancel`: marks Failed. Redirects to `/Payment/Result` |
| `/Payment/Result?orderId={id}` | `GET` | AllowAnonymous (another signed-in user's order shows as not found) | `int orderId, bool cancelled` | `IRepository<PaymentOrder>` | Outcome page. Status is read from the database, never from the query string |
| `/Payment/Status/{id}` | `GET` | Order owner or Admin | `int id` | `IRepository<PaymentOrder>` | Order JSON (`status`, `amount`, `commission`, `netToLawyer`, `gatewayRef`, `paidAt`) |

---

## 9. GatewaySimulatorController (built-in simulated gateway)

Stands in for an external gateway site when `Payments:Mode = Simulator`. Sessions live in memory for 30 minutes. No real money moves.

| Route | Method | Authorization | Parameters | Description |
|---|---|---|---|---|
| `/GatewaySim/Checkout/{key}` | `GET` | AllowAnonymous | session key, `method?` (preselected tab) | Checkout page: bKash / Nagad / Rocket / card, then OTP. A finished session renders the return form |
| `/GatewaySim/Checkout/{key}` | `POST` | AllowAnonymous, antiforgery | `method, account, secret, expiry?` | Wallet number + PIN, or card number + CVV + `MM/YY` expiry |
| `/GatewaySim/Otp/{key}` | `POST` | AllowAnonymous, antiforgery | `otp` | Completes the payment |
| `/GatewaySim/Cancel/{key}` | `POST` | AllowAnonymous, antiforgery | — | Cancels the payment |

When a session finishes, the page auto-submits a form POST to the order's success / fail / cancel URL with the fields listed in §8. Test values: wallet PIN `12121`; card `4111 1111 1111 1111`, CVV `123`, any future expiry; OTP `123456`; wallet `01700000099` = insufficient balance; card `4000 0000 0000 0002` = declined; 3 wrong entries fail the session.

---

## 10. State Machine & Review Guard Contract

1. **Document Lifecycle:**
   - `Draft` → AI generated; citizen can view text preview in `/Case/Result/{id}` or `/Document/Preview/{id}`. PDF download is **locked**.
   - `UnderReview` → Claimed by a verified advocate on `/Lawyer/Queue`.
   - `Approved` / `EditedApproved` → Finalized by advocate. PDF download **unlocks** on both `/Case/Result` and `/Document/Preview`.
   - `Rejected` → Document rejected; citizen receives lawyer feedback.

2. **3-Surface Disclaimer Protocol:**
   - **Surface 1:** Sticky amber top banner (`_DisclaimerBanner.cshtml`) rendered on every page via `_Layout.cshtml`.
   - **Surface 2:** Injected dynamically into every AI response via `DisclaimerInjector.cs`.
   - **Surface 3:** Stamped into all document preview pages (`Preview.cshtml`) and QuestPDF exports.
