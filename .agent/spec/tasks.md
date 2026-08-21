# MuktoAin — Execution Plan & Task Breakdown

This document outlines the three-checkpoint delivery roadmap, target repository layout, and verification criteria for MuktoAin.

---

## 1. Checkpoint Summary

| Checkpoint | Workload | Focus Area | Exit Criteria |
|---|---|---|---|
| **Checkpoint 1** | 30% | Solution Scaffolding, 14-Entity MSSQL Schema (SSMS DDL Scripts), ASP.NET Identity, Statutory Ingestion & Chunking, Qdrant Embedding Job | MSSQL schema scripts execute cleanly in SSMS; 1,484 Acts imported and chunked; Qdrant indexed; Identity & seed admin operational; basic case submission saved without AI. |
| **Checkpoint 2** | 45% | Vector-Primary RAG, Rights Explanation, Document Drafting (Templates), PDF Export (QuestPDF), SQL FTS Fallback & Standalone Search | End-to-end citizen flow: multilingual intake $\rightarrow$ grounded statutory citations $\rightarrow$ auto-drafted document with disclaimer $\rightarrow$ AI logged. Draft status locked. |
| **Checkpoint 3** | 25% | Verified Lawyer Review Gate, Redline Edit History, Admin Analytics & Management, QA Benchmark Evaluation Harness, Deployment | Lawyer review approval/edit/reject workflow; finalized PDF unlock; QA evaluation suite (2,165 questions) passes with benchmark metrics; Dockerfile & CI/CD. |

---

## 2. Detailed Task Breakdown

### Checkpoint 1: Foundation & Data Pipelines (30%)
- [ ] Initialize .NET 8 solution with 4 projects: `MuktoAin.Domain`, `MuktoAin.Application`, `MuktoAin.Infrastructure`, `MuktoAin.Web`.
- [ ] Implement all 14 domain entities, enums, and repository interfaces in `MuktoAin.Domain`.
- [ ] Create manual MSSQL DDL scripts (`scripts/01_schema.sql`, `scripts/02_fulltext.sql`) to execute in SSMS, and configure database connection & SQL repository implementations in `MuktoAin.Infrastructure`.
- [ ] Configure ASP.NET Core Identity with Citizen, Lawyer, and Admin roles (`SeedAdminUser.cs`).
- [ ] Create seed data loaders for `DISTRICT` (64 districts) and `CASE_CATEGORY`.
- [ ] Implement batch ingestion pipeline for Kaggle Bangladesh Legal Acts dataset JSON (`Act` $\rightarrow$ `ActSection` $\rightarrow$ `ActFootnote`).
- [ ] Implement sub-chunking logic and embedding batch job generating `ACT_SECTION_CHUNK` records and storing vectors in Qdrant.
- [ ] Scaffold basic MVC controllers (`AccountController`, `CaseController`, `CategoryController`) and core Razor layout with persistent disclaimer banner.

### Checkpoint 2: Core Citizen Flow & RAG (45%)
- [ ] Implement `GeminiClient` with Polly retry/circuit-breaker configuration in `MuktoAin.Infrastructure`.
- [ ] Implement `QdrantVectorStore` and `SimilaritySearchService` for cosine similarity retrieval.
- [ ] Implement `KeywordSearchService` using SQL Server Full-Text Search (FR-7 standalone search + FR-3 fallback path).
- [ ] Implement `RagContextBuilder` to orchestrate vector retrieval with automatic FTS fallback.
- [ ] Implement `PromptAssembler` and `RightsExplanationService` for grounded legal rights explanations.
- [ ] Build document drafting engine (`DocumentGenerator.cs`) and specific templates (`GeneralDiaryTemplate`, `RtiRequestTemplate`, `LabourComplaintTemplate`, `ConsumerComplaintTemplate`).
- [ ] Implement `DisclaimerInjector` to append legal disclaimers to all AI service responses.
- [ ] Implement `AiLogService` recording every model call into `AI_LOG`.
- [ ] Implement QuestPDF generation in `PdfExportService`.
- [ ] Implement citizen intake views (`Submit.cshtml`, `Track.cshtml`, `_LanguageToggle.cshtml`).

### Checkpoint 3: Review Gate, Admin, Evaluation & Deployment (25%)
- [ ] Implement `LawyerVerificationService` and admin review/approval screens for bar registration numbers.
- [ ] Implement `LawyerReviewService` and queue interface for reviewing unassigned drafts (`UnderReview`).
- [ ] Implement redline editing and approval workflow (`ContentDraft` vs `ContentFinal` diffing).
- [ ] Implement `AdminAnalyticsService` providing anonymized case distributions and district heatmaps.
- [ ] Implement statutory maintenance management (`ActsManagementService` for incremental re-indexing by `ContentHash`).
- [ ] Build automated NLP QA benchmark evaluation harness in `MuktoAin.IntegrationTests/AiPipeline/` using the 2,165 QA dataset.
- [ ] Add few-shot IRAC prompt injection to measure accuracy improvements over zero-shot baseline.
- [ ] Containerize application via `Dockerfile` and configure GitHub Actions CI/CD.

---

## 3. Target Solution File Structure

```
MuktoAin/
├── MuktoAin.sln
├── README.md
├── AGENTS.md
├── .agent/
│   └── spec/
│       ├── requirements.md
│       ├── design.md
│       └── tasks.md
├── Dockerfile
├── docs/
│   ├── architecture.md
│   ├── api-contracts.md
│   ├── attribution-CC-BY-SA-4.0.md
│   └── deployment-guide.md
├── scripts/
│   ├── 01_init_database.sql
│   ├── 02_schema.sql
│   ├── 03_fulltext.sql
│   └── 04_seed_data.sql
├── data/
│   ├── bangladesh-acts-dataset.json
│   ├── districts.json
│   ├── categories.json
│   └── scenario-mappings.json
├── src/
│   ├── MuktoAin.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   └── Constants/
│   ├── MuktoAin.Application/
│   │   ├── DTOs/
│   │   └── Services/
│   ├── MuktoAin.Infrastructure/
│   │   ├── Data/
│   │   ├── Repositories/
│   │   ├── Ai/
│   │   ├── VectorStore/
│   │   ├── Documents/
│   │   ├── Search/
│   │   └── Security/
│   └── MuktoAin.Web/
│       ├── Controllers/
│       ├── ViewModels/
│       ├── Views/
│       ├── Resources/
│       └── wwwroot/
└── tests/
    ├── MuktoAin.UnitTests/
    └── MuktoAin.IntegrationTests/
```
