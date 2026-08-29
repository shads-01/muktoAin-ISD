# MuktoAin — Teammate Dependency & Execution Flow

> **Progress Tracking:** To update progress, edit this file and change `[ ]` to `[x]` on completed tasks, and wrap the completed line in `~~strikethrough~~` (e.g. `- [x] ~~**[T-1.1]** ...~~`).
> **Initials & Step Prefix Key:**
> - **`[S-Setup.#]`** & **`[S-#.#]`** = **Shads** (Project Lead · `Shads_plan.md`)
> - **`[T-#.#]`** = **Tultul** (Data Foundation & Search · `Tultul_plan.md`)
> - **`[A-#.#]`** = **Arpita** (Document Generation, Review Gate & Admin · `Arpita_plan.md`)
> - **`[E-#.#]`** = **Erin** (Frontend Razor Views & Mock Integration · `Erin_plan.md`)

---

## 🏁 Checkpoint 1: Foundation, Schema & Data Ingestion (30%)

### 1. Day 0 Setup & Independent Start (No Cross-Teammate Blockers)
- [x] ~~**[S-Setup.1]** Create GitHub Repository (`muktoAin-ISD`) — *Shads* `[Unblocks: T-1.1, S-Setup.2]`~~
- [x] ~~**[S-Setup.2]** Set Up Branch Protection on `main` — *Shads* `[Unblocks: S-Setup.3]`~~
- [x] ~~**[S-Setup.3]** Create Feature Branches for Each Teammate — *Shads* `[Unblocks: All Teammates]`~~
- [x] ~~**[S-Setup.4]** Assign Teammate Starting Points — *Shads*~~
- [x] ~~**[S-Setup.5]** Prepare Seed Data Files (`districts.json`, `categories.json`, etc.) — *Shads* `[Unblocks: T-1.7]`~~
- [x] ~~**[S-Setup.6]** Set Up Multi-Key Gemini API Key Rotation Config — *Shads* `[Unblocks: S-1.3]`~~
- [x] ~~**[S-Setup.7]** Set Up Qdrant Cloud Cluster — *Shads* `[Unblocks: T-1.11]`~~
- [x] ~~**[S-Setup.8]** Share `appsettings.Development.json.template` — *Shads*~~
- [x] ~~**[S-1.3]** Implement `GeminiClient.cs` with Key Rotation — *Shads* `[Unblocks: S-1.4]`~~
- [x] ~~**[S-1.5]** Define Legal Prompt Templates & Disclaimers (`Disclaimers.cs`) — *Shads* `[Unblocks: S-1.6, S-2.1]`~~
- [x] ~~**[S-1.6]** Implement `DisclaimerInjector.cs` — *Shads* `[Blocked by: S-1.5] [Unblocks: S-2.2]`~~ — implemented with bilingual support (en/bn) and registered in DI; verified by 5 unit tests
- [x] ~~**[S-1.7]** Implement `EncryptionService.cs` (ASP.NET Data Protection API) — *Shads* `[Unblocks: S-2.5]`~~ — implemented with ASP.NET Data Protection API, registered in DI; verified by 4 unit tests (Bangla/English roundtrips, empty/null safety)
- [x] ~~**[E-1.1]** Master Layout `_Layout.cshtml` (Bootstrap 5, Nav, Footer) — *Erin* `[Unblocks: E-1.4]`~~
- [x] ~~**[E-1.2]** `_DisclaimerBanner.cshtml` & `_LanguageToggle.cshtml` — *Erin* `[Unblocks: E-1.1]`~~
- [x] ~~**[E-1.3]** Static Assets (CSS, JS & Noto Sans Bengali Fonts in `wwwroot/`) — *Erin* `[Unblocks: A-2.5]`~~
- [x] ~~**[E-1.4]** Home Controller & Views (Landing page with mock data) — *Erin* `[Blocked by: E-1.1]`~~
- [x] ~~**[E-1.5]** Identity Views (`Login.cshtml`, `Register.cshtml`) — *Erin* `[Blocked by: E-1.1]`~~
- [x] ~~**[E-1.6]** Checkpoint 1 Frontend Exit Gate — *Erin* `[Blocked by: E-1.1 to E-1.5]`~~

### 2. Core Architecture Critical Path (Tultul's First Wave)
- [x] ~~**[T-1.1]** Initialize .NET 8 Solution (`MuktoAin.sln` 4 Projects + References) — *Tultul* `[Blocked by: S-Setup.1] [Unblocks: T-1.2]`~~
- [x] ~~**[T-1.2]** Implement All 9 Enums in `MuktoAin.Domain/Enums/` — *Tultul* `[Blocked by: T-1.1] [Unblocks: T-1.3, A-1.1]`~~
- [x] ~~**[T-1.3]** Implement All 14 Domain Entities in `MuktoAin.Domain/Entities/` — *Tultul* `[Blocked by: T-1.2] [Unblocks: T-1.4, A-1.1]`~~
- [x] ~~**[T-1.4]** Define Repository & Service Interfaces in Domain/Application — *Tultul* `[Blocked by: T-1.3] [Unblocks: T-1.5, A-2.1]` — amended during T-1.12: `IRepository<T>.GetByIdAsync` changed from `int` to `object` (didn't fit AiLog's long PK)~~
- [x] ~~**[T-1.5]** EF Core `AppDbContext.cs` in `MuktoAin.Infrastructure` — *Tultul* `[Blocked by: T-1.3, T-1.4] [Unblocks: T-1.6, S-1.1]`~~
- [x] ~~**[T-1.6]** Manual MSSQL Schema Scripts in SSMS (`scripts/01-14_*.sql`) — *Tultul* `[Blocked by: T-1.3, T-1.5] [Unblocks: S-1.1, T-1.7]`~~
- [x] ~~**[A-1.1]** All DTOs in `MuktoAin.Application/DTOs/` — *Arpita* `[Blocked by: T-1.2, T-1.3] [Unblocks: A-2.1]`~~
- [x] ~~**[A-1.2]** Checkpoint 1 DTOs Exit Gate — *Arpita* `[Blocked by: A-1.1]`~~

### 3. Identity, Data Ingestion & Vector Indexing
- [x] ~~**[S-1.1]** ASP.NET Core Identity Configuration in `MuktoAin.Infrastructure` — *Shads* `[Blocked by: T-1.5, T-1.6] [Unblocks: S-1.2, S-3.6]` — verified against real SQL Server; `AppDbContext` now inherits `IdentityDbContext<User, IdentityRole<int>, int>` (no role/claim tables exist in the SSMS schema by design — authorization runs off `User.Role` enum via `UserRoleClaimsTransformation` in Web/Auth); cookie auth via `AddIdentityCore` + explicit `AddAuthentication(IdentityConstants.ApplicationScheme)`; `UserConfiguration.cs` maps `User.Id` → physical `UserId` column~~ **— [Aug 2026] Registration & Login E2E hardening verified live (SQLEXPRESS, http://localhost:5250):** (1) schema `[dbo].[USER]`/`02_schema.sql` fully match `IdentityUser<int>` — **no schema script change needed**; (2) password policy aligned across `RegisterViewModel` regex, Identity config, and `Login.cshtml` demo-fill hints; (3) `SeedDemoUsers.cs` (Development-only) seeds `citizen@muktoain.bd`/`Citizen@123` + `lawyer@muktoain.bd`/`Lawyer@123` with Pending LawyerProfile `DEMO-BAR-2026-0001`; (4) `IdentityErrorMapper.cs` + `AccountController` give bilingual field-level identity errors and suspended/locked messages; (5) **128 unit tests pass** (10 new AccountController + 8 new IdentityErrorMapper); live E2E confirmed: role logins → 302 to `/Admin/Dashboard` `/Lawyer/Queue` `/Case/Track`, register citizen, register lawyer ± bar no (missing bar = field error), wrong-password, lockout-after-5 (correct pw still locked, `LockoutEnd` set), suspended message, logout clears `MuktoAin.Auth` cookie. **⚠️ OPEN issues found:** (a) **authorization gap** — `// [Authorize(Roles="Admin")]` still commented in `AdminController.cs` (line 5), and `LawyerController`/`CaseController` have no `[Authorize]` gates → any visitor (or unauthenticated) can open `/Admin/Dashboard` & `/Lawyer/Queue` (role claim projection `UserRoleClaimsTransformation` IS ready; gate just needs enabling); (b) UI nav "Logout" (`main.js:444`) is a stub link to `/Account/Login`, not wired to the `[HttpPost] Account/Logout` action. **— [Aug 2026] BOTH OPEN ISSUES RESOLVED + hardened:** (1) role gates enabled — `AdminController` `[Authorize(Roles="Admin")]`, `LawyerController` `[Authorize(Roles="Admin,Lawyer")]`, `CaseController` stays guest-open; `Program.cs` AccessDeniedPath → `/Home/AccessDenied` (403 page, `HomeController`); (2) nav Logout replaced with a real `<form method="post" asp-action="Logout">` (Razor antiforgery token) + `main.js`/`main.css` selector updates. **Live E2E matrix re-verified (single instance, SQL healthy):** admin login → 302 `/Admin/Dashboard`; citizen → 302 `/Case/Track`; lawyer → 302 `/Lawyer/Queue`; unauthenticated `/Admin/Dashboard` & `/Lawyer/Queue` → 302 `/Account/Login?ReturnUrl=...`; citizen on both → 302 `/Home/AccessDenied` (followed = 403 page); lawyer `/Lawyer/Queue` 200 but `/Admin/Dashboard` denied; admin both 200; logout POST (authenticated-context token) → 302 `/` + `MuktoAin.Auth` cleared + subsequent `/Admin/Dashboard` → login redirect; wrong-password → 200 `email or password is incorrect`; register regression ✓; **128 unit tests pass. Also fixed a latent bug:** `UseStatusCodePagesWithReExecute("/Home/Error")` re-executes with the ORIGINAL method but `HomeController.Error` was `[HttpGet]` — any non-GET 4xx/5xx surfaced as a misleading **405**; made `/Home/Error` method-agnostic (route-only) so status-code pages render for POST-triggered errors too. **Root-cause note:** the post-restart universal "405/400 on every POST" was NOT an app regression — the new `_Layout` logout `<form>` emits a second `__RequestVerificationToken`, and the E2E curl harness grabbed two newline-joined tokens (155+1+155=311) which antiforgery correctly rejects; fixed harness extraction (single per-line token + identity-matched context), app unchanged.
- [x] ~~**[S-1.2]** `SeedAdminUser.cs` Startup Seeding — *Shads* `[Blocked by: S-1.1]` — idempotent; credentials from `SeedAdmin` config section (template updated); verified live: seeded `admin@muktoain.bd` (UserId=1, Role=Admin) on real SQL Server — ⚠️ **open env issue found during this verification:** dev machine cannot bind ports 5080/5082 (Windows excluded port range, not a code bug); diagnosis + fix options in Shads_plan Step 1.2 `[ENV-BUG][OPEN]`; also note the local admin row was seeded with the bootstrap default password BEFORE `SeedAdmin:Password` was configured — reset/delete that row or sign in with the default~~ — **[Aug 2026] Stale bootstrap-password admin row (UserId=1) deleted live; on next startup `SeedAdminUser` re-seeded `admin@muktoain.bd` with `SeedAdmin:Password` (`muktoAin@123`), now UserId=2.**
- [x] ~~**[T-1.7]** Seed Data Loaders (`SeedDistricts`, `SeedCategories`, `SeedScenarioMappings`) — *Tultul* `[Blocked by: S-Setup.5, T-1.6] [Unblocks: T-1.8]` — note: no `SeedDocumentTypes` exists because `DocumentType` is an enum column, not a lookup table (scripts/02_schema.sql has no `DOCUMENT_TYPE` table); `SeedScenarioMappings` safely no-ops until `ACT_SECTION` rows exist (T-1.8)~~
- [x] ~~**[T-1.8]** 1,484 Bangladesh Acts Ingestion Pipeline (`ActImportService.cs`) — *Tultul* `[Blocked by: T-1.6, T-1.7] [Unblocks: T-1.9]` — verified against real SQL Server: 1,484 acts, 35,633 sections, 14,523 footnotes imported; idempotent restart confirmed (0 duplicates); unblocked all 26 `SeedScenarioMappings` rows in the same run~~
- [x] ~~**[T-1.9]** Legal Section Chunking Pipeline (`LegalChunkingService.cs`) — *Tultul* `[Blocked by: T-1.8] [Unblocks: S-1.8]` — verified against real SQL Server: 35,633 sections chunked into 42,858 rows; idempotent restart confirmed. Caught and fixed a runaway-split bug during verification (452 sections producing thousands of 1-char-shrinking duplicate chunks each) by requiring a found boundary to land at least halfway into the target window before using it~~
- [x] ~~**[T-1.10]** Manual SQL Server Full-Text Search (FTS) SSMS Script — *Tultul* `[Blocked by: T-1.6] [Unblocks: T-2.2]`~~
- [x] ~~**[T-1.11]** `QdrantVectorStore.cs` (.NET SDK Vector Implementation) — *Tultul* `[Blocked by: S-Setup.7, T-1.4] [Unblocks: S-1.8]` — verified against the live Qdrant Cloud cluster: EnsureCollectionAsync/UpsertAsync/SearchAsync/DeleteAsync round-tripped correctly (collection `act_section_chunks_dev`, size 768, Cosine distance). Used `QueryAsync` instead of `SearchAsync`~~
- [x] ~~**[T-1.12]** MSSQL Repository Implementations (Parameterized SQL) — *Tultul* `[Blocked by: T-1.4, T-1.6] [Unblocks: T-1.13, A-2.1]` — verified against real SQL Server; also added ScenarioMappingRepository for completeness and fixed `IRepository<T>.GetByIdAsync` (see T-1.4 note)~~
- [x] ~~**[T-1.13]** DI Registration in `Program.cs` — *Tultul* `[Blocked by: T-1.11, T-1.12] [Unblocks: S-1.9]` — registered generic `IRepository<>`/`Repository<>` plus the 5 dedicated repository interfaces; verified with a temporary `ValidateOnBuild = true` startup run (no consumers exist yet to exercise these via normal resolution) confirming the whole DI graph resolves cleanly, then reverted the temp flag~~
- [x] ~~**[T-1.14]** Data Layer Unit Tests (`MuktoAin.UnitTests`) — *Tultul* `[Blocked by: T-1.12]` — 14 tests, all passing on EF InMemory; FromSqlRaw/CONTAINSTABLE/ExecuteUpdateAsync methods excluded (unsupported by InMemory) and deferred to T-3.3~~
- [x] ~~**[S-1.8]** `EmbeddingBatchJob.cs` (Embed & Index All Chunks into Qdrant) — *Shads* `[Blocked by: T-1.9, T-1.11, S-1.4] [Unblocks: S-1.9, T-2.1]`~~ — implemented as IHostedService in Infrastructure/VectorStore with sequential chunk processing, SHA-256 incremental hashing, Qdrant upsert and SQL status update; registered in DI; verified by 4 unit tests
- [x] ~~**[T-1.15]** Checkpoint 1 Data Foundation Exit Gate — *Tultul* `[Blocked by: T-1.1 to T-1.14]` — all of T-1.1–T-1.14 confirmed done; full solution build clean (6 projects incl. tests), 14/14 unit tests pass, end-to-end app startup verified against real SQL Server + live Qdrant with no errors, and a direct SQL check confirms data integrity: 1,484 Acts, 35,633 Sections, 42,858 Chunks, 64 Districts, 4 Categories, FTS catalog fully indexed (35,633 items = full section count)~~
- [x] ~~**[S-1.9]** Checkpoint 1 Overall RAG Ingestion Smoke Test Exit Gate — *Shads* `[Blocked by: S-1.8, T-1.13, T-2.1]`~~ — end-to-end RAG retrieval smoke tests implemented in `tests/MuktoAin.IntegrationTests/AiPipeline/RagRetrievalSmokeTests.cs` and Gemini client multi-key rotation unit tests in `tests/MuktoAin.UnitTests/Services/GeminiClientTests.cs`; verified vector-primary retrieval with Labour Act query, FTS fallback on empty vector results, and Gemini key rotation on 429 quota exhaustion; all 93 tests passing across unit and integration test suites.

---

## 🚀 Checkpoint 2: Core Citizen Flow, RAG & Document Generation (45%)

### 1. Retrieval & RAG Context Assembly
- [x] ~~**[T-2.1]** `SimilaritySearchService.cs` (Qdrant Vector Retrieval) — *Tultul* `[Blocked by: S-1.4, S-1.8, T-1.11] [Unblocks: T-2.3]`~~ — implemented in `Infrastructure/VectorStore/SimilaritySearchService.cs`: embeds the query via `IEmbeddingService`, searches Qdrant via `IVectorStore`, re-hydrates full `ActSection` rows via `IActSectionRepository.GetBySectionIdsAsync`; dedupes multiple chunk hits from the same section down to the highest-scoring one; exposed via new `IVectorSectionSearch` Domain interface (mirrors T-2.2's `IKeywordSectionSearch`, the seam T-2.3's `RagContextBuilder` will use as its primary path); registered in `Program.cs`; extracted shared `SectionNumberResolver` (used by both this and `KeywordSearchService`) to `Infrastructure/Common/`; verified by 8 new Moq unit tests — full suite 80/80 passing
- [x] ~~**[T-2.2]** `KeywordSearchService.cs` (SQL FTS Fallback) — *Tultul* `[Blocked by: T-1.10] [Unblocks: T-2.3]` — implemented in `Infrastructure/Search/KeywordSearchService.cs` against `IActSectionRepository.FullTextSearchAsync`, exposed via new `IKeywordSectionSearch` Domain interface (Domain seam for T-2.3's `RagContextBuilder` fallback), registered in `Program.cs`; verified by 6 new Moq unit tests (mapping, quote-escaping, null SectionNumber, blank-query short-circuit) — full suite 38/38 passing~~
- [x] ~~**[T-2.3]** `RagContextBuilder.cs` (Vector-Primary with FTS Fallback) — *Tultul* `[Blocked by: T-2.1, T-2.2] [Unblocks: S-2.1]`~~ — implemented in `Application/Services/RagContextBuilder.cs` against the `IVectorSectionSearch`/`IKeywordSectionSearch` Domain seams: tries vector search first, falls back to keyword search (topK-capped) when the vector path throws or returns empty; blank query short-circuits without calling either; registered in `Program.cs` as `IRagContextBuilder`; verified by 7 new Moq unit tests (vector hit, empty fallback, throw fallback, topK propagation, blank query) — full suite 87/87 passing
- [x] ~~**[T-2.4]** `SearchService.cs` (Standalone Keyword Search for FR-7) — *Tultul* `[Blocked by: T-2.2] [Wires to E-2.4]` — implemented in `Application/Services/SearchService.cs` wrapping `IKeywordSectionSearch` (Domain abstraction, not the Infrastructure concrete) with pagination and optional Act filtering (resolved via `IActRepository.GetWithSectionsAsync` since `RetrievedSection` carries `ActTitle`, not `ActId`); registered in `Program.cs`; verified by 5 new Moq unit tests — full suite 46/46 passing~~
- [x] ~~**[T-2.5]** `CategoryService.cs` (Category Hierarchy for FR-6) — *Tultul* `[Blocked by: T-1.12] [Wires to E-2.5]` — implemented as thin CRUD passthrough over `IRepository<CaseCategory>` in `Application/Services/CategoryService.cs`; note: `CaseCategory` is a flat lookup table (no `ParentCategoryId`), so there's no actual tree despite the task label — registered in `Program.cs`; verified by 3 new Moq unit tests — full suite 41/41 passing~~
- [ ] **[T-2.6]** Checkpoint 2 Search Infrastructure Exit Gate — *Tultul* `[Blocked by: T-2.1 to T-2.5]`

### 2. AI Orchestration, Prompt Assembly & Logging
- [x] ~~**[S-2.1]** `PromptAssembler.cs` (Context Assembly & Grounding) — *Shads* `[Blocked by: T-2.3, S-1.5] [Unblocks: S-2.2]`~~ — implemented `IPromptAssembler` and `PromptAssembler` in Application layer; builds grounded prompts from retrieved sections, scenario mappings, and language disclaimers; verified with unit tests
- [x] ~~**[S-2.2]** `AiOrchestrationService.cs` (Gemini Flash Pipeline) — *Shads* `[Blocked by: S-2.1, S-1.3, S-1.6, S-2.6] [Unblocks: S-2.3, S-2.4, A-2.2]`~~ — implemented `IAiOrchestrationService` and `AiOrchestrationService`; orchestrates cache checks, RAG context retrieval, prompt assembly, Gemini generation, disclaimer injection, token estimation, AI audit logging, and citation persistence in `CASE_ACT_REFERENCE`; verified with unit tests
- [x] ~~**[S-2.3]** `RightsExplanationService.cs` (Explain My Rights) — *Shads* `[Blocked by: S-2.2] [Unblocks: A-2.2, Wires to E-2.2]`~~ — implemented `IRightsExplanationService` and `RightsExplanationService` facade returning `RightsExplanationDto`; verified with unit tests
- [x] ~~**[S-2.4]** `AiLogService.cs` (Audit Logging & Token Tracking) — *Shads* `[Blocked by: T-1.4, S-2.2] [Unblocks: S-2.7]`~~ — implemented `IAiLogService` and `AiLogService` persisting latency, model, tokens, and response to `AI_LOG` table; verified with unit tests
- [x] ~~**[S-2.6]** Polly Resilience Policies (Retry, Key Rotation & Fallback) — *Shads* `[Blocked by: S-1.3] [Unblocks: S-2.2]`~~ — implemented `GeminiResiliencePolicies` (timeout -> circuit breaker -> exponential backoff retry) and registered in DI; verified with unit tests
- [x] ~~**[S-2.7]** AI Logging PII Redaction & Audit Safety — *Shads* `[Blocked by: S-2.4]`~~ — integrated regex-based citizen problem description redaction in `AiLogService` before saving to `AI_LOG`; verified with unit tests

### 3. Case Lifecycle, Document Generation & Lawyer Review Gate
- [x] ~~**[A-2.1]** `CaseService.cs` (Intake, State Machine, Tracking Code) — *Arpita* `[Blocked by: T-1.4, T-1.12, A-1.1] [Unblocks: S-2.5, A-2.4]` — implemented with `SubmitCaseAsync` returning `CaseSubmissionResultDto(CaseId, AnonymousTrackingCode)` (GUID shown once for FR-8), guest/citizen/lawyer/admin access rules in `GetCaseDetailAsync` (explicit null-compare guard), tuple-switch state machine incl. `Finalized→Submitted` re-open row, name lookups via injected `IRepository<CaseCategory>`/`IRepository<District>`; verified by 10 new Moq unit tests~~
- [x] ~~**[S-2.5]** Wire `EncryptionService` into `CaseService` for PII — *Shads* `[Blocked by: S-1.7, A-2.1]`~~ — wired `IEncryptionService` into `CaseService` for encrypt-on-write and decrypt-on-read of `Title` and `Description` with graceful plaintext fallback; verified with unit tests
- [x] ~~**[A-2.2]** `DocumentGenerator.cs` (Core Document Generation Engine) — *Arpita* `[Blocked by: S-2.2, S-2.3] [Unblocks: A-2.3]`~~ — implemented `IDocumentTemplate` and `DocumentGenerator` in `MuktoAin.Application.Documents`; delegates to matching template via DI auto-discovery dictionary; category-to-document-type mapping verified against seed categories; verified with unit tests
- [x] ~~**[A-2.3]** `LabourComplaintTemplate.cs` (First Structured Template) — *Arpita* `[Blocked by: A-2.2] [Unblocks: A-2.4]`~~ — implemented `LabourComplaintTemplate` in `MuktoAin.Application.Documents.Templates` following Bangladesh District Labour Court complaint format with cited statutory sections and bilingual disclaimer stamp (Surface 3 of 3); verified with unit tests
- [x] ~~**[A-2.4]** `DocumentService.cs` (Document CRUD, Lifecycle & Lockout) — *Arpita* `[Blocked by: A-2.3, A-2.1] [Unblocks: A-2.5, A-2.7]`~~ — implemented `DocumentService` in `MuktoAin.Application.Services` with `GenerateDocumentAsync` creating `Draft` status docs with immutable `ContentDraft`, explicit District/Category navigation resolution, preview retrieval, and status updates (`Approved`, `EditedApproved`, `Rejected`); registered in DI; verified with unit tests
- [ ] **[A-2.5]** `PdfExportService.cs` with QuestPDF (Bengali Font) — *Arpita* `[Blocked by: E-1.3, A-2.4] [Wires to E-2.6]`
- [x] ~~**[A-2.6]** `LawyerVerificationService.cs` (Bar Council Verification) — *Arpita* `[Blocked by: T-1.4, T-1.12] [Unblocks: A-2.7]` — implemented Apply/Verify/GetPendingApplications with duplicate-application guard, VerifiedByAdminId + VerifiedAt stamping on decision; verified by 6 new Moq unit tests~~
- [ ] **[A-2.7]** `LawyerReviewService.cs` (Queue, Claim Race Guard, Decisions) — *Arpita* `[Blocked by: A-2.6, A-2.4] [Wires to E-2.7]`
- [ ] **[A-2.8]** Checkpoint 2 Document & Review Exit Gate — *Arpita* `[Blocked by: A-2.1 to A-2.7]`

### 4. Frontend Citizen Views & Contract Handoff
- [x] ~~**[E-2.1]** Citizen Case Intake View (`/Case/Submit`) — *Erin* `[Mock Ready -> Wires to A-2.1]`~~
- [x] ~~**[E-2.2]** Legal Analysis & Rights Explanation View (`/Case/Result`) — *Erin* `[Mock Ready -> Wires to S-2.3]`~~
- [x] ~~**[E-2.3]** Anonymous Case Tracking View (`/Case/Track`) — *Erin* `[Mock Ready -> Wires to A-2.1]`~~
- [x] ~~**[E-2.4]** Acts Keyword Search View (`/Search`) — *Erin* `[Mock Ready -> Wires to T-2.4]`~~
- [x] ~~**[E-2.5]** Act Category Browsing & Detail Views (`/Category`) — *Erin* `[Mock Ready -> Wires to T-2.5]`~~
- [ ] **[E-2.6]** Generated Document Preview View (`/Document/Preview`) — *Erin* `[Mock Ready -> Wires to A-2.4]`
- [x] ~~**[E-2.7]** Lawyer Verification & Review Views (`/Lawyer/Queue`, `/Lawyer/Review`) — *Erin* `[Mock Ready -> Wires to A-2.7]`~~
- [ ] **[E-2.8]** `api-contracts.md` Specification & `.resx` Resource Files — *Erin* `[Unblocks: S-3.5]`
- [ ] **[E-2.9]** Checkpoint 2 Frontend Views Exit Gate — *Erin* `[Blocked by: E-2.1 to E-2.8]`
- [ ] **[S-2.8]** Checkpoint 2 Overall Citizen Flow & RAG Integration Exit Gate — *Shads* `[Blocked by: S-2.1 to S-2.7, T-2.6, A-2.8, E-2.9]`

---

## 🏛️ Checkpoint 3: Review Gate, Admin, Evaluation & Delivery (25%)

### 1. Document Templates, Admin Services & Schema Enhancements
- [ ] **[A-3.1]** Complete All 4 Document Templates (GD, RTI, Consumer) — *Arpita* `[Blocked by: A-2.2]`
- [ ] **[A-3.2]** `AdminAnalyticsService.cs` (KPIs, Funnels, Workloads) — *Arpita* `[Blocked by: T-1.12] [Wires to E-3.1]`
- [ ] **[T-3.1]** `ActsManagementService.cs` (Admin CRUD & SHA256 Re-indexing) — *Tultul* `[Blocked by: T-1.8, S-1.8] [Wires to E-3.2]`
- [ ] **[T-3.2]** `ScenarioMappingService.cs` (Admin Keyword Boosts for FR-18) — *Tultul* `[Blocked by: T-1.12] [Wires to E-3.2]`
- [x] ~~**[S-3.6]** `UserManagementService.cs` (Admin Role Management) — *Shads* `[Blocked by: S-1.1] [Wires to E-3.3]`~~ — implemented `IUserManagementService` and `UserManagementService` wrapping `UserManager<User>` with suspension and admin protection guardrails; registered in DI; verified with unit tests

### 2. Admin Frontend Views & Integration Wiring
- [x] ~~**[E-3.1]** Admin Dashboard & Analytics Views (`/Admin/Analytics`) — *Erin* `[Blocked by: E-1.1] [Wires to A-3.2]`~~
- [ ] **[E-3.2]** Admin Acts Management & Scenario Views (`/Admin/Acts`) — *Erin* `[Blocked by: E-1.1] [Wires to T-3.1, T-3.2]`
- [ ] **[E-3.3]** Admin User Management Views (`/Admin/Users`) — *Erin* `[Blocked by: E-1.1] [Wires to S-3.6]`
- [ ] **[E-3.4]** Final Integration: Wiring Controllers to Real Services — *Erin* `[Blocked by: All Backend Services]`
- [ ] **[E-3.5]** Checkpoint 3 Frontend Integration Exit Gate — *Erin* `[Blocked by: E-3.1 to E-3.4]`

### 3. QA Benchmark, Testing & Hardening
- [ ] **[S-3.1]** QA Benchmark Dataset Loader (2,165 Questions) — *Shads* `[Blocked by: T-2.3, A-2.2] [Unblocks: S-3.2]`
- [ ] **[S-3.2]** Benchmark Runner (Zero-Shot Baseline Evaluation) — *Shads* `[Blocked by: S-3.1] [Unblocks: S-3.3]`
- [ ] **[S-3.3]** Few-Shot IRAC Prompt Assembly & Re-Evaluation — *Shads* `[Blocked by: S-3.2]`
- [ ] **[A-3.3]** Business Logic Unit Tests & Security Edge-Case Tests — *Arpita* `[Blocked by: A-2.1 to A-2.7]`
- [ ] **[A-3.5]** `ModerationService.cs` (Submission Blocklist Filter) — *Arpita* `[Blocked by: T-1.12]`
- [ ] **[A-3.6]** `docs/attribution-CC-BY-SA-4.0.md` (Acts + QA Dataset Licenses) — *Arpita* — *(no blockers)*
- [ ] **[T-3.3]** Repository & DB Integration Tests — *Tultul* `[Blocked by: T-1.12]`
- [ ] **[T-3.5]** `docs/architecture.md` (Clean Arch, ERD, Retrieval Pipeline) — *Tultul* — *(no blockers, update as architecture evolves)*
- [ ] **[A-3.4]** Checkpoint 3 Arpita Exit Gate — *Arpita* `[Blocked by: A-3.1 to A-3.3, A-3.5, A-3.6]`
- [ ] **[T-3.4]** Checkpoint 3 Tultul Exit Gate — *Tultul* `[Blocked by: T-3.1 to T-3.3, T-3.5]`

### 4. Packaging, Localization & Final Release
- [ ] **[S-3.4]** Multi-Stage Dockerfile & GitHub Actions CI/CD Pipeline — *Shads*
- [ ] **[S-3.5]** `RequestLocalizationMiddleware.cs` & Resource Files Wiring — *Shads* `[Blocked by: E-2.8]`
- [ ] **[S-3.8]** `docs/deployment-guide.md` (Prereqs, Local Setup, Docker, Azure, Secrets) — *Shads* `[Blocked by: S-3.4]`
- [ ] **[S-3.9]** Root `README.md` (Mission, Stack, Quick Start, Attribution, Disclaimer) — *Shads* `[Blocked by: S-3.4, S-3.8]`
- [ ] **[S-3.7]** Checkpoint 3 Final Release & Academic Delivery Gate — *Shads* `[Blocked by: All Checkpoint 3 Tasks]`

---

## ⚡ Direct Handoff Summary Table

| Handoff # | Blocked Task | Assigned | Blocked By / Waiting On | Delivered By |
|---|---|---|---|---|
| **H-1** | Solution Scaffold `[T-1.1]` | **Tultul** | Initial repo & configs `[S-Setup.1]` | **Shads** |
| **H-2** | DTOs `[A-1.1]` | **Arpita** | Enums & Entities `[T-1.2, T-1.3]` | **Tultul** |
| **H-3** | Identity `[S-1.1]` | **Shads** | `User.cs`, `AppDbContext.cs`, SSMS Schema `[T-1.3, T-1.5, T-1.6]` | **Tultul** |
| **H-4** | Embedding Batch `[S-1.8]` | **Shads** | Chunk pipeline & `QdrantVectorStore` `[T-1.9, T-1.11]` | **Tultul** |
| **H-5** | `SimilaritySearchService` `[T-2.1]` | **Tultul** | `GeminiEmbeddingService` & Qdrant chunks `[S-1.4, S-1.8]` | **Shads** |
| **H-6** | `PromptAssembler` / AI `[S-2.1, S-2.2]` | **Shads** | `RagContextBuilder` `[T-2.3]` | **Tultul** |
| **H-7** | `DocumentGenerator` `[A-2.2]` | **Arpita** | `AiOrchestrationService` & Rights DTO `[S-2.2, S-2.3]` | **Shads** |
| **H-8** | `PdfExportService` `[A-2.5]` | **Arpita** | Noto Sans Bengali font `[E-1.3]` | **Erin** |
| **H-9** | Localization Middleware `[S-3.5]` | **Shads** | `.resx` resource files `[E-2.8]` | **Erin** |
| **H-10** | QA Benchmark Runner `[S-3.1]` | **Shads** | Full RAG pipeline + Document Generator `[T-2.3, A-2.2]` | **Tultul & Arpita** |
| **H-11** | Full View Integration `[E-3.4]` | **Erin** | Finished backend service implementations | **All Teammates** |
