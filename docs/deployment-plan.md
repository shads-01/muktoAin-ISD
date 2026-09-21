# MuktoAin — Free Hosting Deployment Plan (Azure)

> End-to-end plan for putting MuktoAin on the public internet at zero cost:
> Azure App Service (Free F1, Linux) + Azure SQL Database (free offer) + the
> team's existing Qdrant Cloud cluster + the Gemini free tier, with automatic
> deploys from GitHub Actions.
>
> Local development, Docker and CI are covered in
> [deployment-guide.md](deployment-guide.md). This document only covers hosting.

---

## 0. At a glance

```
                  GitHub (main)
                       │  push / manual run
                       ▼
             GitHub Actions: deploy.yml
      (libman restore → unit tests → publish → bundle seed data)
                       │  zip deploy (publish profile)
                       ▼
Browser ──HTTPS──► Azure App Service  (F1 Linux, .NET 8)
                     │    /home/site/wwwroot  ← app (replaced on every deploy)
                     │    /home/data/keys     ← Data Protection key ring (persistent)
                     │
                     ├──► Azure SQL Database "MuktoAin" (free offer, serverless)
                     │       schema + Acts + chunks + users, imported from Shads's DB
                     ├──► Qdrant Cloud (existing cluster, collection act_section_chunks)
                     └──► Gemini API (AI Studio free-tier keys)
```

What is already done and is **not** repeated here:

- The Qdrant Cloud cluster exists, and the canonical collection
  `act_section_chunks` is fully embedded (3072-dim, `gemini-embedding-001`).
- Shads's merged database holds the Acts, sections and chunks that those
  vectors point to.

The core rule of this plan: **Qdrant and SQL must stay a matched pair.** Every
Qdrant point stores a `SectionId`/`ChunkId`, and search loads the section text
from SQL by that id (`SimilaritySearchService`). So the hosted database must be
a copy of **Shads's database**, not a fresh import of the Kaggle dataset. A
fresh import produces different identity values, and every vector would then
cite the wrong law section.

### Step order

| # | Step | Who | Time |
|---|---|---|---|
| 1 | Create Azure account and resource group | Deployer | 10 min |
| 2 | Create free Azure SQL database | Deployer | 10 min |
| 3 | Export Shads's DB to `.bacpac` and import it | Shads + Deployer | 30–60 min |
| 4 | Create the App Service web app | Deployer | 10 min |
| 5 | Configure app settings (copy keys from your appsettings) | Deployer | 15 min |
| 6 | Set up the GitHub deploy workflow | Repo admin | 10 min |
| 7 | Upload the Data Protection key ring | Shads + Deployer | 10 min |
| 8 | First deploy and smoke test | Deployer | 20 min |

---

## 1. Free-tier limits and what they mean for us

| Service | Free allowance | What happens at the limit | Impact on MuktoAin |
|---|---|---|---|
| Azure App Service **F1** (Linux) | 1 GB RAM, 60 CPU-minutes per day, 1 GB disk, shared compute, no Always On, no custom-domain TLS | App is stopped until the daily quota resets | Fine for demos and grading. Idle app is unloaded, so the first request after idle takes 20–40 s. |
| Azure SQL Database **free offer** | 100,000 vCore-seconds + 32 GB data per database per month, serverless General Purpose | Choose "auto-pause until next month" so it can never bill | DB auto-pauses when idle. The first query after a pause takes up to ~1 min (the connection string's `Connect Timeout=60` and EF retry policy cover this). |
| Qdrant Cloud free cluster | 1 GB RAM / ~4 GB disk | Writes fail when full | Already populated. Read-only use from the hosted app. |
| Gemini API (AI Studio) | Per-project requests-per-minute and per-day caps | HTTP 429, handled by Polly retry + key rotation | Keys from different Google projects add quota; keys from the same project share one pool. |
| GitHub Actions | Free minutes for public repos (2,000/month private) | Workflows queue/stop | One deploy takes a few minutes. |

**Account options.** Azure for Students gives $100 credit with no credit
card (university email). A normal Azure free account needs a card for
identity checks, but everything in this plan stays at $0 if you pick the
free SKUs exactly as written. Do not "upgrade" anything the portal suggests.

---

## 2. Azure account and resource group

1. Sign up at <https://azure.microsoft.com/free/students> (or
   <https://azure.microsoft.com/free>).
2. Install the Azure CLI (optional, but the commands below use it):
   `winget install Microsoft.AzureCLI`, then `az login`.
3. Pick one region and use it for **everything** (DB and app in the same
   region avoids latency and egress). Example: `southeastasia`.
4. Create a resource group:

   ```powershell
   az group create --name muktoain-rg --location southeastasia
   ```

---

## 3. Azure SQL Database (free offer)

### 3.1 Create the server and database

Portal: **Create a resource → SQL Database**.

| Field | Value |
|---|---|
| Resource group | `muktoain-rg` |
| Database name | `MuktoAin` (exact; the SQL scripts reference this name) |
| Server | Create new, e.g. `muktoain-sql-<yourname>`; same region |
| Authentication | **SQL authentication**; create an admin login and a strong password |
| Free offer banner | Click **Apply offer** ("Want to try Azure SQL Database for free?") |
| Behaviour when free limit reached | **Auto-pause the database until next month** |
| Backup storage redundancy | Locally-redundant |

**Networking** tab:

- Connectivity method: **Public endpoint**.
- **Allow Azure services and resources to access this server: Yes**. App
  Service needs this.
- **Add current client IP address: Yes**. You need this for the import.
  Teammates who run SSMS against the hosted DB add their own IP later under
  *Server → Networking*.

**Additional settings**: leave *Use existing data* = **None**. The data comes
from the `.bacpac` in step 3.2. The database must stay **empty** until then.

Connection string (portal: *Database → Connection strings → ADO.NET*), then
add the options this app uses:

```
Server=tcp:muktoain-sql-<yourname>.database.windows.net,1433;Initial Catalog=MuktoAin;User ID=<admin>;Password=<password>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;Connect Timeout=60;
```

### 3.2 Copy Shads's database into Azure (`.bacpac`)

A `.bacpac` carries the schema **and** the data (Acts, sections, chunks with
their `VectorId`, categories, districts, users, cases) in one file. Because
the file brings the schema with it, **do not run `scripts/*.sql` against the
new database before importing**. The import requires an empty database.

**On Shads's machine (source):**

1. Stop the app (`dotnet run`/`dotnet watch`), so nothing writes during export.
2. SSMS → right-click database `MuktoAin` → **Tasks → Export Data-tier
   Application…** → *Save to local disk* → `MuktoAin.bacpac`.

   Or use the CLI (install once: `dotnet tool install -g microsoft.sqlpackage`):

   ```powershell
   sqlpackage /Action:Export `
     /SourceServerName:".\SQLEXPRESS" /SourceDatabaseName:MuktoAin `
     /SourceTrustServerCertificate:True `
     /TargetFile:"MuktoAin.bacpac"
   ```

3. Record row counts, for the check in 3.3:

   ```sql
   SELECT 'ACT' t, COUNT(*) n FROM dbo.ACT
   UNION ALL SELECT 'ACT_SECTION', COUNT(*) FROM dbo.ACT_SECTION
   UNION ALL SELECT 'ACT_SECTION_CHUNK', COUNT(*) FROM dbo.ACT_SECTION_CHUNK
   UNION ALL SELECT 'CHUNK_WITH_VECTOR', COUNT(*) FROM dbo.ACT_SECTION_CHUNK WHERE VectorId IS NOT NULL;
   ```

4. Share `MuktoAin.bacpac` with the deployer privately (it contains user
   accounts and encrypted case data). Never commit it.

If export fails with validation errors (for example, an object referencing
another database), fix or drop that object in the source and retry. The
SqlPackage error text names the object.

**Deployer (target):**

```powershell
sqlpackage /Action:Import `
  /SourceFile:"MuktoAin.bacpac" `
  /TargetServerName:"tcp:muktoain-sql-<yourname>.database.windows.net,1433" `
  /TargetDatabaseName:MuktoAin `
  /TargetUser:<admin> /TargetPassword:"<password>"
```

The import can take 10–40 minutes depending on the Acts corpus size. Serverless
scales up during the import. This uses some of the monthly vCore-second
allowance once, which is expected.

### 3.3 Verify the database

Connect with SSMS to `muktoain-sql-<yourname>.database.windows.net`
(SQL authentication, database `MuktoAin`):

1. Run the row-count query from 3.2. **Every number must match Shads's.**
2. Full-Text Search (needed for the fallback search and FR-7 Acts search):

   ```sql
   SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled');         -- expect 1
   SELECT name FROM sys.fulltext_catalogs;                         -- expect MuktoAinCatalog
   SELECT OBJECT_NAME(object_id) FROM sys.fulltext_indexes;        -- expect ACT_SECTION
   ```

   If the catalog or index is missing, re-run the idempotent script against
   the hosted DB:

   ```powershell
   sqlcmd -S tcp:muktoain-sql-<yourname>.database.windows.net,1433 -d MuktoAin `
     -U <admin> -P "<password>" -i scripts\03_fulltext.sql
   ```

   Population is asynchronous. Wait a few minutes, then check
   `SELECT FULLTEXTCATALOGPROPERTY('MuktoAinCatalog','ItemCount');`.

3. Do **not** run `scripts/01_init_database.sql` on Azure. It runs
   `CREATE DATABASE`/`ALTER DATABASE`, which Azure SQL manages itself
   (Read Committed Snapshot is on by default, and serverless handles pausing).

### 3.4 Future schema changes

When a new `scripts/NN_*.sql` file is merged after this deploy, run **only
that new script** against the hosted DB with the `sqlcmd … -d MuktoAin`
command above. The scripts are idempotent, but do not replay the whole
folder: `01` must not run on Azure.

---

## 4. App Service web app

Portal: **Create a resource → Web App**.

| Field | Value |
|---|---|
| Resource group | `muktoain-rg` |
| Name | e.g. `muktoain-<team>` → URL `https://muktoain-<team>.azurewebsites.net` |
| Publish | **Code** |
| Runtime stack | **.NET 8 (LTS)** |
| Operating system | **Linux** |
| Region | Same as the database |
| Pricing plan | Create new Linux plan → **Free F1** (*Explore pricing plans → Dev/Test → F1*) |
| Deployment / Monitoring tabs | Leave GitHub Actions **off** (this repo ships its own workflow) and Application Insights **off** (not free-tier friendly) |

CLI equivalent:

```powershell
az appservice plan create -g muktoain-rg -n muktoain-plan --is-linux --sku F1
az webapp create -g muktoain-rg -p muktoain-plan -n muktoain-<team> --runtime "DOTNETCORE:8.0"
```

After creation, under *Settings → Configuration → General settings*:

| Setting | Value | Why |
|---|---|---|
| Startup command | `dotnet MuktoAin.Web.dll` | Explicit entry point |
| Web sockets | **On** | SignalR hubs (`Hubs/`) need WebSockets |
| HTTPS Only | **On** (*Settings → Configuration* or *TLS/SSL*) | Force TLS |
| Minimum TLS version | 1.2 | Default, keep it |
| Always On | Not available on F1 | Expect cold starts |

---

## 5. App settings (secrets and configuration)

*Settings → Environment variables → App settings*. Each entry becomes an
environment variable. `__` maps to the `:` config hierarchy.

**No new keys are needed.** The Gemini keys and Qdrant endpoint/key are
already in your local `src/MuktoAin.Web/appsettings.Development.json`. Copy
them from there. Azure never reads that file: it is git-ignored (so CI never
deploys it), and the server runs in `Production`, which does not load
`appsettings.Development.json`.

| In your `appsettings.Development.json` | Azure App setting |
|---|---|
| `Gemini:ApiKeys` → 1st, 2nd, … entry | `Gemini__ApiKeys__0`, `Gemini__ApiKeys__1`, … |
| `Gemini:GenerationModel` | `Gemini__GenerationModel` |
| `Qdrant:Endpoint` | `Qdrant__Endpoint` (copy exactly, including the port) |
| `Qdrant:ApiKey` | `Qdrant__ApiKey` |
| `Qdrant:Collection` | **do not copy.** Local configs use a personal `act_section_chunks_<name>`. Production must use the canonical `act_section_chunks`. |

> [!CAUTION]
> **`Qdrant__VectorSize` must be `3072`.** On startup,
> `QdrantVectorStore.EnsureCollectionAsync` compares this value with the
> collection. On a mismatch it **deletes and recreates the collection**, which
> wipes all 21k+ embedded vectors. Also leave
> `Gemini__EmbeddingOutputDimensionality` **unset**. And never point a
> developer machine at `act_section_chunks` with a different vector size.

> [!IMPORTANT]
> Keep `Embedding__RunOnStartup=false` in production. The embeddings already
> exist, and the F1 CPU quota would stop a long batch job partway anyway.

Full list:

| Name | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Turns off dev demo seeding and runtime Razor compilation, and turns on HSTS and HTTPS redirection |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | App Service terminates TLS at its front end; this lets the app see the real scheme and client IP (HTTPS redirect, rate limiter per-IP partitions) |
| `ConnectionStrings__DefaultConnection` | connection string from 3.1 | Secret |
| `Gemini__ApiKeys__0` … `__N` | copy from your appsettings | Secret |
| `Gemini__GenerationModel` | copy from your appsettings | Set explicitly: the code default `gemini-2.0-flash` has been retired by Google |
| `Gemini__EmbeddingModel` | `gemini-embedding-001` | Must match the model that produced the vectors |
| `Qdrant__Endpoint` | copy from your appsettings | |
| `Qdrant__ApiKey` | copy from your appsettings | Secret |
| `Qdrant__Collection` | `act_section_chunks` | Canonical collection |
| `Qdrant__VectorSize` | `3072` | See the caution above |
| `Embedding__RunOnStartup` | `false` | |
| `SeedAdmin__Email` | admin email | Used only if that admin does not exist yet. The imported DB already has users. |
| `SeedAdmin__Password` | strong password | Secret. Never leave the bootstrap default. |
| `DataProtection__KeysPath` | `/home/data/keys` | Keeps the key ring outside `/home/site/wwwroot`, which each deploy replaces (section 7) |

Optional: `DevFeatures__EnableConversationMarkdownExport=false` (already the
default).

Saving app settings restarts the app. Set them **before** the first deploy.

---

## 6. Continuous deployment: GitHub Actions

The workflow is [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml).
On every push to `main`, and on demand via *Actions → Deploy → Run workflow*, it:

1. restores LibMan vendor libraries (`wwwroot/lib` is git-ignored),
2. runs the unit tests (a failing test blocks the deploy),
3. runs `dotnet publish -c Release`,
4. copies the committed seed files `data/*.json` into `publish/data/`.
   This step is required: `SeedCategories` reads `categories.json` on every
   startup and would crash the app if the file is missing.
5. zip-deploys the output to App Service.

`ci.yml` is unchanged and stays the PR/build gate.

### One-time repository setup

1. Azure portal → web app → **Overview → Download publish profile**.
   (If the button is disabled: *Configuration → General settings → SCM Basic
   Auth Publishing Credentials → On → Save*, then download.)
2. GitHub → repo → **Settings → Secrets and variables → Actions**:
   - **Secret** `AZURE_WEBAPP_PUBLISH_PROFILE` = the whole XML file content.
   - **Variable** `AZURE_WEBAPP_NAME` = `muktoain-<team>`.

   The workflow skips itself while `AZURE_WEBAPP_NAME` is unset, so it stays
   green before Azure exists.
3. Optional: *Settings → Environments → production* → add required reviewers
   if a human should approve every deploy.

Treat the publish profile like a password. If it leaks, open *Overview →
Reset publish profile* and update the secret.

### Manual deploy (fallback, from a laptop)

```powershell
cd src\MuktoAin.Web; libman restore; cd ..\..
dotnet publish src\MuktoAin.Web\MuktoAin.Web.csproj -c Release -o publish
Remove-Item publish\appsettings.Development.json -ErrorAction SilentlyContinue   # never ship local secrets
New-Item -ItemType Directory -Force publish\data | Out-Null
Copy-Item data\*.json publish\data\
Compress-Archive -Path publish\* -DestinationPath app.zip -Force
az webapp deploy -g muktoain-rg -n muktoain-<team> --src-path app.zip --type zip
```

The `Remove-Item` line matters. A local publish copies your
`appsettings.Development.json` (with real keys) into the output. Production
never reads it, but it should never sit on a server. CI builds never contain it,
because the file is git-ignored.

---

## 7. Data Protection key ring (do not skip)

Field-level PII encryption (`Case.Title`, `Case.Description`, …) uses ASP.NET
Data Protection. Data encrypted with one key ring can **only** be decrypted by
that key ring.

- The imported database contains cases encrypted by **Shads's local** key ring,
  stored in `src/MuktoAin.Web/keys/` on Shads's machine (files named
  `key-<guid>.xml`).
- The app reads the ring from `DataProtection:KeysPath`, set to
  `/home/data/keys` in section 5. `/home` is persistent storage on App Service
  and survives restarts and deploys; `/home/site/wwwroot` is replaced by every
  deploy.

Copy the key files to the server **before** users log in:

1. Shads zips `src/MuktoAin.Web/keys/*.xml` and sends it privately. These
   files are secrets: anyone who has them plus the database can read case data.
2. Upload each file:

   ```powershell
   az webapp deploy -g muktoain-rg -n muktoain-<team> --type static `
     --src-path key-<guid>.xml --target-path /home/data/keys/key-<guid>.xml
   ```

   Or use the browser: `https://muktoain-<team>.scm.azurewebsites.net/newui`
   → *File Manager* → create `/home/data/keys` → upload.
3. Restart the app (*Overview → Restart*).

If this step is skipped, the app still starts. It creates a new ring in
`/home/data/keys`, and every **pre-existing** encrypted case shows as
unreadable. New cases work. To recover, stop the app, replace the generated
files with Shads's, and restart.

Never delete `/home/data/keys` on the server. Losing it makes every case
encrypted on the server permanently unreadable. Download a backup copy after
the first week of real use (same File Manager).

The log line *"No XML encryptor configured. Key may be persisted to storage in
unencrypted form"* is expected on Linux App Service. The ring is protected by
App Service file-system access control.

---

## 8. First deploy and smoke test

1. Make sure sections 3–7 are done (DB imported, settings saved, secret and
   variable set, keys uploaded).
2. GitHub → *Actions → Deploy → Run workflow* on `main`. Wait for green.
3. Open *App Service → Log stream* and load
   `https://muktoain-<team>.azurewebsites.net`. The first load can take
   30–90 s (app cold start + DB resume). Expected log lines:
   - no seeding errors; districts/categories/scenario mappings already present
     are skipped (idempotent),
   - `bangladesh-acts-dataset.json not found` style warning: **expected**. The
     Acts are already in the DB, and the dataset file is intentionally not
     shipped.
   - no `Qdrant collection check failed` warning.

Smoke checklist (tick every row):

| # | Check | Pass criteria |
|---|---|---|
| 1 | Home page | Loads over HTTPS; the disclaimer banner shows (surface 1 of 3) |
| 2 | Admin login | `SeedAdmin` / imported admin logs in; change the password immediately |
| 3 | Chat / RAG | Ask a Bangla labour question; the answer cites real Act sections with matching text and carries the AI disclaimer (surface 2) |
| 4 | Keyword Acts search (FR-7) | Returns results (proves FTS works on Azure SQL) |
| 5 | Existing case | A case created before migration shows a readable title (proves the key ring copy worked) |
| 6 | Draft → lawyer review → approve | Workflow completes; the citizen sees the approved document |
| 7 | PDF export | Bangla renders correctly and the disclaimer is stamped (surface 3) |
| 8 | Real-time updates | Notification/status updates arrive without refresh (SignalR over WebSockets) |
| 9 | Language toggle | Bangla/English switch works |

If check 3 cites wrong or unrelated sections, the database is not a copy of
Shads's. Re-do section 3.2 from the correct source. Do **not** re-embed.

---

## 9. Operations

**Logs.** *App Service → Log stream* (live). Enable *App Service logs →
Application logging (Filesystem)* to keep a short on-disk history.

**Cold starts.** F1 unloads idle apps, and the free DB auto-pauses. Before a
demo or grading session, open the site 2–3 minutes early to warm both.

**Daily CPU quota.** When the 60 CPU-minute F1 quota runs out, the site
returns *403 – This web app is stopped* until the daily reset. Avoid running
the benchmark or any batch work on the hosted app.

**Monthly DB allowance.** *SQL database → Overview* shows the free-offer
usage. With auto-pause chosen, the DB pauses (does not bill) until the month
resets.

**Gemini 429s.** Transient 429s are retried by Polly. Sustained 429s mean the
daily quota is used up. Add keys from another Google project
(`Gemini__ApiKeys__N`) or wait for the reset.

**Rollback.** Re-run the Deploy workflow on an earlier commit
(*Actions → Deploy → Run workflow* → choose the ref). Or revert the commit
on `main`; the push redeploys. Database changes are not rolled back
automatically. Schema scripts are additive, so the previous app version keeps
working.

**Rotating secrets.** Change the value in App settings (the app restarts). For
the publish profile, see section 6.

**Backups.** Azure SQL keeps automatic point-in-time backups (7 days by
default) at no extra cost on the free offer. Back up `/home/data/keys`
yourself (section 7).

---

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| *Application Error* page, log shows `FileNotFoundException … categories.json` | Seed files not deployed | Deploy through `deploy.yml`, or include `data/*.json` in a manual zip (section 6) |
| Startup hangs, then `SqlException` timeout | DB was paused and resuming | Refresh after ~1 min; the EF retry policy usually covers it |
| `Cannot open server … requested by the login. Client with IP … is not allowed` | Firewall | *SQL server → Networking*: allow Azure services (app) or add your IP (SSMS) |
| Chat answers have no citations; log shows `Qdrant collection check failed` | Wrong endpoint/port or API key | Copy `Qdrant:Endpoint` exactly from the working local config; check the key in the Qdrant console |
| Qdrant collection suddenly empty | `Qdrant__VectorSize` ≠ 3072 on some app or dev machine | Fix the value. The vectors must then be re-embedded by the canonical run (Shads), so prevent this in the first place. |
| Old cases show garbled or unreadable titles | Key ring missing or different | Section 8 |
| Redirect loop or wrong `http://` links | Forwarded headers not enabled | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` |
| Unstyled pages (no Bootstrap) | `wwwroot/lib` missing from the package | Build must run `libman restore` (the workflow does) |
| Live updates never arrive | WebSockets off | *Configuration → General settings → Web sockets: On* |
| *403 – This web app is stopped* | F1 daily CPU quota used up | Wait for the daily reset |
| Deploy step fails with 401 | Publish profile stale or basic auth disabled | Re-download the profile (section 6) and update the secret |

---

## 11. Known gaps

- **Single instance only.** The key ring lives on the App Service file share.
  Scaling out needs a shared ring (for example Blob storage), which is outside the
  free tier.
- **No custom domain TLS on F1.** The site runs on `*.azurewebsites.net`.
- **Not rehearsed end to end yet.** The publish and seed-bundling steps were
  verified locally. The Azure portal steps follow current Azure documentation,
  but nobody has run the full sequence against a live subscription yet. Record
  any drift you find here.

---

See also: [deployment-guide.md](deployment-guide.md) (local setup, Docker, CI) ·
[architecture.md](architecture.md) · [../data/README.md](../data/README.md)
(datasets) · [../README.md](../README.md).
