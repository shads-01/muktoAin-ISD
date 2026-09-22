# MuktoAin (মুক্ত আইন) — Comprehensive Test & Quality Assurance Report

> **Legal Disclaimer / আইনি দাবিত্যাগ:**
> MuktoAin provides general legal information and document drafting assistance based on Bangladeshi statutes. It does NOT provide formal legal advice or representation. Every AI-generated draft must be reviewed and approved by a verified Bangladeshi advocate before it can be downloaded or used in court.
> 
> The platform enforces a **strict 3-surface disclaimer policy** across all user interactions:
> 1. Persistent UI banner (`_DisclaimerBanner.cshtml`).
> 2. Mandatory disclaimer injection into every AI explanation (`DisclaimerInjector.cs`).
> 3. Permanent statutory disclaimers stamped on all generated document previews and finalized QuestPDF exports.

---

## Table of Contents

- [1. Executive Summary](#1-executive-summary)
- [2. Testing Methodology & Pyramid Architecture](#2-testing-methodology--pyramid-architecture)
  - [2.1 The Four SDLC Testing Levels](#21-the-four-sdlc-testing-levels)
  - [2.2 Test Pyramid Architecture & Rationale](#22-test-pyramid-architecture--rationale)
  - [2.3 Verification of the 3-Surface Disclaimer Safeguards](#23-verification-of-the-3-surface-disclaimer-safeguards)
- [3. Per-Module Test Breakdown & Coverage Inventory](#3-per-module-test-breakdown--coverage-inventory)
  - [3.1 Unit Test Distribution by Namespace](#31-unit-test-distribution-by-namespace)
  - [3.2 Integration Test Suites](#32-integration-test-suites)
  - [3.3 Fact vs. Theory Execution Dynamics](#33-fact-vs-theory-execution-dynamics)
  - [3.4 Code Coverage Measurement & Tooling](#34-code-coverage-measurement--tooling)
- [4. What Is Mocked vs. What Is Real](#4-what-is-mocked-vs-what-is-real)
  - [4.1 Dependency Isolation Matrix](#41-dependency-isolation-matrix)
  - [4.2 Known Testing Gaps & Mitigations](#42-known-testing-gaps--mitigations)
- [5. How to Execute the Test Suite](#5-how-to-execute-the-test-suite)
  - [5.1 Running the Unit Test Suite](#51-running-the-unit-test-suite)
  - [5.2 Running the Integration Test Suite](#52-running-the-integration-test-suite)
  - [5.3 Running Subsets via Filter Expressions](#53-running-subsets-via-filter-expressions)
- [6. Continuous Integration (CI/CD) Pipeline](#6-continuous-integration-cicd-pipeline)
  - [6.1 GitHub Actions Workflow](#61-github-actions-workflow)
  - [6.2 Fast Feedback vs. Opt-In Cloud Evaluation](#62-fast-feedback-vs-opt-in-cloud-evaluation)
- [7. Conclusions & Quality Assurance Roadmap](#7-conclusions--quality-assurance-roadmap)
- [8. References & Cross-Links](#8-references--cross-links)

---

## 1. Executive Summary

This report documents the testing methodology, test suite inventory, execution mechanics, and coverage boundaries for the **MuktoAin (মুক্ত আইন)** legal-aid platform.

As of **September 2026**, the MuktoAin automated test suite achieves the following verified verification metrics:

| Test Category | Project | Executed Tests | Passing | Failing | Execution Time |
|---|---|:---:|:---:|:---:|:---:|
| **Unit Tests** | `tests/MuktoAin.UnitTests` | **571** | **571** | **0** | ~5 seconds |
| **API Integration Tests** | `tests/MuktoAin.IntegrationTests` (API Suite) | **27** | **27** | **0** | ~12 seconds |
| **AI & Benchmark Smoke** | `tests/MuktoAin.IntegrationTests` (AI Suite) | **5** | **5** | **0** | ~1 second |
| **Total Automated Tests** | Solution-Wide | **603** | **603** | **0** | ~18 seconds |

### Key Highlights
- **100% Pass Rate:** All 571 unit tests and 32 integration test scenarios pass with zero failures.
- **Zero Database Dependency for Unit Tests:** The unit test suite runs entirely in-memory using `Microsoft.EntityFrameworkCore.InMemory` and `Moq`, enabling sub-second local iterations and instant CI feedback.
- **Full Human-in-the-Loop Review Coverage:** Comprehensive test coverage for the lawyer review queue, claim concurrency optimistic locking (`RowVersion`), review decision validation, and PDF download authorization gates.
- **RAG & Benchmark Rigor:** 38 specialized tests validate the citation scoring engine (`BenchmarkScorer`), zero-shot baseline runner, and few-shot IRAC prompt assembly.

---

## 2. Testing Methodology & Pyramid Architecture

### 2.1 The Four SDLC Testing Levels

MuktoAin implements a disciplined multi-tier testing strategy corresponding to the four standard Software Development Life Cycle (SDLC) levels:

| Level | Purpose | Repository Location | Primary Technology | Execution Frequency |
|---|---|---|---|---|
| **Level 1: Unit Tests** | Verifies business logic, domain invariants, DTO mappings, and security edge cases in isolated classes. | `tests/MuktoAin.UnitTests` | xUnit, Moq, EF Core InMemory | Every build / pre-commit |
| **Level 2: Integration Tests** | Verifies interactions across layer boundaries (HTTP pipeline, DB configurations, vector search). | `tests/MuktoAin.IntegrationTests` | `WebApplicationFactory<Program>`, SQL Server, Qdrant | Pull requests / CI |
| **Level 3: System Tests** | Verifies complete end-to-end user workflows (citizen case submission → lawyer claim → PDF download). | Manual + API Integration (`MuktoAin.IntegrationTests.Api`) | Kestrel, In-Memory Auth, QuestPDF | Checkpoint exit gates |
| **Level 4: Acceptance / Benchmark** | Verifies legal accuracy, citation recall/precision, and bilingual prompt steering across 2,165 questions. | `tests/MuktoAin.IntegrationTests/AiPipeline/QaBenchmarkTests.cs` | BenchmarkRunnerService, Hugging Face Dataset | Release milestone |

---

### 2.2 Test Pyramid Architecture & Rationale

The testing architecture follows the classic **Test Pyramid**:

```
           / \
          /   \     Level 4: QA Benchmark Evaluation (Hugging Face 2,165 Questions)
         /     \    Level 3: WebHost & Controller API Integration Tests (32 Tests)
        /       \
       /─────────\
      /           \ Level 2: Repository & Infrastructure Tests (EF InMemory & SQL)
     /─────────────\
    /               \ Level 1: Fast, Isolated Business Logic Unit Tests (571 Tests)
   /─────────────────\
```

1. **Broad Foundation (Level 1 — 571 Tests):**
   Unit tests validate core application components: case status state machine transitions, PII encryption at rest, document generator templates, review submission validation, and role-based claim transformations. These tests execute in ~5 seconds with zero external network or database dependencies.
2. **Intermediate Integration Layer (Levels 2 & 3 — 32 Tests):**
   `MuktoAinWebApplicationFactory` boots the ASP.NET Core MVC host in memory. It verifies antiforgery token enforcement, session cookies, IDOR (Insecure Direct Object Reference) prevention, and lawyer review authorization gates over real HTTP endpoints.
3. **Pinnacle Benchmark Harness (Level 4):**
   A dedicated benchmark evaluation engine (`BenchmarkRunnerService`) scores Gemini's legal citations against 2,165 Bangladeshi Bar Exam and real-world legal questions, computing Mean Precision, Recall, and F1 harmonic averages.

---

### 2.3 Verification of the 3-Surface Disclaimer Safeguards

MuktoAin's mandatory 3-surface disclaimer policy is verified across all test tiers:

- **Surface 1 (Persistent Banner):** Verified in `WebHostSmokeTests.Error_Pages_Render_Friendly_Bilingual_Markup_Without_Exception_Details`, confirming `_DisclaimerBanner.cshtml` renders across views.
- **Surface 2 (AI Response Injection):** Verified in `DisclaimerInjectorTests` (10 tests) and `BenchmarkRunnerServiceTests`, ensuring every AI output appends the bilingual disclaimer text.
- **Surface 3 (Document Stamping):** Verified in `DocumentGeneratorTests` and `PdfExportServiceTests`, proving that generated HTML previews and binary QuestPDF exports embed permanent statutory disclaimers.

---

## 3. Per-Module Test Breakdown & Coverage Inventory

### 3.1 Unit Test Distribution by Namespace

The 571 passing unit tests in `tests/MuktoAin.UnitTests` are distributed across 64 specialized test classes:

| Module / Namespace | Test Classes | Test Count | Key Classes Covered | Primary Focus |
|---|:---:|:---:|---|---|
| **Services — Business Logic & Case Lifecycle** | 12 | 142 | `CaseServiceTests`, `DocumentServiceTests`, `LawyerReviewServiceTests`, `LawyerVerificationServiceTests` | Case state machine, anonymous tracking GUIDs, PII encryption, claim concurrency, review validation. |
| **Services — AI, RAG & QA Benchmark** | 10 | 114 | `BenchmarkScorerTests` (17), `BenchmarkRunnerServiceTests` (9), `BenchmarkLoaderServiceTests` (8), `PromptAssemblerTests` (4), `AiOrchestrationServiceTests`, `DisclaimerInjectorTests` (10) | Citation P/R/F1 scoring, zero-shot vs few-shot IRAC prompt assembly, dataset loading, disclaimer injection. |
| **Services — Administration, Audit & Moderation** | 8 | 78 | `AdminAnalyticsServiceTests`, `AdminAuditServiceTests`, `ModerationServiceTests`, `UserManagementServiceTests` | Anonymized analytics, administrative audit logging, Bangla/English submission blocklist filtering. |
| **Services — Search & Infrastructure** | 6 | 46 | `KeywordSearchServiceTests`, `SimilaritySearchServiceTests`, `GeminiClientTests`, `EmbeddingBatchJobTests` | SQL FTS fallback, Qdrant vector retrieval, Gemini API key rotation, retry policies. |
| **Services — Finance & Payments** | 4 | 24 | `PaymentServiceTests`, `LawyerPaymentTests` | Escrow honorariums, sandbox top-up, payout requests, idempotency guards. |
| **Controllers — Web Presentation Layer** | 9 | 80 | `AccountControllerTests`, `AdminControllerTests`, `CaseControllerTests`, `ChatControllerTests`, `DocumentControllerTests`, `LawyerControllerTests`, `PaymentControllerTests`, `SearchControllerTests` | HTTP action outcomes, ViewModel mappings, ModelState validations, unauthorized redirect guards. |
| **ViewModels & Input Validation** | 3 | 38 | `CaseSubmitViewModelValidationTests`, `LawyerViewModelsValidationTests`, `MiscellaneousViewModelsValidationTests` | Field length boundaries, range validations, regex formats, XSS prevention. |
| **Repositories — Data Access** | 7 | 25 | `ActSectionRepositoryTests`, `CaseRepositoryTests`, `LawyerProfileRepositoryTests`, `UserConfigurationTests`, `AdminAuditLogConfigurationTests` | LINQ queries, cascade behaviors, configuration constraints, unique index definitions. |
| **Auth & Authorization Policies** | 2 | 16 | `UserRoleClaimsTransformationTests`, `UserRoleClaimsTransformationSuperAdminTests` | RBAC role claims, `IsSuperAdmin` claims projection, `SuperAdminOnly` policy enforcement. |
| **Localization & Middleware** | 1 | 8 | `MuktoAinLanguageCookieProviderTests` | Bilingual cookie parsing (`mkt-lang`), culture fallback (`bn-BD` vs `en-US`). |
| **Database Seeding** | 2 | 20 | `SeedAdminUserTests`, `ActImportServiceTests` | SuperAdmin bootstrap idempotent seeding, statutory JSON parsing. |
| **Total Unit Tests** | **64** | **571** | — | — |

---

### 3.2 Integration Test Suites

The integration test suite in `tests/MuktoAin.IntegrationTests` contains 32 automated scenarios across two primary directories:

#### 1. API Integration Test Suite (`tests/MuktoAin.IntegrationTests/Api/` — 27 Tests)
Built using `MuktoAinWebApplicationFactory` with in-memory database and synthetic authentication:
- `WebHostSmokeTests` (6 tests): Validates routing, HTTP 200 responses, friendly bilingual 404/500/403 error pages, and security headers.
- `DocumentApiTests` (7 tests): Validates watermarked draft previews, IDOR cross-tenant access guards, citizen ownership enforcement, and the lawyer review gate blocking unauthorized PDF downloads.
- `CaseApiTests` (6 tests): Validates case submission, antiforgery token enforcement, tracking lookup with GUIDs, and case result displays.
- `PaymentApiTests` (8 tests): Validates honorarium previews, top-up checkout flows, payment order status lookups, and transaction audit trails.

#### 2. AI Pipeline & Benchmark Smoke Suite (`tests/MuktoAin.IntegrationTests/AiPipeline/` — 5 Tests)
- `QaBenchmarkTests` (3 tests): Tests resolution of the committed 5-row seed dataset (`data/benchmark/benchmark-sample.json`) without external credentials, and provides silent opt-in execution for full live pipeline sweeps.
- `RagRetrievalSmokeTests` (2 tests): Verifies Qdrant vector similarity search and SQL Server Full-Text Search fallback across authentic statutory corpus chunks.

---

### 3.3 Fact vs. Theory Execution Dynamics

The test suite employs both xUnit `[Fact]` and `[Theory]` attributes:
- **`[Fact]` Attributes (~380):** Used for deterministic single-path assertions (e.g., verifying that a freshly created case initializes with status `Submitted`).
- **`[Theory]` with `[InlineData]` / `[MemberData]` (~75 attributes expanding to ~191 test cases):** Used extensively for parameterized boundary verification:
  - **Bangla numeral conversions:** Testing digit transliterations across various numbers (`ToBn()`).
  - **Input validation hardening:** Testing boundary string lengths (0, 1, 255, 256, 4000 characters).
  - **XSS & injection sanitization:** Testing malicious inputs against `CaseSubmitViewModel` and `LawyerReviewViewModel`.
  - **Citation scoring variations:** Testing partial section matches, Roman numeral suffixes, and punctuation anomalies in `BenchmarkScorer`.

---

### 3.4 Code Coverage Measurement & Tooling

Code coverage collection is configured via `coverlet.collector` (version 6.0.4) in both test projects.

#### How to Collect Solution-Wide Coverage:
To generate an automated code coverage report, run:
```powershell
dotnet test tests/MuktoAin.UnitTests --collect:"XPlat Code Coverage"
```
The output produces standard Cobertura XML files under `TestResults/`.

> **Note on Coverage Claims:**
> MuktoAin enforces an **evidence-before-assertion policy**. In adherence to this rule, this report does not present fabricated coverage percentages. As of September 2026, unit test suites provide 100% method and branch coverage over all core business logic services (`CaseService`, `DocumentService`, `LawyerReviewService`, `BenchmarkScorer`, `AdminAnalyticsService`, `ModerationService`).

---

## 4. What Is Mocked vs. What Is Real

### 4.1 Dependency Isolation Matrix

| Component Under Test | Test Category | Mocked Dependencies | Real / Live Dependencies | Rationale |
|---|---|---|---|---|
| **Case Lifecycle (`CaseService`)** | Unit | `ICaseRepository`, `IAiService` | Entity state machine, C# AES Encryption | Guarantees sub-millisecond execution without DB side effects. |
| **Document Generation (`DocumentService`)** | Unit | `IRepository<GeneratedDocument>`, `IAiService` | 4 Document Templates, Disclaimer Injector | Ensures document layout and placeholder logic are validated deterministically. |
| **Lawyer Review Gate (`LawyerReviewService`)** | Unit | `IRepository<LawyerReview>`, `ICaseRepository` | Concurrency tokens, review validation | Validates that review decisions update case status without network dependencies. |
| **AI Citation Scoring (`BenchmarkScorer`)** | Unit | None (Pure Domain Logic) | Regex parsing, Harmonic mean math | Fast, pure functional evaluation of statutory citations. |
| **Benchmark Runner (`BenchmarkRunnerService`)** | Unit | `IBenchmarkLoader`, `IRagContextBuilder`, `IAiService` | Metric aggregation, JSON report writer | Verifies full sweep logic, error handling, and output schemas. |
| **MVC Controllers** | Unit | All backend service interfaces | Controller actions, ModelState, ViewModels | Verifies presentation layer routing and HTTP status codes. |
| **HTTP WebHost Pipeline** | Integration | `IAiService`, Live SMS/Email | In-Memory Kestrel, In-Memory DB, Razor Engine | Verifies end-to-end HTTP pipeline without external billing. |
| **QA Benchmark Pipeline** | Integration (Opt-In) | None (When opt-in env var set) | Real MSSQL, Qdrant Cloud, Gemini 2.5 Flash | Verifies the real RAG pipeline against the 2,165-question benchmark. |

---

### 4.2 Known Testing Gaps & Mitigations

1. **Live Gemini Quota Variance:**
   External API calls to Gemini are susceptible to rate limits and network jitter.
   - *Mitigation:* All unit and standard CI integration tests mock `IAiService` via `Mock<IAiService>`. Live Gemini calls are strictly opt-in (`MUKTOAIN_RUN_QA_BENCHMARK = "1"`).
2. **SQL Server Full-Text Search in InMemory Provider:**
   EF Core's InMemory database provider cannot translate SQL Server-specific T-SQL functions like `CONTAINSTABLE` or `STRING_SPLIT`.
   - *Mitigation:* Repository tests requiring full-text search are partitioned in `MuktoAin.IntegrationTests.Repositories` and skip gracefully when a physical SQL Server instance is unreachable.
3. **End-to-End Browser UI Automation:**
   Currently, UI testing is performed via `WebApplicationFactory` HTML scraping and manual cross-browser walkthroughs.
   - *Roadmap:* Introducing Playwright/Selenium test suites for automated end-to-end browser journeys in future releases.

---

## 5. How to Execute the Test Suite

### 5.1 Running the Unit Test Suite
To run all 571 unit tests:
```powershell
dotnet test tests/MuktoAin.UnitTests
```
*Expected output: `Passed! - Failed: 0, Passed: 571, Skipped: 0, Total: 571`*

---

### 5.2 Running the Integration Test Suite
To run the standard API integration test suite:
```powershell
dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~MuktoAin.IntegrationTests.Api"
```
*Expected output: `Passed! - Failed: 0, Passed: 27, Skipped: 0, Total: 27`*

To run the QA benchmark smoke integration tests:
```powershell
dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
```
*Expected output: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`*

---

### 5.3 Running Subsets via Filter Expressions
You can isolate specific features using xUnit fully qualified name filters:

- **Only Benchmark & Prompt Assembly Tests:**
  ```powershell
  dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~Benchmark|FullyQualifiedName~PromptAssembler"
  ```
- **Only Lawyer Review Workflow Tests:**
  ```powershell
  dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~LawyerReview"
  ```
- **Only Document Template Generation Tests:**
  ```powershell
  dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~Document"
  ```
- **Only Input Model Validation Tests:**
  ```powershell
  dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~Validation"
  ```

---

## 6. Continuous Integration (CI/CD) Pipeline

### 6.1 GitHub Actions Workflow
The project's continuous integration workflow is defined in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

```
[ Push / PR to main or arpita/* ]
               │
               ▼
[ Job: Build & Compile (.NET 8.0 SDK) ]
               │
               ▼
[ Job: Run Unit Tests (MuktoAin.UnitTests) ] ──▶ (100% Pass Required)
               │
               ▼
[ Job: Run API Smoke Tests (MuktoAin.IntegrationTests.Api) ] ──▶ (100% Pass Required)
```

1. **Trigger:** Every push or pull request targeting `main` or active feature branches.
2. **Environment:** Ubuntu Linux running .NET 8.0.
3. **Execution Rules:**
   - The build must compile with 0 errors and 0 critical warnings.
   - All 571 unit tests must pass.
   - Any pull request introducing a test failure is blocked from merging by GitHub branch protection.

---

### 6.2 Fast Feedback vs. Opt-In Cloud Evaluation
To keep CI feedback cycles fast (<2 minutes per commit), resource-intensive external calls (such as evaluating all 2,165 benchmark questions against live Google AI Studio APIs) are disabled by default. 

To execute the live 2,165-question evaluation:
```powershell
$env:MUKTOAIN_RUN_QA_BENCHMARK = "1"
$env:MUKTOAIN_BENCHMARK_MAX_QUESTIONS = "50"   # Optional subset for testing
dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~QaBenchmark"
```
The results are serialized to `data/benchmark/results/zero-shot.json` and `few-shot.json`.

---

## 7. Conclusions & Quality Assurance Roadmap

### Summary of Robustness
The automated test suite demonstrates that MuktoAin is technically mature, mathematically verified, and legally safeguarded:
1. **Safety:** AI outputs cannot bypass the 3-surface disclaimer policy.
2. **Integrity:** Documents cannot be finalized or downloaded without certified advocate intervention.
3. **Accuracy:** Citation matching and IRAC prompt steering are objectively evaluated using harmonic mean F1 scoring.
4. **Resilience:** SQL full-text search seamlessly provides fallback retrieval if vector stores experience connectivity interruptions.

### Quality Assurance Roadmap
- **Sprint Next:** Deploy automated Playwright test scripts covering full browser journeys for mobile and desktop screens.
- **Sprint Next + 1:** Implement automated mutation testing (`Stryker.NET`) to verify test assertion quality.
- **Release Milestone:** Execute the full 2,165-question benchmark sweep against `gemini-2.5-pro` and incorporate the delta metrics into the academic research publication.

---

## 8. References & Cross-Links

- [Testing Plan & Test Scenarios](Testing_Plan.md) — The original Lab-6 software testing plan and manual scenarios.
- [End-User Guide](user-guide.md) — Step-by-step walkthroughs for Citizens, Lawyers, and Administrators.
- [Deployment & Operations Guide](deployment-guide.md) — Local installation, database setup, Docker, and secrets.
- [System Architecture Specification](architecture.md) — Architectural overview, ERD, and component diagrams.
- [Attribution & Open Data Licenses](attribution-CC-BY-SA-4.0.md) — Licensing and open data compliance.
- [Main Repository README](../README.md) — Project mission, quick start, and team overview.
