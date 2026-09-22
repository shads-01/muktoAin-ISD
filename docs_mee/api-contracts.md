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

---

## 8. State Machine & Review Guard Contract

1. **Document Lifecycle:**
   - `Draft` → AI generated; citizen can view text preview in `/Case/Result/{id}` or `/Document/Preview/{id}`. PDF download is **locked**.
   - `UnderReview` → Claimed by a verified advocate on `/Lawyer/Queue`.
   - `Approved` / `EditedApproved` → Finalized by advocate. PDF download **unlocks** on both `/Case/Result` and `/Document/Preview`.
   - `Rejected` → Document rejected; citizen receives lawyer feedback.

2. **3-Surface Disclaimer Protocol:**
   - **Surface 1:** Sticky amber top banner (`_DisclaimerBanner.cshtml`) rendered on every page via `_Layout.cshtml`.
   - **Surface 2:** Injected dynamically into every AI response via `DisclaimerInjector.cs`.
   - **Surface 3:** Stamped into all document preview pages (`Preview.cshtml`) and QuestPDF exports.
