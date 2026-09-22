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
| `/Admin/Users` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IUserManagementService`, `UserManager<User>` | User account administration, role assignment, and suspension toggle |
| `/Admin/Users/{id}/Suspend`| `POST` | `[Authorize(Roles = "Admin")]` | `int id` | `IUserManagementService` | Toggles user account status between `Active` and `Suspended` |
| `/Admin/Lawyers` | `GET` | `[Authorize(Roles = "Admin")]` | — | `ILawyerVerificationService` | Admin verification queue for lawyer bar credentials |
| `/Admin/Lawyers/{id}/Verify`| `POST`| `[Authorize(Roles = "Admin")]` | `int id, bool approve` | `ILawyerVerificationService` | Approves or rejects lawyer verification applications |
| `/Admin/Acts` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IActRepository`, `IEmbeddingBatchJob` | Bangladesh Acts corpus management and embedding status |
| `/Admin/ScenarioMappings` | `GET` | `[Authorize(Roles = "Admin")]` | — | `IScenarioMappingRepository`, `IScenarioMappingService` | Keyword-to-statute grounding boosts management (FR-18) |
| `/Admin/Transactions` | `GET` | `[Authorize(Roles = "Admin")]` | — | `PaymentService` | Payment orders and pending lawyer payouts. There is no "mark paid" action: orders become Paid only through the gateway (§8) |
| `/Admin/RefundOrder` | `POST` | `SuperAdminOnly`, antiforgery | `int orderId` | `PaymentService`, `IAdminAuditService` | Refunds a **Paid** order as a ledger reversal (clears `Case.HonorariumPaid`) and writes an audit row. A non-Paid order shows an error (AUD-11) |

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
