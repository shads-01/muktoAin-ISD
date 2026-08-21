# MuktoAin — Functional & Non-Functional Requirements

This document specifies the complete functional requirements (FR), non-functional requirements (NFR), NLP evaluation benchmark, and governance policies for MuktoAin.

---

## 1. Functional Requirements (FR-1 through FR-18)

| ID | Feature Name | Description | Key Components |
|---|---|---|---|
| **FR-1** | User Authentication & Guest Access | Citizen/Lawyer/Admin registration and login via ASP.NET Core Identity. Supports anonymous/guest case submissions (`Case.UserId` nullable + `IsAnonymous = true`). Admin seed script (`SeedAdminUser.cs`). | `AccountController`, `User`, `SeedAdminUser.cs` |
| **FR-2** | Multilingual Intake Form | Citizen problem submission in Bangla, English, or mixed Banglish without requiring explicit server-side translation. Gemini natively handles mixed-language comprehension. | `CaseController`, `Submit.cshtml`, `_LanguageToggle.cshtml` |
| **FR-3** | Legal Act Identification (RAG) | Semantic retrieval of top-$k$ relevant statutory chunks from the Bangladesh Legal Acts dataset via Qdrant vector store. SQL Server FTS serves as fallback. | `RagContextBuilder`, `SimilaritySearchService`, `GeminiEmbeddingService`, `KeywordSearchService` |
| **FR-4** | "Explain My Rights" | Plain-language explanation of applicable citizen rights grounded strictly in retrieved statutory sections, citing Act name and Section number. | `RightsExplanationService`, `PromptAssembler` |
| **FR-5** | Automated Document Drafting | Generation of structured legal documents (GD application, RTI request, Labour complaint, Consumer complaint) by merging case details with domain-specific templates. | `DocumentGenerator`, `Templates/*`, `GeminiClient` |
| **FR-6** | Legal Category Browsing | Exploration of legal topics and categories without requiring case submission. | `CategoryController`, `CaseCategory` |
| **FR-7** | Full-Text Act Search (Standalone) | Direct citizen keyword search and browsing over the legislative corpus using SQL Server Full-Text Search. | `KeywordSearchService`, `ActSection` |
| **FR-8** | Case Tracking Dashboard | Status tracking dashboard (Submitted $\rightarrow$ Under Review $\rightarrow$ Finalized) for citizens and admins. | `CaseController`, `Dashboard.cshtml`, `CaseStatus` |
| **FR-9** | PDF Document Export | High-quality PDF export of finalized legal drafts via QuestPDF. Locked until status reaches `Approved`. | `PdfExportService`, `GeneratedDocument` |
| **FR-10** | Gemini API Integration | Resilient HTTP client wrapper for Google Gemini with Polly retry and circuit breaker policies. | `GeminiClient`, `Polly` |
| **FR-11** | Non-Removable Legal Disclaimer | Redundant disclaimer system across UI, API responses, and generated documents (§3). | `_DisclaimerBanner.cshtml`, `DisclaimerInjector`, `Disclaimers.cs` |
| **FR-12** | AI Observability & Audit Logging | Logging of prompt text, raw response, model name, tokens used, and latency (round-trip ms) into `AI_LOG`. | `AiLogService`, `AiLog` |
| **FR-13** | Lawyer Review Queue | Filterable review queue for verified lawyers showing unassigned documents awaiting review. | `ReviewController`, `Queue.cshtml`, `LawyerReviewService` |
| **FR-14** | Lawyer Review & Redline Editing | Interface for lawyers to approve, edit-and-approve (preserving original AI draft and recording final draft), or reject drafts with mandatory review comments. | `ReviewController`, `ReviewDetails.cshtml`, `LawyerReview` |
| **FR-15** | Verified Lawyer Registry | Lawyer credential verification workflow gated by admin approval (`VerificationStatus`). | `LawyerVerificationService`, `LawyerProfile` |
| **FR-16** | Anonymized Analytics Dashboard | Aggregated metrics on case categories, district trends, and turnaround times without exposing PII. | `AdminAnalyticsService`, `Analytics.cshtml` |
| **FR-17** | Legislative Corpus Ingestion & Sync | Batch ingestion and incremental re-embedding based on section `ContentHash`. | `ActsManagementService`, `SeedActsFromJson.cs`, `EmbeddingBatchJob` |
| **FR-18** | Admin Management Console | Comprehensive management of users, lawyer verifications, scenario mappings, and system logs. | `AdminController`, `Views/Admin/*` |

---

## 2. Non-Functional Requirements (NFR)

- **Security & Privacy:** 
  - Field-level encryption for sensitive citizen PII in cases using `EncryptionService.cs`.
  - Enforced TLS 1.2+ and secure HTTP-only cookies.
  - Role-based authorization policies (Citizen, Lawyer, Admin).
- **Reliability & Graceful Degradation:**
  - If Qdrant is offline or times out, the system automatically falls back to SQL Server Full-Text Search.
  - External AI calls are guarded with Polly timeout and retry policies.
- **Localization & Accessibility:**
  - Multi-language resource files (`.resx`) for UI labels in Bengali and English.
  - Bundled Bangla Unicode font (e.g., Kalpurush / SolaimanLipi / Noto Sans Bengali) ensuring consistent rendering on all devices and PDF exports.
  - Mobile-first responsive Razor layout.
- **Auditability & Traceability:**
  - Every AI invocation is immutably recorded in `AI_LOG`.
  - Every lawyer edit preserves both the raw AI draft (`ContentDraft`) and lawyer's final edit (`ContentFinal`).
- **Data Compliance & Licensing:**
  - Primary dataset attribution: CC BY-SA 4.0 (`docs/attribution-CC-BY-SA-4.0.md`).
  - Pre-processed one-time batch import rather than live scraping.
- **Maintainability:**
  - Strict Clean Architecture separation (Domain $\rightarrow$ Application $\rightarrow$ Infrastructure $\rightarrow$ Web).

---

## 3. Mandatory Disclaimer Policy

The legal disclaimer ("MuktoAin provides general legal information and document drafting assistance, not formal legal advice...") must be enforced across **three independent surfaces**:

1. **Persistent UI Banner:** Rendered in `_Layout.cshtml` via `_DisclaimerBanner.cshtml` on every page; non-dismissible.
2. **AI Service Boundary Injection:** `DisclaimerInjector.cs` injects the statutory disclaimer text directly into all AI response payloads (rights explanations, statutory advice, draft contents) before returning from the Application layer.
3. **Document Template Stamp:** Stamped server-side directly into all document templates and QuestPDF exports, preventing removal during lawyer review.

---

## 4. NLP / Evaluation Workstream

- **Benchmark Dataset:** `momahadi/bangladesh-legal-qa-dataset` (2,165 annotated Bangla/English QA pairs grounded in 6 Acts + 3 schedules with IRAC reasoning; CC BY 4.0).
- **Evaluation Harness:** Automated integration test suite (`tests/MuktoAin.IntegrationTests/AiPipeline/`) evaluating:
  1. *Retrieval Accuracy:* Top-$k$ recall of the correct statutory section.
  2. *Answer Correctness:* LLM grounded answer accuracy compared to the gold-standard ground truth.
- **Few-Shot Steering:** Representative QA samples (with IRAC explanations) injected via `PromptAssembler.cs` to guide Gemini's citations.
- **Baseline vs. Few-Shot Reporting:** Evaluation pipeline measures baseline zero-shot retrieval/accuracy against few-shot guided performance for academic reporting.
