# MuktoAin (মুক্ত আইন) — Agent Guidelines & Project Overview

Welcome to the MuktoAin codebase. This document is the core instruction and boundary manual for AI coding agents working on this repository.

---

## 1. Project Mission & Identity

**MuktoAin (মুক্ত আইন)** is an AI-augmented legal-aid platform for Bangladesh. 
- **Citizen Experience:** Citizens describe a legal problem in Bangla, English, or mixed Banglish. The platform retrieves relevant Bangladeshi statutes, explains applicable rights in plain language, and auto-drafts structured legal documents (General Diary applications, RTI requests, Labour complaints, Consumer complaints, etc.).
- **Human-in-the-Loop Safeguard:** Every AI-generated draft is locked behind a **mandatory verified-lawyer review gate** before a citizen can download/finalize it.
- **Academic Context:** Two-course paired project (treat FR/NFR requirements and the 3-checkpoint execution plan as a single unified specification).
- **Name & Stack Note:** Supersedes any earlier concept draft (formerly "BanglaLex" / Claude API). Do NOT use or reference BanglaLex.

---

## 2. Technology Stack & Frameworks

| Layer | Choice | Rationale & Boundaries |
|---|---|---|
| **Backend Framework** | ASP.NET Core MVC (.NET 8) | Official course stack |
| **Language** | C# (.NET 8) | Course language |
| **Data Access & Queries** | Manual MSSQL Queries & Scripts (`Microsoft.Data.SqlClient` / Dapper / EF Raw SQL) | Explicit parameterized SQL queries written and executed via SSMS and repository layer |
| **Primary Relational DB** | Microsoft SQL Server (SSMS) | Database schema and queries managed manually in SSMS; LocalDB / SQL Server Express / Dev for local |
| **Vector DB (RAG)** | Qdrant (.NET SDK) | Official vector store for chunk embeddings (Qdrant Cloud initially; Docker container in CP3) |
| **Full-Text Search (FTS)** | SQL Server FTS | Configured via manual T-SQL scripts in SSMS; **fallback only** when Qdrant is unavailable, plus standalone Acts search (FR-7) |
| **Embedding Model** | Google `text-embedding-004` | Multilingual (Bangla, English, Banglish) |
| **Generation Model** | Gemini API (`gemini-2.5-flash` via Google AI Studio free tier) | Best-in-class Bengali; behind swappable `IAiService` interface |
| **Frontend** | Razor Views + Bootstrap 5 + Vanilla JS / Fetch | Server-rendered MVC (no SPA frameworks like React/Vue/Angular) |
| **Authentication** | ASP.NET Core Identity | Role-based (Citizen, Lawyer, Admin) |
| **PDF Generation** | QuestPDF (MIT-licensed) | Server-side generation from finalized lawyer-approved content |
| **Batch / Workers** | `IHostedService` / Background Services | Ingestion, chunking, embedding batch jobs |
| **Resilience & Fault Handling** | Polly | Retry and circuit breaker policies for external API calls |

---

## 3. Strict Architectural Rules & Boundaries

1. **Clean Architecture (4 Projects):**
   - `MuktoAin.Domain`: Core entities, enums, interfaces, constants (PromptTemplates, Disclaimers). Zero dependencies on external libraries or other projects.
   - `MuktoAin.Application`: DTOs, service interfaces & implementations (business logic, AI orchestration, state machines).
   - `MuktoAin.Infrastructure`: Manual MSSQL DDL scripts, SQL repo implementations, Qdrant client, Gemini client, QuestPDF, encryption.
   - `MuktoAin.Web`: Presentation layer, Razor Controllers, Views, ViewModels, localization resource files.
2. **Schema & Model Integrity:**
   - **14 Entities Total:** 13 core entities + `ACT_SECTION_CHUNK`.
   - `LAWYER_PROFILE` has **NO direct `CaseId`** (reviews reach cases solely via `GENERATED_DOCUMENT`).
   - `LAWYER_PROFILE` has its own `LawyerProfileId` (PK) and connects to `USER` via a 1-to-(0..1) relationship (`UserId` as a UNIQUE FK, not a shared PK).
   - `CASE.DistrictId` must be a foreign key to `DISTRICT` (64 rows), not a free-text field.
   - `ACT_FOOTNOTE` is an Act-level entity (captures amendment history).
   - Citations operate at the section level (`CASE_ACT_REFERENCE.SectionId`), while vector embeddings operate at the chunk level (`ACT_SECTION_CHUNK`).
3. **Retrieval Flow (Vector-Primary with Fallback):**
   - Normal path queries Qdrant for top-$k$ section chunks.
   - SQL Server FTS is **only invoked as a fallback** if Qdrant fails/times out, or when a user explicitly performs standalone keyword search across Acts (FR-7). Do NOT run hybrid queries concurrently on every request.
4. **Mandatory 3-Surface Disclaimer Policy:**
   - (1) Persistent UI banner on every page (`_DisclaimerBanner.cshtml`).
   - (2) Injected into every AI output via `DisclaimerInjector.cs`.
   - (3) Stamped permanently into generated document templates and PDFs.

---

## 4. Structured Specification Reference

Detailed project knowledge is maintained in the `.agent/spec/` directory:
- [Requirements & Specifications](file:///.agent/spec/requirements.md) — Functional requirements (FR-1 to FR-18), NFRs, evaluation benchmark, disclaimer rules.
- [Architecture & Database Design](file:///.agent/spec/design.md) — 4-project structure, 14-entity relational schema, specialization design, vector search & RAG pipeline.
- [Execution Plan & Tasks](file:///.agent/spec/tasks.md) — 3-checkpoint breakdown, target repository file layout, exit criteria.
