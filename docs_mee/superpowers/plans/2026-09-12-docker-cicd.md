# Multi-Stage Dockerfile & CI/CD Pipeline (S-3.4) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a hardened multi-stage Dockerfile (non-root runtime), a minimal-but-working local e2e docker-compose stack, and extend the existing GitHub Actions CI pipeline with an image-build job — verified by a real `docker build`, a compose smoke test hitting `/Home`, and a validated workflow YAML.

**Architecture:** The repo already contains a partial multi-stage `Dockerfile` (scaffolded during T-1.1, extended in S-3.3), a `.dockerignore`, and `.github/workflows/ci.yml` with build / unit-tests / opt-in integration-tests jobs. This plan AMENDS those in place rather than rewriting: it adds a non-root runtime user to the Dockerfile, fixes the invalid first line in `.dockerignore`, adds `node_modules/` and key-pattern exclusions, adds a `docker-build` job to the existing CI workflow, and introduces `docker-compose.yml` for local end-to-end smoke testing (app + SQL Server + Qdrant). Deployment stays manual (CI-only by design — see Shads_plan Step 3.4 Option A).

**Tech Stack:** Docker multi-stage builds (`mcr.microsoft.com/dotnet/sdk:8.0` → `aspnet:8.0`), LibMan CLI (wwwroot vendor libs are git-ignored), GitHub Actions (ubuntu-latest), docker-compose v2, `mcr.microsoft.com/mssql/server:2022-latest`, `qdrant/qdrant`.

**Spec:** `AGENTS.md` (stack table + disclaimer rules) · `.github/workflows/ci.yml` header comments (eng-review constraints) · `plans/Dependency_plan.md` `[S-3.4]`.

## Global Constraints

- **NO git commit steps.** Per `AGENTS.md` §6, agents must NEVER commit, stage, or push — Shads is the sole committer. All changes stay in the working tree. Each plan below ends with a "Record completion in plans/Dependency_plan.md" step instead of a commit step.
- Amend the EXISTING `Dockerfile`, `.dockerignore`, and `.github/workflows/ci.yml` in place — do not delete or restart the prior scaffolding (it carries eng-review notes that must survive).
- CI remains **CI-only**: no deploy, no registry push, no Azure jobs (S-3.8/S-3.9 handle deployment docs later).
- The CI `integration-tests` job's gating (`vars.ENABLE_INTEGRATION_TESTS`) and its FTS-skip caveat (mssql image lacks Full-Text Search) must be preserved untouched.
- Runtime container must run as **non-root** (`.NET 8` base images ship a pre-created non-root user via `$APP_UID`).
- App startup requires SQL Server (seeders run unconditionally in `Program.cs`), so a **bare** `docker run` of the app cannot serve `/Home` — the runtime smoke test MUST go through docker-compose. The CI job only verifies the image builds and the published output exists (green-by-construction, matching the existing integration-tests philosophy).
- Small seed files under `data/` (`districts.json`, `categories.json`, `scenario-mappings.json`) MUST ship in the image — `SeedDistricts`/`SeedCategories` throw without them. The ~50-100MB `data/bangladesh-acts-dataset.json` stays docker-ignored (`ActImportService` no-ops when absent).
- Windows dev machines have an excluded port range blocking 5080/5082 (see `[ENV-BUG]` note in Dependency_plan S-1.2) — compose maps the app to host port **8080** and SQL to **1434** to avoid the 1433 clash with local SQL Express.

---

### Task 1: Harden the Dockerfile (non-root runtime) & fix `.dockerignore`

**Files:**
- Modify: `Dockerfile`
- Modify: `.dockerignore`

**Interfaces:**
- Produces: image `muktoain` (tag free) with entrypoint `dotnet MuktoAin.Web.dll`, listening on port 8080 as a non-root user. Task 2's compose `build:` and Task 3's CI `docker build` both consume this exact file.

- [ ] **Step 1: Replace `Dockerfile` with the hardened version**

The only changes from the current file: the `final` stage gains `USER $APP_UID` (non-root; the `.NET 8` `aspnet:8.0` image defines `ENV APP_UID=1654` and pre-creates the user) plus an explicit `ASPNETCORE_HTTP_PORTS` pin. Full final content:

```dockerfile
# S-3.4 (Hrittika): multi-stage build -- SDK image compiles & publishes, ASP.NET
# runtime image runs. Amends the original scaffold (which carried the S-3.3
# comment) with a non-root runtime user; nothing else changes.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json .
COPY src/MuktoAin.Domain/MuktoAin.Domain.csproj MuktoAin.Domain/
COPY src/MuktoAin.Application/MuktoAin.Application.csproj MuktoAin.Application/
COPY src/MuktoAin.Infrastructure/MuktoAin.Infrastructure.csproj MuktoAin.Infrastructure/
COPY src/MuktoAin.Web/MuktoAin.Web.csproj MuktoAin.Web/
RUN dotnet restore MuktoAin.Web/MuktoAin.Web.csproj
COPY src/ .
# wwwroot/lib vendor libraries are git-ignored -- restore them via LibMan
# so Bootstrap/jQuery ship inside the published output.
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli
WORKDIR /src/MuktoAin.Web
RUN /root/.dotnet/tools/libman restore
WORKDIR /src
RUN dotnet publish MuktoAin.Web/MuktoAin.Web.csproj -c Release -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
# .NET 8 base images pre-create a non-root user (uid $APP_UID = 1654); port
# 8080 is unprivileged so the app binds fine without root.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "MuktoAin.Web.dll"]
```

- [ ] **Step 2: Fix `.dockerignore`**

The current file's first line is `> .dockerignore`, which is not valid dockerignore syntax (`!` is the exception prefix; `>` matches nothing and is dead weight). Remove it, and add the exclusions required by the spec: `node_modules/` and key/certificate patterns. Full final content:

```
bin/
obj/
.git/
.github/
*.md
plans/
frontend/
frontend.zip
scripts/
tests/
node_modules/
keys/
*.pfx
*.key
docs/
.vs/

# The ~50-100MB Kaggle dataset is git-ignored and optional at runtime
# (ActImportService no-ops when it's absent). Small seed files under data/
# (districts.json, categories.json, scenario-mappings.json) MUST ship in the
# image -- SeedDistricts/SeedCategories throw without them.
data/bangladesh-acts-dataset.json
```

- [ ] **Step 3: Verify the image builds**

Run:
```pwsh
docker build -t muktoain .
```
Expected: build completes with no errors; final layers include `USER $APP_UID`.

- [ ] **Step 4: Verify non-root + published output inside the image**

Run:
```pwsh
docker run --rm --entrypoint /bin/sh muktoain -c "id && test -f /app/MuktoAin.Web.dll && echo OK"
```
Expected output includes `uid=1654` (the non-root `app` user) and prints `OK`.

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Flip the Task 1 deliverable into the `[S-3.4]` line when the whole task list below is done (do it once at the end — see Task 4, Step 5). Do NOT commit; leave all changes in the working tree for Shads.

---

### Task 2: docker-compose.yml — local e2e stack (app + SQL Server + Qdrant)

**Files:**
- Create: `docker-compose.yml`

**Interfaces:**
- Consumes: the `muktoain` image built from Task 1's Dockerfile.
- Produces: `docker compose up -d --build` brings up the app on `http://localhost:8080` against fresh SQL Server (host port **1434**) and Qdrant (host ports 6333/6334). Task 4's smoke test consumes this.

- [ ] **Step 1: Create `docker-compose.yml`**

```yaml
# S-3.4: LOCAL end-to-end smoke stack (app + SQL Server + Qdrant).
# Not a production deployment -- Production config/secrets come with S-3.8.
#
# Usage:
#   1. Apply the schema once against the mapped SQL port (host 1434):
#        pwsh ./scripts/run-all.ps1 -ServerInstance "localhost,1434" -User sa -Password 'YourStrong!Passw0rd'
#   2. docker compose up -d --build
#   3. curl -f http://localhost:8080/Home
services:
  app:
    build: .
    ports:
      - "8080:8080"
    environment:
      # Development keeps demo seeding ON and HTTPS redirect OFF, matching the
      # dev workflow; the app container only listens on plain HTTP anyway.
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__DefaultConnection: "Server=sqlserver;Database=MuktoAin;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True"
      Qdrant__Endpoint: "http://qdrant:6334"
      # Qdrant auth optional for a local container; cloud clusters need ApiKey.
      Embedding__RunOnStartup: "false"
    depends_on:
      sqlserver:
        condition: service_healthy
      qdrant:
        condition: service_started

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStrong!Passw0rd"
      MSSQL_PID: Express
    ports:
      - "1434:1433"
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'YourStrong!Passw0rd' -Q 'SELECT 1' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s

  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"
      - "6334:6334"
```

Notes for the implementer:
- Host port **1434** (not 1433) deliberately avoids the local SQL Express that already listens on 1433 on the dev box.
- `Embedding__RunOnStartup=false` keeps the smoke test from burning Gemini quota.
- `Gemini__ApiKeys__0` is intentionally NOT set here; with no key the app logs warnings but still serves pages (same graceful-degradation the dev box already relies on).
- The mssql image lacks Full-Text Search — FTS-dependent endpoints won't work in this stack (same caveat the CI integration job already documents). The `/Home` smoke test does not exercise FTS.

- [ ] **Step 2: Apply the schema to the compose SQL container**

Run:
```pwsh
pwsh ./scripts/run-all.ps1 -ServerInstance "localhost,1434" -User sa -Password 'YourStrong!Passw0rd'
```
Expected: all `scripts/01-14_*.sql` scripts execute successfully against the `MuktoAin` database.

- [ ] **Step 3: Bring the stack up**

Run:
```pwsh
docker compose up -d --build
docker compose ps
```
Expected: `app`, `sqlserver`, `qdrant` all running; `app` reports `(healthy)`-style status or at minimum `Up`.

- [ ] **Step 4: Smoke test `/Home`**

Run:
```pwsh
curl.exe -f http://localhost:8080/Home
```
Expected: HTTP 200 with HTML containing `MuktoAin`. Then tear down (leave the file in place):
```pwsh
docker compose down
```

---

### Task 3: CI workflow — add the `docker-build` job

**Files:**
- Modify: `.github/workflows/ci.yml` (append one job; change nothing else)

**Interfaces:**
- Consumes: Task 1's Dockerfile (via `docker build .` in the job).
- Produces: a `docker-build` job that runs after `build` on every push/PR to `main`. Existing jobs (`build`, `unit-tests`, `integration-tests`) keep their names, triggers, and gating.

- [ ] **Step 1: Append the job to `ci.yml`**

Add this job as the last entry under `jobs:` (indentation: 2 spaces under `jobs:`). Do NOT modify the existing jobs or the header comments:

```yaml
  docker-build:
    # Verifies the S-3.4 Dockerfile builds and the published output is intact.
    # Runtime smoke testing is deliberately NOT done here: the app needs SQL
    # Server at startup (Program.cs seeders run unconditionally), so a runtime
    # check would require the full compose stack (see the S-3.4 plan, done
    # locally). Keeps CI green-by-construction, same as integration-tests.
    runs-on: ubuntu-latest
    needs: build
    steps:
      - uses: actions/checkout@v4
      - name: Build Docker image
        run: docker build -t muktoain:ci .
      - name: Verify published output in image
        run: docker run --rm --entrypoint /bin/sh muktoain:ci -c "test -f /app/MuktoAin.Web.dll && echo published-output-ok"
```

- [ ] **Step 2: Validate the workflow YAML**

Preferred (actionlint catches job/step schema errors, not just YAML syntax):
```pwsh
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:latest -color
```
Expected: no output, exit code 0.

Fallback if the actionlint image cannot be pulled:
```pwsh
gh workflow view ci.yml --yaml
```
Expected: prints the file without a parse error (note: this validates GitHub's view, weaker than actionlint — prefer actionlint).

- [ ] **Step 3: Lint the compose file too**

```pwsh
docker compose config --quiet
```
Expected: exit code 0, no output.

- [ ] **Step 4: Verify job graph reads correctly**

Run:
```pwsh
gh workflow view ci.yml
```
Expected: job list shows `build`, `unit-tests`, `integration-tests`, `docker-build`.

---

### Task 4: End-to-end verification & plan bookkeeping

**Files:**
- Modify: `plans/Dependency_plan.md` (one line)
- Create (optional, only if a gap was found): fixes to `Dockerfile` / `docker-compose.yml` / `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: the recorded `[S-3.4]` completion state in `plans/Dependency_plan.md`.

- [ ] **Step 1: Full clean rebuild of the image**

Run:
```pwsh
docker build --no-cache -t muktoain .
```
Expected: success (catches any ordering issue between `libman restore` and the publish step that caching could mask).

- [ ] **Step 2: Full compose smoke**

```pwsh
pwsh ./scripts/run-all.ps1 -ServerInstance "localhost,1434" -User sa -Password 'YourStrong!Passw0rd'
docker compose up -d --build
Start-Sleep -Seconds 20
curl.exe -f http://localhost:8080/Home
curl.exe -f http://localhost:8080/Account/Login
docker compose down
```
Expected: both curls return HTTP 200. (`/Account/Login` proves Identity routing + Razor rendering works in the container, not just the static landing page.)

- [ ] **Step 3: Re-validate the workflow**

```pwsh
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:latest -color
```
Expected: clean.

- [ ] **Step 4: Record completion in plans/Dependency_plan.md**

In `plans/Dependency_plan.md`, Checkpoint 3 section 4, change:

```
- [ ] **[S-3.4]** Multi-Stage Dockerfile & GitHub Actions CI/CD Pipeline — *Hrittika* (scaffolded T-1.1)
```

to:

```
- [x] ~~**[S-3.4]** Multi-Stage Dockerfile & GitHub Actions CI/CD Pipeline — *Hrittika* (scaffolded T-1.1)~~ — multi-stage Dockerfile hardened with non-root `$APP_UID` runtime user; `.dockerignore` fixed (invalid `>` line removed, `node_modules/`/key exclusions added); `docker-build` job appended to existing CI (image build + published-output check, runtime smoke kept local via docker-compose); verified with `docker build --no-cache`, compose e2e curl of `/Home` + `/Account/Login` (HTTP 200), and actionlint clean
```

Do NOT commit — Shads commits manually per `AGENTS.md` §6.

- [ ] **Step 5: Report**

Summarize to Shads: image size (`docker images muktoain`), any compose quirks encountered (e.g., SQL cold-start timing), and whether actionlint or the fallback was used.

---

## Self-Review Notes

- Spec coverage: multi-stage build (Task 1), non-root (Task 1), `.dockerignore` with bin/obj/.git/node_modules/keys (Task 1), ci.yml with restore/build/unit/integration + optional docker job (Task 3), docker-compose app+mssql+qdrant (Task 2), verification via `docker build`, YAML validation, `docker run`/compose smoke on `/Home` (Tasks 1, 2, 4).
- Open question for Shads: whether CI should also lint the compose file in CI (Task 3 Step 3 runs it locally only — adding it to CI is a one-liner if desired, left out to keep the job minimal).
