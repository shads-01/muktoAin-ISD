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
- [ ] **[S-Setup.1]** Create GitHub Repository (`muktoAin-ISD`) — *Shads* `[Unblocks: T-1.1, S-Setup.2]`
- [ ] **[S-Setup.2]** Set Up Branch Protection on `main` — *Shads* `[Unblocks: S-Setup.3]`
- [ ] **[S-Setup.3]** Create Feature Branches for Each Teammate — *Shads* `[Unblocks: All Teammates]`
- [ ] **[S-Setup.4]** Assign Teammate Starting Points — *Shads*
- [ ] **[S-Setup.5]** Prepare Seed Data Files (`districts.json`, `categories.json`, etc.) — *Shads* `[Unblocks: T-1.7]`
- [ ] **[S-Setup.6]** Set Up Multi-Key Gemini API Key Rotation Config — *Shads* `[Unblocks: S-1.3]`
- [ ] **[S-Setup.7]** Set Up Qdrant Cloud Cluster — *Shads* `[Unblocks: T-1.11]`
- [ ] **[S-Setup.8]** Share `appsettings.Development.json.template` — *Shads*
- [ ] **[S-1.3]** Implement `GeminiClient.cs` with Key Rotation — *Shads* `[Unblocks: S-1.4]`
- [ ] **[S-1.4]** Implement `GeminiEmbeddingService.cs` (`text-embedding-004`) — *Shads* `[Unblocks: T-2.1]`
- [ ] **[S-1.5]** Define Legal Prompt Templates & Disclaimers (`Disclaimers.cs`) — *Shads* `[Unblocks: S-1.6, S-2.1]`
- [ ] **[S-1.6]** Implement `DisclaimerInjector.cs` — *Shads* `[Blocked by: S-1.5] [Unblocks: S-2.2]`
- [ ] **[S-1.7]** Implement `EncryptionService.cs` (ASP.NET Data Protection API) — *Shads* `[Unblocks: S-2.5]`
- [ ] **[E-1.1]** Master Layout `_Layout.cshtml` (Bootstrap 5, Nav, Footer) — *Erin* `[Unblocks: E-1.4]`
- [ ] **[E-1.2]** `_DisclaimerBanner.cshtml` & `_LanguageToggle.cshtml` — *Erin* `[Unblocks: E-1.1]`
- [ ] **[E-1.3]** Static Assets (CSS, JS & Noto Sans Bengali Fonts in `wwwroot/`) — *Erin* `[Unblocks: A-2.5]`
- [ ] **[E-1.4]** Home Controller & Views (Landing page with mock data) — *Erin* `[Blocked by: E-1.1]`
- [ ] **[E-1.5]** Identity Views (`Login.cshtml`, `Register.cshtml`) — *Erin* `[Blocked by: E-1.1]`
- [ ] **[E-1.6]** Checkpoint 1 Frontend Exit Gate — *Erin* `[Blocked by: E-1.1 to E-1.5]`

### 2. Core Architecture Critical Path (Tultul's First Wave)
- [x] ~~**[T-1.1]** Initialize .NET 8 Solution (`MuktoAin.sln` 4 Projects + References) — *Tultul* `[Blocked by: S-Setup.1] [Unblocks: T-1.2]`~~
- [x] ~~**[T-1.2]** Implement All 9 Enums in `MuktoAin.Domain/Enums/` — *Tultul* `[Blocked by: T-1.1] [Unblocks: T-1.3, A-1.1]`~~
- [x] ~~**[T-1.3]** Implement All 14 Domain Entities in `MuktoAin.Domain/Entities/` — *Tultul* `[Blocked by: T-1.2] [Unblocks: T-1.4, A-1.1]`~~
- [x] ~~**[T-1.4]** Define Repository & Service Interfaces in Domain/Application — *Tultul* `[Blocked by: T-1.3] [Unblocks: T-1.5, A-2.1]`~~
- [x] ~~**[T-1.5]** EF Core `AppDbContext.cs` in `MuktoAin.Infrastructure` — *Tultul* `[Blocked by: T-1.3, T-1.4] [Unblocks: T-1.6, S-1.1]`~~
- [x] ~~**[T-1.6]** Manual MSSQL Schema Scripts in SSMS (`scripts/01-14_*.sql`) — *Tultul* `[Blocked by: T-1.3, T-1.5] [Unblocks: S-1.1, T-1.7]`~~
- [x] ~~**[A-1.1]** All DTOs in `MuktoAin.Application/DTOs/` — *Arpita* `[Blocked by: T-1.2, T-1.3] [Unblocks: A-2.1]`~~
- [x] ~~**[A-1.2]** Checkpoint 1 DTOs Exit Gate — *Arpita* `[Blocked by: A-1.1]`~~

### 3. Identity, Data Ingestion & Vector Indexing
- [ ] **[S-1.1]** ASP.NET Core Identity Configuration in `MuktoAin.Infrastructure` — *Shads* `[Blocked by: T-1.5, T-1.6] [Unblocks: S-1.2, S-3.6]`
- [ ] **[S-1.2]** `SeedAdminUser.cs` Startup Seeding — *Shads* `[Blocked by: S-1.1]`
- [ ] **[T-1.7]** Seed Data Loaders (`SeedDistricts`, `SeedCategories`, `SeedDocumentTypes`) — *Tultul* `[Blocked by: S-Setup.5, T-1.6] [Unblocks: T-1.8]`
- [ ] **[T-1.8]** 1,484 Bangladesh Acts Ingestion Pipeline (`ActImportService.cs`) — *Tultul* `[Blocked by: T-1.6, T-1.7] [Unblocks: T-1.9]`
- [ ] **[T-1.9]** Legal Section Chunking Pipeline (`LegalChunkingService.cs`) — *Tultul* `[Blocked by: T-1.8] [Unblocks: S-1.8]`
- [x] ~~**[T-1.10]** Manual SQL Server Full-Text Search (FTS) SSMS Script — *Tultul* `[Blocked by: T-1.6] [Unblocks: T-2.2]`~~
- [ ] **[T-1.11]** `QdrantVectorStore.cs` (.NET SDK Vector Implementation) — *Tultul* `[Blocked by: S-Setup.7, T-1.4] [Unblocks: S-1.8]`
- [ ] **[T-1.12]** MSSQL Repository Implementations (Parameterized SQL) — *Tultul* `[Blocked by: T-1.4, T-1.6] [Unblocks: T-1.13, A-2.1]`
- [ ] **[T-1.13]** DI Registration in `Program.cs` — *Tultul* `[Blocked by: T-1.11, T-1.12] [Unblocks: S-1.9]`
- [ ] **[T-1.14]** Data Layer Unit Tests (`MuktoAin.UnitTests`) — *Tultul* `[Blocked by: T-1.12]`
- [ ] **[S-1.8]** `EmbeddingBatchJob.cs` (Embed & Index All Chunks into Qdrant) — *Shads* `[Blocked by: T-1.9, T-1.11, S-1.4] [Unblocks: S-1.9, T-2.1]`
- [ ] **[T-1.15]** Checkpoint 1 Data Foundation Exit Gate — *Tultul* `[Blocked by: T-1.1 to T-1.14]`
- [ ] **[S-1.9]** Checkpoint 1 Overall RAG Ingestion Smoke Test Exit Gate — *Shads* `[Blocked by: S-1.8, T-1.13, T-2.1]` *(retrieval leg requires T-2.1 — may land early CP2)*

---

## 🚀 Checkpoint 2: Core Citizen Flow, RAG & Document Generation (45%)

### 1. Retrieval & RAG Context Assembly
- [ ] **[T-2.1]** `SimilaritySearchService.cs` (Qdrant Vector Retrieval) — *Tultul* `[Blocked by: S-1.4, S-1.8, T-1.11] [Unblocks: T-2.3]`
- [ ] **[T-2.2]** `KeywordSearchService.cs` (SQL FTS Fallback) — *Tultul* `[Blocked by: T-1.10] [Unblocks: T-2.3]`
- [ ] **[T-2.3]** `RagContextBuilder.cs` (Vector-Primary with FTS Fallback) — *Tultul* `[Blocked by: T-2.1, T-2.2] [Unblocks: S-2.1]`
- [ ] **[T-2.4]** `SearchService.cs` (Standalone Keyword Search for FR-7) — *Tultul* `[Blocked by: T-2.2] [Wires to E-2.4]`
- [ ] **[T-2.5]** `CategoryService.cs` (Category Hierarchy for FR-6) — *Tultul* `[Blocked by: T-1.12] [Wires to E-2.5]`
- [ ] **[T-2.6]** Checkpoint 2 Search Infrastructure Exit Gate — *Tultul* `[Blocked by: T-2.1 to T-2.5]`

### 2. AI Orchestration, Prompt Assembly & Logging
- [ ] **[S-2.1]** `PromptAssembler.cs` (Context Assembly & Grounding) — *Shads* `[Blocked by: T-2.3, S-1.5] [Unblocks: S-2.2]`
- [ ] **[S-2.2]** `AiOrchestrationService.cs` (Gemini Flash Pipeline) — *Shads* `[Blocked by: S-2.1, S-1.3, S-1.6, S-2.6] [Unblocks: S-2.3, S-2.4, A-2.2]`
- [ ] **[S-2.3]** `RightsExplanationService.cs` (Explain My Rights) — *Shads* `[Blocked by: S-2.2] [Unblocks: A-2.2, Wires to E-2.2]`
- [ ] **[S-2.4]** `AiLogService.cs` (Audit Logging & Token Tracking) — *Shads* `[Blocked by: T-1.4, S-2.2] [Unblocks: S-2.7]`
- [ ] **[S-2.6]** Polly Resilience Policies (Retry, Key Rotation & Fallback) — *Shads* `[Blocked by: S-1.3] [Unblocks: S-2.2]`
- [ ] **[S-2.7]** AI Logging PII Redaction & Audit Safety — *Shads* `[Blocked by: S-2.4]`

### 3. Case Lifecycle, Document Generation & Lawyer Review Gate
- [ ] **[A-2.1]** `CaseService.cs` (Intake, State Machine, Tracking Code) — *Arpita* `[Blocked by: T-1.4, T-1.12, A-1.1] [Unblocks: S-2.5, A-2.4]`
- [ ] **[S-2.5]** Wire `EncryptionService` into `CaseService` for PII — *Shads* `[Blocked by: S-1.7, A-2.1]`
- [ ] **[A-2.2]** `DocumentGenerator.cs` (Core Document Generation Engine) — *Arpita* `[Blocked by: S-2.2, S-2.3] [Unblocks: A-2.3]`
- [ ] **[A-2.3]** `LabourComplaintTemplate.cs` (First Structured Template) — *Arpita* `[Blocked by: A-2.2] [Unblocks: A-2.4]`
- [ ] **[A-2.4]** `DocumentService.cs` (Document CRUD, Lifecycle & Lockout) — *Arpita* `[Blocked by: A-2.3, A-2.1] [Unblocks: A-2.5, A-2.7]`
- [ ] **[A-2.5]** `PdfExportService.cs` with QuestPDF (Bengali Font) — *Arpita* `[Blocked by: E-1.3, A-2.4] [Wires to E-2.6]`
- [ ] **[A-2.6]** `LawyerVerificationService.cs` (Bar Council Verification) — *Arpita* `[Blocked by: T-1.4, T-1.12] [Unblocks: A-2.7]`
- [ ] **[A-2.7]** `LawyerReviewService.cs` (Queue, Claim Race Guard, Decisions) — *Arpita* `[Blocked by: A-2.6, A-2.4] [Wires to E-2.7]`
- [ ] **[A-2.8]** Checkpoint 2 Document & Review Exit Gate — *Arpita* `[Blocked by: A-2.1 to A-2.7]`

### 4. Frontend Citizen Views & Contract Handoff
- [ ] **[E-2.1]** Citizen Case Intake View (`/Case/Submit`) — *Erin* `[Mock Ready -> Wires to A-2.1]`
- [ ] **[E-2.2]** Legal Analysis & Rights Explanation View (`/Case/Result`) — *Erin* `[Mock Ready -> Wires to S-2.3]`
- [ ] **[E-2.3]** Anonymous Case Tracking View (`/Case/Track`) — *Erin* `[Mock Ready -> Wires to A-2.1]`
- [ ] **[E-2.4]** Acts Keyword Search View (`/Search`) — *Erin* `[Mock Ready -> Wires to T-2.4]`
- [ ] **[E-2.5]** Act Category Browsing & Detail Views (`/Category`) — *Erin* `[Mock Ready -> Wires to T-2.5]`
- [ ] **[E-2.6]** Generated Document Preview View (`/Document/Preview`) — *Erin* `[Mock Ready -> Wires to A-2.4]`
- [ ] **[E-2.7]** Lawyer Verification & Review Views (`/Lawyer/Queue`, `/Lawyer/Review`) — *Erin* `[Mock Ready -> Wires to A-2.7]`
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
- [ ] **[S-3.6]** `UserManagementService.cs` (Admin Role Management) — *Shads* `[Blocked by: S-1.1] [Wires to E-3.3]`

### 2. Admin Frontend Views & Integration Wiring
- [ ] **[E-3.1]** Admin Dashboard & Analytics Views (`/Admin/Analytics`) — *Erin* `[Blocked by: E-1.1] [Wires to A-3.2]`
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
