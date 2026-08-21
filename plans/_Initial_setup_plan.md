# Initial Setup Plan — Local Development Environment

> **Prerequisite**: Shads has completed Part 1 of [Shads_plan.md](file:///d:/Projects/muktoAin-ISD/plans/Shads_plan.md) — the GitHub repo exists, branches are created, `.gitignore` and `.editorconfig` are committed, and `appsettings.Development.json.template` is in the repo.

This guide gets every teammate from zero to a running local dev environment. 
- All database tables, indexes, constraints, and Full-Text Search are managed via **SQL Server Management Studio (SSMS)** using **manual MSSQL scripts and queries**.
- **Qdrant Vector Database** runs on **Qdrant Cloud (Free Tier)** initially for Checkpoints 1 & 2. Docker containerization is deferred to **Checkpoint 3**.

---

## 1. Software Prerequisites

### 1.1 .NET 8 SDK

1. Download from [dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Install the **SDK** (not just the Runtime).
3. Verify:
   ```
   dotnet --version
   ```
   Should output `8.0.x`.

### 1.2 Microsoft SQL Server

You need a running SQL Server instance on your machine:
- **SQL Server 2022 Express** (Recommended) from [microsoft.com/sql-server/sql-server-downloads](https://www.microsoft.com/sql-server/sql-server-downloads) (Basic or Custom with **Full-Text and Semantic Extractions for Search** feature enabled).
- **OR SQL Server LocalDB** (included with Visual Studio 2022). Verify with:
  ```
  sqllocaldb info
  sqllocaldb start MSSQLLocalDB
  ```

> [!IMPORTANT]
> **Full-Text Search (FTS)** is required for FR-7 (standalone Acts search) and the FTS fallback path.
> - When installing SQL Server Express, make sure **Full-Text Search** is checked.
> - To verify FTS is active on your server, open SSMS and execute:
>   ```sql
>   SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS IsFTSInstalled;
>   ```
>   Must return `1`.

### 1.3 SQL Server Management Studio (SSMS) — **Mandatory**

SSMS is the primary tool for managing our database, executing manual MSSQL DDL/DML scripts, creating Full-Text catalogs, and writing/testing queries.

1. Download the latest SSMS from [learn.microsoft.com/sql/ssms](https://learn.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms).
2. Install SSMS.
3. Open SSMS and verify you can connect to your local server:
   - Server Name: `(localdb)\mssqllocaldb` OR `.\SQLEXPRESS` OR `localhost`
   - Authentication: **Windows Authentication**

### 1.4 Qdrant Cloud Free Tier (No Docker Required for CP1 & CP2)

For Checkpoints 1 & 2, we use a cloud-hosted vector database on **Qdrant Cloud Free Tier** (1GB storage, free forever).
- Shads sets up the shared team cluster on [cloud.qdrant.io](https://cloud.qdrant.io) and provides the cluster endpoint URL and API key.
- No local Docker installation is needed until Checkpoint 3 (where Docker containerization and local deployment are introduced).

---

## 2. Repository Branch


After opening the repo folder in any IDE, you need to switch to your assigned feature branch:

| Teammate | Branch | Role Focus |
|---|---|---|
| Shads | `shads/identity-ai-core` | Auth, Gemini client, encryption, AI logging |
| Tultul | `tultul/schema-data-pipeline` | MSSQL schema scripts, SQL repos, batch ingestion, Qdrant/FTS search |
| Arpita | `arpita/document-pipeline` | Case DTOs, document templates, QuestPDF, review flow |
| Erin | `erin/frontend-views` | Razor Views, Bootstrap 5, responsive UI, localization |

```
git checkout <your-branch>
```

---

## 3. Configure `appsettings.Development.json`

1. Copy the template file:
   ```
   copy src\MuktoAin.Web\appsettings.Development.json.template src\MuktoAin.Web\appsettings.Development.json
   ```

2. Open `appsettings.Development.json` and fill in the database connection string and cloud credentials:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MuktoAin;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
     },
     "Gemini": {
       "ApiKeys": [
         "YOUR_KEY_HERE"
       ],
       "EmbeddingModel": "text-embedding-004",
       "GenerationModel": "gemini-2.5-flash"
     },
     "Qdrant": {
       "Endpoint": "https://your-cluster-id.cloud.qdrant.io:6333",
       "ApiKey": "YOUR_QDRANT_CLOUD_API_KEY"
     }
   }
   ```
   *(If you are using SQL Server Express instead of LocalDB, set `Server=.\\SQLEXPRESS;Database=MuktoAin;...`)*

3. **Paste the Gemini API keys and Qdrant Cloud credentials** provided by Shads into their respective fields.

> [!WARNING]
> Never commit `appsettings.Development.json` to Git.

---

## 4. Database Setup in SSMS (Manual MSSQL Scripts)

We do **not** use blind automated code-first migrations. All schema tables, foreign keys, indexes, Full-Text catalogs, and seed data are created via manual MSSQL scripts located in the `scripts/` folder of the repository.

### 4.1 Step-by-Step Script Execution in SSMS

1. Launch **SSMS** and connect to your SQL Server instance (`(localdb)\mssqllocaldb` or `.\SQLEXPRESS`).
2. **Create Database**:
   - In SSMS, go to `File` $\rightarrow$ `Open` $\rightarrow$ `File...` and select `scripts/01_init_database.sql` (or create a new query window):
     ```sql
     IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MuktoAin')
     BEGIN
         CREATE DATABASE MuktoAin;
     END
     GO
     USE MuktoAin;
     GO
     ```
   - Press **F5** (Execute).
3. **Execute Table Schema Script**:
   - Open `scripts/02_schema.sql` in SSMS.
   - Ensure the database dropdown at top-left shows `MuktoAin`.
   - Press **F5** (Execute).
   - This creates all **14 relational tables** with explicit primary keys, foreign key constraints, default values, and standard indexes.
4. **Execute Full-Text Search Script**:
   - Open `scripts/03_fulltext.sql` in SSMS.
   - Press **F5** (Execute).
   - This creates the Full-Text Catalog `ftCatalog_MuktoAin` and the Full-Text Index on `ActSection(SectionText, ActTitle)`.
5. **Execute Seed Data Script**:
   - Open `scripts/04_seed_data.sql` in SSMS.
   - Press **F5** (Execute).
   - This seeds the 64 districts, initial case categories, scenario mappings, and the default admin user.

### 4.2 Verifying in SSMS Object Explorer

In SSMS Object Explorer, expand `Databases` $\rightarrow$ `MuktoAin` $\rightarrow$ `Tables`. You should see all 14 tables: `USER`, `LAWYER_PROFILE`, `CASE_CATEGORY`, `DISTRICT`, `ACT`, `ACT_SECTION`, `ACT_SECTION_CHUNK`, `ACT_FOOTNOTE`, `CASE`, `SCENARIO_MAPPING`, `GENERATED_DOCUMENT`, `CASE_ACT_REFERENCE`, `LAWYER_REVIEW`, `AI_LOG`.

---

## 5. Qdrant Setup (Qdrant Cloud)

We use **Qdrant Cloud Free Tier** as our cloud vector store for CP1 & CP2:

1. **Cluster Creation (Shads)**:
   - Shads creates a free 1GB cluster on [cloud.qdrant.io](https://cloud.qdrant.io).
   - Generates an API key with read/write access to the `act_section_chunks` collection.
   - Shares the cluster URL (e.g. `https://xxxxxx.us-east-1.cloud.qdrant.io:6333`) and API key with the team.
2. **Teammate Configuration**:
   - Paste the shared URL and API key into `appsettings.Development.json`.
3. **Collection Creation**:
   - Automatically ensured on application startup via `IVectorStore.EnsureCollectionAsync()` (768-dimensional Cosine distance for `text-embedding-004`).
4. **Checkpoint 3 Note**:
   - In Checkpoint 3, Docker containerization (`docker-compose.yml` with local Qdrant + SQL Server container) will be configured for local offline testing and CI/CD pipelines.

---

## 6. Gemini API Key Setup

Each teammate generates a free API key from [aistudio.google.com](https://aistudio.google.com) (without enabling billing) and sends it to Shads to assemble the 4-key round-robin pool.

---

## 7. Build, Run and Verify

### 7.1 Build Solution

```
dotnet restore src/MuktoAin.sln
dotnet build src/MuktoAin.sln
```

### 7.2 Run Web App

```
dotnet run --project src/MuktoAin.Web
```
Open `http://localhost:5000` or `https://localhost:5001`.

### 7.3 Verification Checklist
- [ ] Disclaimer banner appears at the top of all pages
- [ ] SSMS shows all 14 tables created under `MuktoAin`
- [ ] Qdrant Cloud connection succeeds on app startup
- [ ] Standalone search page can query `ActSection` via SQL Server FTS
- [ ] Admin login works with `admin@muktoain.bd` / `Admin@123!`

---

## 8. Who Needs What — Quick Reference

| Requirement | Tultul | Shads | Arpita | Erin |
|---|---|---|---|---|
| .NET 8 SDK | ✅ Day 1 | ✅ Day 1 | ✅ Day 1 | ✅ Day 1 |
| SQL Server + SSMS | ✅ Day 1 | ✅ Day 1 | ✅ Day 1 | ❌ (Uses mock data initially) |
| Manual SQL Scripts (`scripts/`) | ✅ Creates/Runs | ✅ Runs/Updates | ✅ Runs | ❌ |
| Qdrant Cloud (URL & API Key) | ✅ Day 3 | ✅ Sets up Day 1 | ❌ (CP2) | ❌ |
| Gemini API Keys | ❌ (CP2) | ✅ Day 1 | ❌ (CP2) | ❌ |
| Docker Desktop | ❌ (CP3 only) | ❌ (CP3 only) | ❌ (CP3 only) | ❌ (CP3 only) |

---

## 9. Troubleshooting

### SSMS: Cannot connect to `(localdb)\mssqllocaldb`
- Open command prompt and run:
  ```
  sqllocaldb start MSSQLLocalDB
  ```
- If using SQL Server Express, connect to `.\SQLEXPRESS` or `localhost` instead.

### Qdrant: 401 Unauthorized or Connection Error
- Check that the `Endpoint` in `appsettings.Development.json` has `https://` prefix and port `:6333`.
- Verify the `ApiKey` matches the key shared by Shads from Qdrant Cloud.

### Script execution error: "Full-Text Search is not installed"
- Rerun SQL Server installer and add the **Full-Text and Semantic Extractions for Search** feature.

---

## 10. Daily Workflow Cheat Sheet

```
# 1. Pull latest code
git fetch origin main
git merge origin/main

# 2. If SQL scripts were updated, open updated .sql script in SSMS and execute (F5)

# 3. Run web application (Qdrant Cloud is always online)
dotnet run --project src/MuktoAin.Web

# 4. Commit and push feature branch
git add .
git commit -m "feat: your descriptive message"
git push origin <your-branch>
```
