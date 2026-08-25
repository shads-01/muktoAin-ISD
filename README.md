# ⚖️ MuktoAin (মুক্ত আইন) — Local Development Setup

> **AI-augmented legal-aid platform for Bangladesh.** 🇧🇩
> This guide gets you from a fresh clone to a running local environment in minutes! 🚀

For project background, architecture, and coding rules, please see [AGENTS.md](AGENTS.md). For the full technical specification, explore [.agent/spec/](.agent/spec/).

---

## 📑 Table of Contents
- [1. 🛠️ Prerequisites](#1-️-prerequisites-install-once-per-machine)
- [2. 🚀 First-Time Setup](#2--first-time-project-setup)
- [3. 🔄 Daily Dev Workflow](#3--every-time-local-dev-workflow)
- [4. 📁 Project Structure](#4--project-structure)
- [5. 🆘 Troubleshooting](#5--troubleshooting)

---

## 1. 🛠️ Prerequisites (Install once per machine)

Make sure you have the following installed before starting:

| Tool | Version | Notes |
|---|---|---|
| **.NET SDK** | `8.0.400+` | Pinned in [`global.json`](global.json). Verify with: `dotnet --version` |
| **SQL Server** | 2022 Express / Dev | **Must have Full-Text Search!** (See note below) |
| **SSMS** | Latest | [SQL Server Management Studio](https://learn.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms) to run schema scripts |
| **Git** | Recent | For version control |
| **LibMan CLI** | Latest | Restores frontend vendor libraries. Run: `dotnet tool install -g Microsoft.Web.LibraryManager.Cli` |

> [!WARNING]
> **Do NOT use LocalDB!** It doesn't support Full-Text Search, which this project requires. SQL Server must be installed **with the "Full-Text and Semantic Extractions for Search" feature.**

### 🔍 Verifying SQL Server has Full-Text Search

Open SSMS, connect to your instance, and run:
```sql
SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS IsFTSInstalled;
```
> [!IMPORTANT]
> Must return `1`. If it returns `0`, re-run the SQL Server installer. Choose **Installation → Add features to an existing instance of SQL Server** and check **Full-Text and Semantic Extractions for Search**.

---

## 2. 🚀 First-Time Project Setup

Follow these steps to get your local environment running:

### Step 1: Clone & Restore
```bash
# 1. Clone and check out your feature branch
git clone https://github.com/shads-01/muktoAin-ISD.git
cd muktoAin-ISD
git checkout <your-branch>

# 2. Restore .NET packages
dotnet restore src/MuktoAin.sln

# 3. Restore frontend vendor libraries (Bootstrap, jQuery)
cd src/MuktoAin.Web
libman restore
cd ../..
```

### Step 2: Configure Environment
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

### Step 3: Database & Schema
We use manual SQL scripts (no EF Core migrations). Run them all at once:

```powershell
# In PowerShell:
.\scripts\run-all.ps1
```
*(If your instance isn't `SQLEXPRESS`, run: `.\scripts\run-all.ps1 -ServerInstance ".\YourInstanceName"`)*

> [!TIP]
> Prefer SSMS? You can manually run the scripts in order: `01_init_database.sql` → `02_schema.sql` → `03_fulltext.sql`. 

**SSMS Connection Values:**

| Field | Value |
|---|---|
| **Server name** | `.\SQLEXPRESS` (or your instance) |
| **Authentication** | Windows Authentication |
| **Database** | `MuktoAin` |

### Step 4: Build & Run! 🎉
```bash
dotnet build src/MuktoAin.sln
dotnet run --project src/MuktoAin.Web
```
Open **`http://localhost:5082`** — that's what plain `dotnet run` binds by default (per [launchSettings.json](src/MuktoAin.Web/Properties/launchSettings.json)'s `http` profile). Always check the console output for the actual URL:
```
Now listening on: http://localhost:5082
```
Welcome to MuktoAin! 

---

## 3. 🔄 Every-Time Local Dev Workflow

Your daily routine when working on the project:

```bash
# 1. Get the latest code
git fetch origin main
git merge origin/main          # (or rebase, per your team's convention)

# 2. Re-run DB scripts if they changed (they are idempotent & safe!)
.\scripts\run-all.ps1

# 3. Build and run
dotnet build src/MuktoAin.sln
dotnet run --project src/MuktoAin.Web

# 4. Commit your work to a feature branch (never directly to main)
git checkout -b your-feature-branch
git add .
git commit -m "Your descriptive message"
git push -u origin your-feature-branch
```

---

## 4. 📁 Project Structure

This project follows Clean Architecture principles:

```text
src/
 ├── MuktoAin.Domain/         # Entities, enums, interfaces (Zero external dependencies)
 ├── MuktoAin.Application/    # Business logic, DTOs, service implementations
 ├── MuktoAin.Infrastructure/ # EF Core, SQL repositories, Gemini/Qdrant clients
 └── MuktoAin.Web/            # ASP.NET Core MVC (Controllers, Views, Program.cs)

tests/
 ├── MuktoAin.UnitTests/      # Fast, isolated tests (EF InMemory provider)
 └── MuktoAin.IntegrationTests/ # End-to-end tests requiring real SQL Server

scripts/                      # Manual MSSQL DDL scripts (Run in order: 01 → 02 → 03)
data/                         # Seed JSON (districts, categories, scenario mappings)
plans/                        # Execution plans + Dependency_plan.md tracker
.agent/spec/                  # Full requirements, architecture, and task specs
```

### 🧪 Running Tests
```bash
dotnet test src/MuktoAin.sln              # Run all tests
dotnet test tests/MuktoAin.UnitTests      # Run unit tests only (fast, no DB needed)
```

---

## 5. 🆘 Troubleshooting

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
Your SQL Server instance lacks Full-Text Search. See the verification query in Step 1. Remember: LocalDB does not support this!
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
git push -u origin your-branch-name
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
