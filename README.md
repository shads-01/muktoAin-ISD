# ⚖️ MuktoAin (মুক্ত আইন)

> **AI-augmented legal-aid platform for Bangladesh.** 🇧🇩
> Citizens describe a legal problem in Bangla, English, or mixed Banglish —
> MuktoAin retrieves the relevant statutes, explains their rights in plain
> language, and drafts structured legal documents (GD applications, RTI
> requests, labour & consumer complaints). **Every AI-generated draft is locked
> behind a mandatory verified-lawyer review gate** before a citizen can use it.

## 👥 Team

| Member | Role | Area |
|---|---|---|
| **Shads** | Project Lead | Identity & AI core, RAG ingestion, evaluation, delivery |
| **Tultul** | Data Foundation | Schema, entities, repositories, search infrastructure |
| **Arpita** | Document Pipeline | Case/document services, lawyer review gate, admin |
| **Erin** | Frontend | Razor views, mock-first UI, final integration |

---

## 📑 Table of Contents
- [1. 🧰 Technology Stack](#1--technology-stack)
- [2. 🏛️ Architecture Overview](#2-️-architecture-overview)
- [3. ⚡ Quick Start](#3--quick-start)
- [4. 🐳 Docker](#4--docker)
- [5. 📊 Dataset Attribution & Licenses](#5--dataset-attribution--licenses)
- [6. ⚠️ Legal Disclaimer](#6-️-legal-disclaimer)
- [7. 🆘 Troubleshooting](#7--troubleshooting)

---

## 1. 🧰 Technology Stack

Full rationale in [AGENTS.md §2](AGENTS.md).

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core MVC (.NET 8), C# |
| Data access | Manual parameterized MSSQL queries (repository layer) + EF Core mapping onto hand-authored schema |
| Relational DB | Microsoft SQL Server (schema managed via SSMS scripts) |
| Vector DB | Qdrant (.NET SDK) |
| Full-text fallback | SQL Server FTS |
| Embeddings | Google `gemini-embedding-001` (3072-dim) |
| Generation | Gemini Flash API (multi-key rotation, Polly resilience) |
| Frontend | Razor Views + Bootstrap 5 + vanilla JS/Fetch |
| Auth | ASP.NET Core Identity (Citizen / Lawyer / Admin) |
| PDF | QuestPDF |

## 2. 🏛️ Architecture Overview

Clean Architecture, 4 projects:

```text
src/
 ├── MuktoAin.Domain/         # Entities, enums, interfaces, constants
 ├── MuktoAin.Application/    # DTOs, business logic, AI orchestration
 ├── MuktoAin.Infrastructure/ # SQL repos, Gemini/Qdrant clients, QuestPDF, encryption
 └── MuktoAin.Web/            # MVC controllers, views, viewmodels, localization
```

Retrieval flow: **vector-primary** (Qdrant top-k over `ACT_SECTION_CHUNK`
embeddings) with SQL Server FTS as an explicit **fallback only** (Qdrant outage
or standalone keyword search, FR-7). Every AI output passes three disclaimer
surfaces: persistent UI banner → injected into AI responses → stamped into
finalized documents/PDFs.

Deep dive: [.agent/spec/design.md](.agent/spec/design.md),
[requirements](.agent/spec/requirements.md),
[execution plan](.agent/spec/tasks.md), and
[deployment guide](docs/deployment-guide.md).
*(A rendered `docs/architecture.md` with ERD lands with Tultul's T-3.5.)*

---

## 3. ⚡ Quick Start

> Detailed first-time setup lives below in
> [Local Development Setup](#-muktoain-মকত-আইন)--local-development-setup;
> the short version:

1. Install prerequisites: .NET SDK `8.0.400+`, SQL Server 2022 **with
   Full-Text Search** (not LocalDB!), SSMS, LibMan CLI.
2. Clone, restore: `dotnet restore src/MuktoAin.Web/MuktoAin.Web.csproj` +
   `libman restore` in `src/MuktoAin.Web`.
3. Copy `appsettings.Development.json.template` →
   `appsettings.Development.json`; fill in DB connection, Gemini keys, Qdrant
   endpoint/key, seed-admin password.
4. Apply schema: `.\scripts\run-all.ps1`
5. Run: `dotnet run --project src/MuktoAin.Web`

Everything seeds automatically and idempotently on startup (districts,
categories, scenario mappings, Acts import when the Kaggle dataset is present,
section chunking, initial admin user).

### 🧪 Tests

```bash
dotnet test tests/MuktoAin.UnitTests          # fast, no DB needed
dotnet test tests/MuktoAin.IntegrationTests   # needs real SQL Server (+ secrets for AI tests)
```

---

## 4. 🐳 Docker

```bash
docker build -t muktoain-web .
docker run -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="..." \
  -e Gemini__ApiKeys__0="..." \
  -e Qdrant__Endpoint="..." -e Qdrant__ApiKey="..." \
  muktoain-web
```

See the [deployment guide](docs/deployment-guide.md) for the full environment
variable reference, Azure topology, and CI integration-test opt-in.

---

## 5. 📊 Dataset Attribution & Licenses

- **Bangladesh Legal Acts Dataset** — Kaggle (`sakhadib/bangladesh-legal-acts-dataset`),
  ~50–100MB, git-ignored; download instructions and SHA256 verification in
  [data/README.md](data/README.md).
- **Bangladesh Legal QA benchmark** — Kaggle (`momahadi/bangladesh-legal-qa-dataset`),
  2,165 questions, used by the CP3 evaluation harness.

Full license/attribution document (`docs/attribution-CC-BY-SA-4.0.md`) lands
with task A-3.6.

---

## 6. ⚠️ Legal Disclaimer

> MuktoAin provides general legal information and document drafting assistance.
> This is **NOT formal legal advice**. Every document must be reviewed by a
> verified lawyer before use. For urgent legal matters, consult a qualified advocate.

> মুক্ত আইন সাধারণ আইনি তথ্য ও নথি প্রণয়নে সহায়তা প্রদান করে। এটি আনুষ্ঠানিক আইনি
> পরামর্শ নয়। প্রতিটি নথি ব্যবহারের পূর্বে একজন যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা
> করা আবশ্যক।

---

# ⚖️ Local Development Setup

This section gets you from a fresh clone to a running local environment in minutes! 🚀

For project background, coding rules, see [AGENTS.md](AGENTS.md). For the full technical specification, explore [.agent/spec/](.agent/spec/).

### 🔍 Verifying SQL Server has Full-Text Search

Open SSMS, connect to your instance, and run:
```sql
SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS IsFTSInstalled;
```
> [!IMPORTANT]
> Must return `1`. If it returns `0`, re-run the SQL Server installer. Choose **Installation → Add features to an existing instance of SQL Server** and check **Full-Text and Semantic Extractions for Search**.

### 🚀 First-Time Project Setup

**Step 1: Clone & Restore**
```bash
git clone https://github.com/shads-01/muktoAin-ISD.git
cd muktoAin-ISD
git checkout <your-branch>

dotnet restore src/MuktoAin.slnx

cd src/MuktoAin.Web
libman restore
cd ../..
```

**Step 2: Configure Environment**
Copy the template to create your local settings (this file is git-ignored to protect secrets):

**Windows (CMD/PowerShell):**
```cmd
copy src\MuktoAin.Web\appsettings.Development.json.template src\MuktoAin.Web\appsettings.Development.json
```
**Mac/Linux (Bash):**
```bash
cp src/MuktoAin.Web/appsettings.Development.json.template src/MuktoAin.Web/appsettings.Development.json
```

> [!NOTE]  
> Edit `src/MuktoAin.Web/appsettings.Development.json`. The `DefaultConnection` string usually works as-is if your SQL Server instance is named `SQLEXPRESS`. If you have a custom named instance, adjust the `Server=` property!

**Step 3: Database & Schema**
We use manual SQL scripts (no EF Core migrations). Run them all at once:

```powershell
.\scripts\run-all.ps1
```
*(If your instance isn't `SQLEXPRESS`, run: `.\scripts\run-all.ps1 -ServerInstance ".\YourInstanceName"`)*

> [!TIP]
> Prefer SSMS? Manually run the scripts in order: `01_init_database.sql` → `02_schema.sql` → `03_fulltext.sql`.

**Step 4: Build & Run! 🎉**
```bash
dotnet build src/MuktoAin.sln
dotnet run --project src/MuktoAin.Web
```
Open **`http://localhost:5250`** — that's what plain `dotnet run` binds by default (per [launchSettings.json](src/MuktoAin.Web/Properties/launchSettings.json)'s `http` profile). Always check the console output for the actual URL:
```
Now listening on: http://localhost:5250
```

> [!NOTE]
> **Data seeds automatically on startup — no separate seed command.** Every `dotnet run` seeds districts, categories, and (if `data/bangladesh-acts-dataset.json` is present) the 1,484 Bangladesh Acts into your DB. It's idempotent — safe to run every time, it only inserts what's missing.
>
> The Acts dataset is large and **not committed to git** — see [data/README.md](data/README.md) to download it from Kaggle. Don't have it yet? That's fine: the app logs a warning and starts normally without it, you just won't have Acts/Sections data until you download it. First import with the file present takes a couple of minutes (1,484 rows, one at a time) — later runs are instant since it skips what's already there.

### 🔄 Daily Dev Workflow

```bash
# 1. Get the latest code
git fetch origin main
git merge origin/main          # (or rebase, per your team's convention)

# 2. Re-run DB scripts if they changed (they are idempotent & safe!)
.\scripts\run-all.ps1

# 3. Build and run
dotnet build src/MuktoAin.slnx
dotnet run --project src/MuktoAin.Web

# 4. Commit your work to a feature branch (never directly to main)
git checkout -b your-feature-branch
git add .
git commit -m "Your descriptive message"
git push -u origin your-feature-branch
```

## 7. 🆘 Troubleshooting

<details>
<summary><b>Build fails with MSB3027/MSB3021 (file locked)</b></summary>
<br>
A previous <code>dotnet run</code> is still running in the background. Kill it:

```powershell
Get-Process MuktoAin.Web -ErrorAction SilentlyContinue | Stop-Process -Force
```
</details>

<details>
<summary><b>scripts/03_fulltext.sql fails or Acts search returns no results</b></summary>
<br>
Your SQL Server instance lacks Full-Text Search. See the verification query above. Remember: LocalDB does not support this!
</details>

<details>
<summary><b>"ActImportService: 'bangladesh-acts-dataset.json' not found -- skipping" warning on startup</b></summary>
<br>
This is expected if you haven't downloaded the Acts dataset yet — it's optional for most tasks. See <a href="data/README.md">data/README.md</a> for the Kaggle link and SHA256 to verify it. The app runs fine without it; you just won't have Acts/Sections data until it's in place.
</details>

<details>
<summary><b>Filtered index errors (QUOTED_IDENTIFIER) in SSMS</b></summary>
<br>
If you copy-paste parts of <code>02_schema.sql</code> into a fresh query window, make sure to include <code>SET QUOTED_IDENTIFIER ON;</code> at the top before running any filtered <code>CREATE INDEX</code> statements.
</details>

<details>
<summary><b>git push to main is rejected</b></summary>
<br>
<code>main</code> is protected! Move your work to a branch:

```bash
git branch your-branch-name
git reset --hard origin/main
git checkout your-branch-name
git push -u origin your-feature-branch
```
</details>

<details>
<summary><b>Frontend looks completely unstyled</b></summary>
<br>
Your <code>wwwroot/lib/</code> might be missing. Run:

```bash
cd src/MuktoAin.Web
libman restore
```
</details>
