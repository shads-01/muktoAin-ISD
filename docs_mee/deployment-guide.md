# MuktoAin — Deployment Guide

> Covers prerequisites, local setup, Docker, Azure deployment, and secrets
> configuration. For day-to-day local development see the root [README](../README.md).

---

## 1. Prerequisites

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | `8.0.400+` (pinned in `global.json`) | Build & run |
| Docker Desktop | Latest | Container builds (S-3.3) |
| SQL Server 2022 | Express/Dev/Full **with Full-Text Search** | Primary relational DB |
| Qdrant Cloud account | Free 1GB tier | Vector store (RAG) |
| Google AI Studio account | Free tier | Gemini API keys |

> [!WARNING]
> SQL Server **must** include the *Full-Text and Semantic Extractions for Search*
> feature. LocalDB does not support FTS. Verify:
> `SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled');` → must return `1`.

---

## 2. Local Development Setup

1. Clone the repo and check out your feature branch.
2. Restore packages: `dotnet restore src/MuktoAin.Web/MuktoAin.Web.csproj`
   and frontend vendor libs: `cd src/MuktoAin.Web && libman restore`.
3. Copy `appsettings.Development.json.template` → `appsettings.Development.json`
   and fill in: connection string, Gemini API keys, Qdrant endpoint/key,
   seed-admin credentials.
4. Apply the schema (no EF migrations by design): `.\scripts\run-all.ps1`
5. Run: `dotnet run --project src/MuktoAin.Web`

Data seeds automatically on startup (idempotent): districts, categories,
scenario mappings, Acts import *(only if `data/bangladesh-acts-dataset.json`
is present)*, section chunking, and the initial admin user.

### Default admin account

On first startup an admin is seeded if it does not exist:

```json
"SeedAdmin": {
  "Email": "admin@muktoain.bd",
  "Password": "REPLACE_WITH_STRONG_ADMIN_PASSWORD"
}
```

If `SeedAdmin:Password` is not configured, a bootstrap default is used **and a
warning is logged**. Always set this via environment variable in any shared or
production environment: `SeedAdmin__Password`.

---

## 3. Environment Variables / Secrets Configuration

All configuration can be overridden with environment variables using the
double-underscore (`__`) hierarchy separator:

| Setting | Env var | Notes |
|---|---|---|
| Connection string | `ConnectionStrings__DefaultConnection` | Required everywhere |
| Gemini key #N | `Gemini__ApiKeys__0`, `Gemini__ApiKeys__1`, … | One per teammate's Google project |
| Generation model | `Gemini__GenerationModel` | Default `gemini-2.0-flash` |
| Embedding model | `Gemini__EmbeddingModel` | Default `gemini-embedding-001` |
| Retry count | `Gemini__RetryCount` | Polly pipeline (default 3) |
| Circuit breaker threshold | `Gemini__CircuitBreakerFailureThreshold` | Failures before opening |
| Request timeout (s) | `Gemini__RequestTimeoutSeconds` | Per-call bound (default 60) |
| Qdrant endpoint | `Qdrant__Endpoint` | Cloud cluster URL |
| Qdrant API key | `Qdrant__ApiKey` | Read/write key |
| Seed admin email | `SeedAdmin__Email` | First-run only |
| Seed admin password | `SeedAdmin__Password` | **Change from default!** |

**Never commit real secrets.** `appsettings.Development.json` is git-ignored;
in hosted environments use platform secret stores (Azure App Service
Configuration / Key Vault references, GitHub Actions secrets).

### Data Protection keys

Field-level PII encryption uses ASP.NET Data Protection. On a single machine
keys persist automatically. If you ever scale to **multiple instances**, mount a
shared key ring (`PersistKeysToFileSystem` / Azure Blob) — otherwise one instance
cannot decrypt data encrypted by another.

---

## 4. Docker Build and Run

```bash
# Build (multi-stage; LibMan vendor libs are restored inside the build stage)
docker build -t muktoain-web .

# Run against your dev SQL Server + Qdrant Cloud
docker run -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Server=host.docker.internal\SQLEXPRESS;Database=MuktoAin;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true" \
  -e Gemini__ApiKeys__0="your-key" \
  -e Qdrant__Endpoint="https://your-cluster.cloud.qdrant.io:6333" \
  -e Qdrant__ApiKey="your-qdrant-key" \
  -e SeedAdmin__Password="YourStrongPassword!" \
  muktoain-web
```

Notes:

- The container listens on port **8080** (ASP.NET Core 8 default).
- Small seed files (`districts.json`, `categories.json`,
  `scenario-mappings.json`) are baked into the image; the ~50–100MB Acts
  dataset is excluded (`.dockerignore`) and its importer no-ops gracefully.
  To import Acts into a containerized deployment, run the import once against
  a database that already has the data (e.g., your local machine), then point
  the container at that database.
- Windows Trusted_Connection does not work from Linux containers against a
  host SQL Server unless configured carefully — for anything beyond local
  experimentation prefer SQL authentication.

---

## 5. GitHub Actions CI

`.github/workflows/ci.yml` runs on every push/PR to `main`:

1. **build** — restore + compile.
2. **unit-tests** — EF InMemory tests only (no services needed).
3. **integration-tests** — gated behind repository settings so CI stays green
   until you opt in. To enable:
   - Repository → Settings → Secrets and variables → Actions:
     - Variable `ENABLE_INTEGRATION_TESTS` = `true`
     - Secrets: `QDRANT_ENDPOINT`, `QDRANT_API_KEY`, `GEMINI_API_KEY_1`
   - The job spins up a SQL Server 2022 service container, applies
     `scripts/run-all.ps1`, and runs the integration test project.
   - Caveat: the standard `mcr/mssql/server` image lacks Full-Text Search;
     tag FTS tests `[Trait("Category", "RequiresFts")]` so they skip in CI.

Branch protection on `main` should require the **build** and **unit-tests**
checks to pass.

---

## 6. Azure Deployment (Outline)

Target topology when Azure credits are available:

```
Browser ──► Azure App Service (Linux, .NET 8, container deploy)
                │                     │
                ▼                     ▼
        Azure SQL Database      Qdrant Cloud (external SaaS)
        (FTS supported on       (keep free tier or self-host)
         Premium/DTU vCore)
```

1. **Azure SQL**: deploy S0+ tier; ensure the database compatibility level
   supports FTS. Run `scripts/run-all.ps1` against it once
   (`sqlcmd` with `-U`/`-P`), or from SSMS.
2. **App Service**: `az webapp up --runtime "DOTNET:8"` or deploy the Docker
   image to Azure Container Apps / App Service for Containers.
3. **Configuration**: add every setting from §4 as App Service application
   settings (they surface as environment variables). Use Key Vault references
   for the Gemini/Qdrant keys.
4. **TLS**: App Service terminates TLS ≥1.2 by default. Keep
   HTTPS-only enabled and forward the scheme (`UseHttpsRedirection` is already
   wired).
5. **CD (optional)**: extend `ci.yml` with an `azure/webapps-deploy` step gated
   on `main` merges once credits exist (Shads_plan Step 3.4 Option B).
