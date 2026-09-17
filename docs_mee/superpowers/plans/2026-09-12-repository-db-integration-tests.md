# Repository & DB Integration Tests (Hrittika) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cover the repository raw-SQL paths that EF InMemory deliberately could not test in T-1.14 (FromSqlRaw/STRING_SPLIT, CONTAINSTABLE full-text search, `ExecuteUpdateAsync`/`ExecuteSqlRawAsync` batch writes) plus real-SQL-Server-only behaviors (unique/filtered-index violations, server defaults, transaction commit/rollback) — against a dedicated `MuktoAin_IntegrationTest` database rebuilt from the hand-authored scripts in `scripts/`, with every test auto-skipping when no SQL Server instance is reachable so CI without a DB still passes.

**Architecture:** A single xUnit `ICollectionFixture` (`SqlDatabaseFixture`) owns the test database's lifecycle: it probes candidate SQL Server instances (LocalDB → SQL Express → env-var override), drops/recreates `MuktoAin_IntegrationTest`, replays `scripts/02_schema.sql`, `08_redesign_tables.sql`, `09_part_b_tables.sql`, `09_fix_chat_session_sessionkey_unique_index.sql` and (best-effort) `03_fulltext.sql` through a small GO-batch splitter, then exposes short-lived `AppDbContext` factories and seed helpers. All test classes join one `[Collection]` so they run serialized against the shared database. A second probe (`FullTextAvailable`) lets `CONTAINSTABLE` tests skip cleanly on SQL Express installs without the Full-Text feature.

**Tech Stack:** .NET 8, xUnit 2.9.3 (+ `Xunit.SkippableFact` for runtime skips), `Microsoft.EntityFrameworkCore.SqlServer` 8.0.11 (transitive via `MuktoAin.Infrastructure`), `Microsoft.Data.SqlClient` (transitive), the repo's existing `AppDbContext` + repositories, idempotent SQL scripts from `scripts/`.

**Spec:** `.agent/spec/requirements.md` (FR-3 SQL-FTS fallback, FR-7 standalone full-text search, evaluation harness note in the NFR section) and `plans/Dependency_plan.md` line 151 — `[T-3.3] Repository & DB Integration Tests — *Hrittika* [Blocked by: T-1.12]`. T-1.14 (`plans/Dependency_plan.md` line 59) explicitly deferred FromSqlRaw/CONTAINSTABLE/ExecuteUpdateAsync coverage to this task; this plan discharges that deferral.

## Global Constraints

- **NEVER commit, stage, push, or amend** (`AGENTS.md` §6 — Shads is the sole committer). Every task below ends with running tests, not a git step. Leave all changes in the working tree.
- **Schema is hand-authored in `scripts/`** — tests must NOT call `EnsureCreated()` or use EF migrations; the schema source of truth is the T-SQL scripts, replayed verbatim by the fixture.
- **Tests must be skippable when the DB is unavailable** — every test method uses `[SkippableFact]` + `Skip.IfNot(_fx.DatabaseAvailable)` (and `Skip.IfNot(_fx.FullTextAvailable)` for CONTAINSTABLE tests) so a CI runner without SQL Server passes with skips, not failures.
- **Match existing integration-test conventions** (`tests/MuktoAin.IntegrationTests/AiPipeline/RagRetrievalSmokeTests.cs`): namespace `MuktoAin.IntegrationTests.<Area>`, plain xUnit facts, no `Microsoft.NET.Test.Sdk` config changes, `<Using Include="Xunit" />` global using already present in the csproj.
- **No schema changes** — this task adds test code and one test-project package reference only. If a test reveals a schema bug, stop and record it; do not edit `scripts/` in this task.
- **Bilingual data is not exercised here** — these are data-layer tests; Bangla literals are fine in seeded text but no localization assertions are in scope.
- **Update `plans/Dependency_plan.md`** — flip `[T-3.3]` to `[x]` wrapped in `~~strikethrough~~` with a summary (repo-mandatory rule, AGENTS.md §5). This is the final step of Task 5, not a per-task step.
- **Database name is dedicated:** `MuktoAin_IntegrationTest` — never touch the dev `MuktoAin` database. Connection target resolution order: env var `MUKTOAIN_TEST_CONNECTION_STRING` → `(localdb)\MSSQLLocalDB` → `.\SQLEXPRESS`.

---

### Task 1: Test-database fixture, script runner, and collection wiring

**Files:**
- Modify: `tests/MuktoAin.IntegrationTests/MuktoAin.IntegrationTests.csproj` (add `Xunit.SkippableFact` package)
- Create: `tests/MuktoAin.IntegrationTests/Support/SqlScriptRunner.cs`
- Create: `tests/MuktoAin.IntegrationTests/Support/SqlDatabaseFixture.cs`
- Create: `tests/MuktoAin.IntegrationTests/Support/SqlDatabaseCollection.cs`
- Test: `tests/MuktoAin.IntegrationTests/Support/SqlDatabaseFixtureSmokeTests.cs`

**Interfaces:**
- Produces (consumed by Tasks 2–4): `MuktoAin.IntegrationTests.Support.SqlDatabaseFixture` with:
  - `bool DatabaseAvailable`, `bool FullTextAvailable`, `string DbConnectionString`
  - `AppDbContext CreateContext()` — fresh `AppDbContext` wired with `UseSqlServer(DbConnectionString)`
  - `Task<(int ActId, int SectionId)> SeedActWithSectionAsync(string sectionText, string? sectionNumber = null)`
  - `Task<int> SeedChunkAsync(int sectionId, string chunkText, string? vectorId = null, string? contentHash = null)`
  - `Task<int> GetDistrictIdAsync()`, `Task<int> GetCategoryIdAsync()`, `Task<int> GetUserIdAsync()` — cached singleton FK parents
  - `Task WaitForFullTextIndexAsync(int timeoutSeconds = 60)` — blocks until the FTS catalog has indexed every `ACT_SECTION` row
  - `Task<object?> ScalarAsync(string sql)`
- Produces: `MuktoAin.IntegrationTests.Support.SqlScriptRunner.SplitIntoBatches(string script)` → `IReadOnlyList<string>` (splits SSMS-style scripts on `GO`, strips `USE` lines).
- Produces: xUnit collection name `"MuktoAinSqlDb"` (all test classes annotate `[Collection("MuktoAinSqlDb")]`).

- [ ] **Step 1: Add the `Xunit.SkippableFact` package to the test project**

Edit `tests/MuktoAin.IntegrationTests/MuktoAin.IntegrationTests.csproj` — add one `PackageReference` to the existing xUnit `ItemGroup`:

```xml
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <PackageReference Include="Xunit.SkippableFact" Version="1.5.25" />
  </ItemGroup>
```

`Xunit.SkippableFact` is the standard xUnit 2.x mechanism for runtime skips (`Skip.IfNot(...)` inside a `[SkippableFact]`); a plain `[Fact]` cannot skip conditionally at runtime.

- [ ] **Step 2: Write the SQL script batch splitter**

The scripts under `scripts/` are authored for SSMS/sqlcmd: statements are separated by `GO` (a client directive, not T-SQL) and switch context with `USE MuktoAin;`. The fixture replays them over one open ADO.NET connection targeting `MuktoAin_IntegrationTest`, so the runner strips `USE` lines (the connection string supplies the catalog) and splits on `GO`. Because every batch executes on the same connection, `SET QUOTED_IDENTIFIER ON` batches in `02_schema.sql` keep their effect.

```csharp
// tests/MuktoAin.IntegrationTests/Support/SqlScriptRunner.cs
namespace MuktoAin.IntegrationTests.Support;

// Splits SSMS-style scripts (scripts/*.sql) into executable T-SQL batches:
// `GO` is a client directive that ADO.NET rejects, so batches are split on it;
// `USE MuktoAin;` lines are removed because the fixture's connection string
// already targets MuktoAin_IntegrationTest. SET-option batches (e.g. SET
// QUOTED_IDENTIFIER ON in 02_schema.sql) survive because all batches run on
// one open connection, and SET options persist per session.
public static class SqlScriptRunner
{
    public static IReadOnlyList<string> SplitIntoBatches(string script)
    {
        var batches = new List<string>();
        var current = new List<string>();

        foreach (var line in script.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase))
            {
                continue; // catalog comes from the connection string, not the script
            }
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                Flush(batches, current);
            }
            else
            {
                current.Add(line);
            }
        }
        Flush(batches, current);
        return batches;
    }

    private static void Flush(List<string> batches, List<string> current)
    {
        var batch = string.Join("\n", current).Trim();
        current.Clear();
        if (batch.Length > 0)
        {
            batches.Add(batch);
        }
    }
}
```

- [ ] **Step 3: Write the `SqlDatabaseFixture`**

Drop/recreate → replay mandatory scripts → best-effort full-text script → expose contexts and seed helpers. `InitializeAsync` sets `DatabaseAvailable` only after a successful probe; any script-execution failure propagates (a reachable DB with a broken script must fail loudly, whereas an unreachable DB skips silently).

```csharp
// tests/MuktoAin.IntegrationTests/Support/SqlDatabaseFixture.cs
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using Xunit.Sdk;

namespace MuktoAin.IntegrationTests.Support;

// Owns the dedicated MuktoAin_IntegrationTest database for the whole test run.
// When no SQL Server instance is reachable, DatabaseAvailable stays false and
// every test Skips itself — CI runners without a database still pass. When a
// server IS reachable, the database is dropped and recreated and the
// hand-authored schema scripts are replayed from scripts/, then shared seed
// helpers (FK parents, acts/sections/chunks) are offered to the test classes.
public class SqlDatabaseFixture : IAsyncLifetime
{
    public const string DatabaseName = "MuktoAin_IntegrationTest";

    // 02 first: the full 14-table schema, already containing the column
    // migrations folded into it by scripts 04-06/10. 08/09_* are additive,
    // idempotent redesign scripts. 03 (full-text) is attempted separately
    // because SQL Express without Advanced Services lacks the feature.
    private static readonly string[] MandatoryScripts =
    {
        "02_schema.sql",
        "08_redesign_tables.sql",
        "09_part_b_tables.sql",
        "09_fix_chat_session_sessionkey_unique_index.sql",
    };
    private const string FullTextScript = "03_fulltext.sql";

    private int? _districtId;
    private int? _categoryId;
    private int? _userId;

    public bool DatabaseAvailable { get; private set; }
    public bool FullTextAvailable { get; private set; }
    public string DbConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var master = await ProbeAsync();
        if (master is null)
        {
            return; // DatabaseAvailable stays false -> all tests skip
        }
        DatabaseAvailable = true;

        var builder = new SqlConnectionStringBuilder(master) { InitialCatalog = DatabaseName };
        DbConnectionString = builder.ConnectionString;

        await RecreateDatabaseAsync(master);

        var scriptsRoot = FindScriptsRoot();
        foreach (var script in MandatoryScripts)
        {
            var text = File.ReadAllText(Path.Combine(scriptsRoot, script));
            await ExecuteBatchesAsync(SqlScriptRunner.SplitIntoBatches(text));
        }

        FullTextAvailable = await TryInstallFullTextAsync(scriptsRoot);
    }

    public Task DisposeAsync() => Task.CompletedTask; // keep DB for post-run inspection

    // ---- connectivity -------------------------------------------------------

    private static async Task<string?> ProbeAsync()
    {
        foreach (var candidate in CandidateConnectionStrings())
        {
            try
            {
                await using var conn = new SqlConnection(candidate);
                await conn.OpenAsync();
                return candidate;
            }
            catch (SqlException)
            {
                // try the next candidate; if all fail the run reports unavailable
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidateConnectionStrings()
    {
        var env = Environment.GetEnvironmentVariable("MUKTOAIN_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
            yield break;
        }
        // Same default instance the app itself uses (src/MuktoAin.Web/appsettings.json),
        // then the SQL Express instance (src/MuktoAin.Web/appsettings.Development.json.template).
        yield return "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";
        yield return "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";
    }

    private static async Task ExecuteAsync(string connectionString, string batch)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = batch;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RecreateDatabaseAsync(string masterConnectionString)
    {
        await ExecuteAsync(masterConnectionString, $@"
IF DB_ID(N'{DatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{DatabaseName}];
END
CREATE DATABASE [{DatabaseName}];");
        // Mirror scripts/01_init_database.sql's isolation settings (fresh DB,
        // so the ROLLBACK IMMEDIATE clauses are no-ops but harmless).
        await ExecuteAsync(masterConnectionString, $@"
ALTER DATABASE [{DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [{DatabaseName}] SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE [{DatabaseName}] SET AUTO_CLOSE OFF;");
    }

    private async Task<bool> TryInstallFullTextAsync(string scriptsRoot)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(scriptsRoot, FullTextScript));
            await ExecuteBatchesAsync(SqlScriptRunner.SplitIntoBatches(text));
            return true;
        }
        catch (SqlException)
        {
            // Instance without "Full-Text and Semantic Extractions for Search"
            // (plain SQL Express). CONTAINSTABLE tests skip on FullTextAvailable.
            return false;
        }
    }

    // ---- contexts & raw execution ------------------------------------------

    public AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(DbConnectionString)
            .Options);

    public async Task ExecuteBatchesAsync(IEnumerable<string> batches)
    {
        await using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();
        foreach (var batch in batches)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    public static string FindScriptsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "scripts", "02_schema.sql")))
        {
            dir = dir.Parent;
        }
        return dir is null
            ? throw new InvalidOperationException(
                $"Could not locate scripts/02_schema.sql walking up from {AppContext.BaseDirectory}")
            : Path.Combine(dir.FullName, "scripts");
    }

    // ---- shared seed helpers ------------------------------------------------

    public async Task<(int ActId, int SectionId)> SeedActWithSectionAsync(
        string sectionText, string? sectionNumber = null)
    {
        await using var ctx = CreateContext();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var act = new Act
        {
            Title = "Test Act " + marker,
            ActNumber = "T-" + marker,
            Year = 2026,
            PublicationDate = "12 September 2026",
            Language = "en",
            SourceUrl = "https://bdlaws.test/" + marker,
        };
        var section = new ActSection
        {
            OrdinalPosition = 1,
            SectionNumber = sectionNumber ?? "1",
            SectionText = sectionText,
        };
        act.Sections.Add(section);
        ctx.Acts.Add(act);
        await ctx.SaveChangesAsync();
        return (act.ActId, section.SectionId);
    }

    public async Task<int> SeedChunkAsync(
        int sectionId, string chunkText, string? vectorId = null, string? contentHash = null)
    {
        await using var ctx = CreateContext();
        var chunk = new ActSectionChunk
        {
            SectionId = sectionId,
            ChunkOrder = 1,
            ChunkText = chunkText,
            TokenCount = chunkText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            VectorId = vectorId,
            ContentHash = contentHash,
        };
        ctx.ActSectionChunks.Add(chunk);
        await ctx.SaveChangesAsync();
        return chunk.ChunkId;
    }

    public async Task<int> GetDistrictIdAsync()
    {
        if (_districtId.HasValue)
        {
            return _districtId.Value;
        }
        await using var ctx = CreateContext();
        var district = new District { DistrictId = 250, Name = "IntegrationTest District" };
        ctx.Districts.Add(district);
        await ctx.SaveChangesAsync();
        _districtId = district.DistrictId;
        return _districtId.Value;
    }

    public async Task<int> GetCategoryIdAsync()
    {
        if (_categoryId.HasValue)
        {
            return _categoryId.Value;
        }
        await using var ctx = CreateContext();
        var category = new CaseCategory
        {
            Name = "IntegrationTest Category",
            Description = "Seeded by T-3.3 integration tests",
        };
        ctx.CaseCategories.Add(category);
        await ctx.SaveChangesAsync();
        _categoryId = category.CategoryId;
        return _categoryId.Value;
    }

    public async Task<int> GetUserIdAsync()
    {
        if (_userId.HasValue)
        {
            return _userId.Value;
        }
        await using var ctx = CreateContext();
        var user = new User
        {
            FullName = "Integration Test Admin",
            Email = $"it-admin-{Guid.NewGuid():N}@test.local",
            Role = UserRole.Admin,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        _userId = user.Id;
        return _userId.Value;
    }

    // ---- full-text readiness ------------------------------------------------

    // CONTAINSTABLE only sees rows after the catalog crawls them (change
    // tracking AUTO picks new rows up asynchronously). Kick a full population
    // (ignore the "already in progress" error) and poll ItemCount until it
    // covers every ACT_SECTION row, so queries are deterministic.
    public async Task WaitForFullTextIndexAsync(int timeoutSeconds = 60)
    {
        try
        {
            await ExecuteAsync(
                DbConnectionString,
                "ALTER FULLTEXT INDEX ON [dbo].[ACT_SECTION] START FULL POPULATION;");
        }
        catch (SqlException)
        {
            // Population may already be running — the polling loop handles it.
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var itemCount = Convert.ToInt32(await ScalarAsync(
                "SELECT ISNULL(FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount'), 0);"));
            var expected = Convert.ToInt32(await ScalarAsync(
                "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));
            if (itemCount >= expected)
            {
                return;
            }
            await Task.Delay(500);
        }
        throw new XunitException(
            $"Full-text index did not finish population within {timeoutSeconds}s " +
            $"(FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount') never reached " +
            $"the ACT_SECTION row count).");
    }
}
```

Note: `MuktoAin.Domain.Entities.User` inherits `IdentityUser<int>`, so the PK property is `Id` (mapped to the physical `UserId` column by `UserConfiguration`) — the seed helper uses `user.Id`.

- [ ] **Step 4: Write the collection definition**

xUnit runs test classes in parallel by default; a shared collection forces serialization so tests never fight over the shared database.

```csharp
// tests/MuktoAin.IntegrationTests/Support/SqlDatabaseCollection.cs
using Xunit;

namespace MuktoAin.IntegrationTests.Support;

// Single shared, serialized database for all T-3.3 test classes.
[CollectionDefinition("MuktoAinSqlDb")]
public class SqlDatabaseCollection : ICollectionFixture<SqlDatabaseFixture>
{
}
```

- [ ] **Step 5: Write the fixture smoke tests**

These prove both halves of the skip contract: with a reachable DB the schema really materialized (≥14 core tables + the 5 redesign tables from `08`); without a DB the fixture reports unavailable instead of throwing.

```csharp
// tests/MuktoAin.IntegrationTests/Support/SqlDatabaseFixtureSmokeTests.cs
namespace MuktoAin.IntegrationTests.Support;

[Collection("MuktoAinSqlDb")]
public class SqlDatabaseFixtureSmokeTests
{
    private readonly SqlDatabaseFixture _fx;

    public SqlDatabaseFixtureSmokeTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Fixture_Materialized_The_Schema_From_The_Sql_Scripts()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var tableCount = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo');"));
        // 14 core tables (scripts/02_schema.sql) + CHAT_SESSION, CHAT_MESSAGE,
        // ANSWER_CACHE, PAYMENT_ORDER, PAYOUT_REQUEST (scripts/08_redesign_tables.sql).
        Assert.True(tableCount >= 19, $"Expected at least 19 dbo tables, found {tableCount}.");
    }

    [SkippableFact]
    public async Task FullText_Availability_Matches_Server_Property()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var isFullTextInstalled = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int);"));
        Assert.Equal(isFullTextInstalled == 1, _fx.FullTextAvailable);
    }
}
```

- [ ] **Step 6: Build and run the smoke tests (DB reachable)**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~SqlDatabaseFixtureSmokeTests" -v minimal`
Expected: PASS (2/2) — the fixture created `MuktoAin_IntegrationTest`, replayed the scripts, and both probes agree. If the machine has LocalDB, this validates the whole setup pipeline on the first run.

- [ ] **Step 7: Verify the skip contract with the DB made unreachable**

Run (PowerShell):
```powershell
$env:MUKTOAIN_TEST_CONNECTION_STRING = "Server=127.0.0.1,1;Database=master;User Id=sa;Password=not-real;Connect Timeout=2"
dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~SqlDatabaseFixtureSmokeTests" -v minimal
Remove-Item Env:MUKTOAIN_TEST_CONNECTION_STRING
```
Expected: PASS (2/2, both reported as skipped) — proves CI without a DB passes. Then re-run the Step 6 command to confirm the DB path works again.

---

### Task 2: `ActSectionRepository` raw-SQL tests (STRING_SPLIT + CONTAINSTABLE)

**Files:**
- Test: `tests/MuktoAin.IntegrationTests/Repositories/ActSectionRepositorySqlTests.cs`

**Interfaces:**
- Consumes: `SqlDatabaseFixture` from Task 1 (`DatabaseAvailable`, `FullTextAvailable`, `CreateContext()`, `SeedActWithSectionAsync`, `WaitForFullTextIndexAsync`, `ScalarAsync`), collection `"MuktoAinSqlDb"`, and `MuktoAin.Infrastructure.Repositories.ActSectionRepository` (`GetBySectionIdsAsync(IEnumerable<int>)`, `FullTextSearchAsync(string query, int maxResults)`) exactly as implemented today — no source changes.

- [ ] **Step 1: Write the test class**

```csharp
// tests/MuktoAin.IntegrationTests/Repositories/ActSectionRepositorySqlTests.cs
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: real-SQL-Server coverage for the two ActSectionRepository raw-SQL
// paths that the EF InMemory provider cannot execute and that the T-1.14
// unit tests deliberately excluded (see the empty
// tests/MuktoAin.UnitTests/Repositories/ActSectionRepositoryTests.cs):
//   - GetBySectionIdsAsync -> FromSqlRaw with STRING_SPLIT
//   - FullTextSearchAsync  -> FromSqlInterpolated with CONTAINSTABLE
[Collection("MuktoAinSqlDb")]
public class ActSectionRepositorySqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public ActSectionRepositorySqlTests(SqlDatabaseFixture fx) => _fx = fx;

    // ---- FromSqlRaw + STRING_SPLIT -----------------------------------------

    [SkippableFact]
    public async Task GetBySectionIdsAsync_Returns_Matching_Sections_With_Act_Loaded()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionA) = await _fx.SeedActWithSectionAsync("First section text.");
        var (_, sectionB) = await _fx.SeedActWithSectionAsync("Second section text.");
        await _fx.SeedActWithSectionAsync("Third section text."); // must NOT come back

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.GetBySectionIdsAsync(new[] { sectionA, sectionB })).ToList();

        Assert.Equal(2, results.Count);
        // The ids travel as ONE string parameter into STRING_SPLIT ({0}), and
        // the Include(s => s.Act) navigation is hydrated by a real join.
        Assert.All(results, s => Assert.NotNull(s.Act));
        Assert.Equal(
            new[] { sectionA, sectionB }.OrderBy(i => i),
            results.Select(s => s.SectionId).OrderBy(i => i));
gment    }

    [SkippableFact]
    public async Task GetBySectionIdsAsync_Returns_Empty_When_No_Id_Matches()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = await repo.GetBySectionIdsAsync(new[] { -1, -2 });

        Assert.Empty(results);
    }

    // ---- FromSqlInterpolated + CONTAINSTABLE (skips without the FTS feature) -

    [SkippableFact]
    public async Task FullTextSearchAsync_Phrase_Query_Finds_Section()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "The wages of every worker shall be paid before the expiry of the seventh working day.");
        await _fx.SeedActWithSectionAsync("Unrelated consumer protection tribunal text.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("\"wages of every worker\"", maxResults: 5)).ToList();

        var match = Assert.Single(results);
        Assert.Contains("wages of every worker", match.SectionText, StringComparison.OrdinalIgnoreCase);
        // The CONTAINSTABLE query joins [dbo].[ACT] so the title is available
        // for FR-7 display even though ACT_SECTION has no ActTitle column.
        Assert.NotNull(match.Act);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Ranks_Term_Repetitions_Higher()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "Wages wages wages: this section repeats wages to rank first for the wages query.");
        await _fx.SeedActWithSectionAsync("A single mention of wages here.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("wages", maxResults: 10)).ToList();

        // CONTAINSTABLE RANK grows with term frequency; the query orders by
        // ft.[RANK] DESC, so the repetition-heavy section must come first.
        Assert.True(results.Count >= 2, $"Expected both wages sections, got {results.Count}.");
        Assert.Contains("repeats", results[0].SectionText, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Apostrophe_Phrase_Does_Not_Throw()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "If a worker's wages are withheld, the worker's remedy is a General Diary.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        // The search condition is a single SQL parameter ({query} in
        // FromSqlInterpolated); an apostrophe inside a quoted phrase must be
        // treated as term content, never as SQL string termination.
        var results = (await repo.FullTextSearchAsync("\"worker's wages\"", maxResults: 5)).ToList();

        Assert.Single(results);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Never_Treats_Query_As_Raw_Sql()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        var before = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        try
        {
            // Malformed search conditions may legitimately be rejected by the
            // FTS parser with a SqlException — what must NEVER happen is the
            // string executing as SQL.
            await repo.FullTextSearchAsync("wages\"; DROP TABLE [dbo].[ACT_SECTION]; --", maxResults: 5);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // FTS parser rejected the value — acceptable.
        }

        var after = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));
        Assert.Equal(before, after); // table survived => parameterization held
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Respects_MaxResults()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        for (var i = 0; i < 3; i++)
        {
            await _fx.SeedActWithSectionAsync($"Fine notice number {i} about unpaid wages.");
        }
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("wages", maxResults: 2)).ToList();

        Assert.Equal(2, results.Count); // TOP({maxResults}) is parameterized, not concatenated
    }
}
```

- [ ] **Step 2: Run the class with the DB reachable**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~ActSectionRepositorySqlTests" -v minimal`
Expected: PASS — 7 tests. On a full-featured instance (LocalDB / SQL Express with Advanced Services) all 7 run; on a plain SQL Express the 5 FTS tests skip and the 2 STRING_SPLIT tests pass.

- [ ] **Step 3: Verify rank determinism is not flaky**

Re-run the same filter 3 times (`dotnet test ... --filter "FullyQualifiedName~FullTextSearchAsync" -v minimal` ×3).
Expected: PASS every time. If `FullTextSearchAsync_Ranks_Term_Repetitions_Higher` flakes (RANK tie-breaking), widen the gap by adding more repetitions to the first seeded section's text and re-verify.

---

### Task 3: `ActSectionChunkRepository` batch-write tests (ExecuteUpdateAsync + ExecuteSqlRawAsync)

**Files:**
- Test: `tests/MuktoAin.IntegrationTests/Repositories/ActSectionChunkRepositorySqlTests.cs`

**Interfaces:**
- Consumes: `SqlDatabaseFixture` from Task 1 (`CreateContext()`, `SeedActWithSectionAsync`, `SeedChunkAsync`), collection `"MuktoAinSqlDb"`, and `MuktoAin.Infrastructure.Repositories.ActSectionChunkRepository` (`UpdateEmbeddingInfoAsync(int chunkId, string vectorId, string contentHash)`, `UpdateBatchEmbeddingInfoAsync(IReadOnlyList<(int chunkId, string vectorId, string contentHash)> updates, CancellationToken ct = default)`, `GetUnembeddedChunksAsync(int batchSize)`, `GetUnembeddedChunksAfterAsync(int afterChunkId, int batchSize, CancellationToken)`, `GetUnembeddedCountAsync()`) exactly as implemented today — no source changes.

- [ ] **Step 1: Write the test class**

The database is shared across the whole run and never truncated between tests, so every assertion filters chunk results down to the ChunkIds the test itself seeded (other classes' leftover rows coexist).

```csharp
// tests/MuktoAin.IntegrationTests/Repositories/ActSectionChunkRepositorySqlTests.cs
using Microsoft.EntityFrameworkCore;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: covers ActSectionChunkRepository's SQL-Server-only write paths that
// the T-1.14 EF InMemory unit tests excluded:
//   - UpdateEmbeddingInfoAsync      -> ExecuteUpdateAsync
//   - UpdateBatchEmbeddingInfoAsync -> ExecuteSqlRawAsync batch UPDATE with
//     ROWLOCK over Chunk(25) windows (the SQL Server branch of the
//     IsSqlServer() guard)
// Read paths (GetUnembedded*) also run here to prove the filtered index
// IX_ACT_SECTION_CHUNK_VectorId_Null behaves as the EmbeddingBatchJob expects.
[Collection("MuktoAinSqlDb")]
public class ActSectionChunkRepositorySqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public ActSectionChunkRepositorySqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task UpdateEmbeddingInfoAsync_Updates_Row_Without_Loading_Entity()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Chunked section for embedding tests.");
        var chunkId = await _fx.SeedChunkAsync(sectionId, "Chunk one text.");

        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx)
                .UpdateEmbeddingInfoAsync(chunkId, "vec-it-001", "hash-abc");
        }

        await using var verify = _fx.CreateContext();
        var chunk = await verify.ActSectionChunks
            .AsNoTracking()
            .SingleAsync(c => c.ChunkId == chunkId);
        Assert.Equal("vec-it-001", chunk.VectorId);
        Assert.Equal("hash-abc", chunk.ContentHash);
        Assert.NotNull(chunk.LastEmbeddedAt); // ExecuteUpdateAsync stamps UtcNow server-side
    }

    [SkippableFact]
    public async Task UpdateEmbeddingInfoAsync_Removes_Chunk_From_Unembedded_Query()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Another chunked section.");
        var embeddedId = await _fx.SeedChunkAsync(sectionId, "Will be embedded.");
        var pendingId = await _fx.SeedChunkAsync(sectionId, "Stays pending.");

        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx)
                .UpdateEmbeddingInfoAsync(embeddedId, "vec-it-002", "hash-def");
        }

        await using var verify = _fx.CreateContext();
        var pending = (await new ActSectionChunkRepository(verify)
            .GetUnembeddedChunksAsync(batchSize: 100)).ToList();
        Assert.Contains(pending, c => c.ChunkId == pendingId);
        Assert.DoesNotContain(pending, c => c.ChunkId == embeddedId);
    }

    [SkippableFact]
    public async Task GetUnembeddedChunksAsync_Returns_Only_Null_VectorId_Rows_In_ChunkId_Order()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for the ordering test.");
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await _fx.SeedChunkAsync(sectionId, $"Chunk {i}."));
        }
        await _fx.SeedChunkAsync(sectionId, "Already embedded.", vectorId: "vec-pre");

        await using var ctx = _fx.CreateContext();
        var results = (await new ActSectionChunkRepository(ctx)
            .GetUnembeddedChunksAsync(batchSize: 100)).ToList();

        // Filter to this test's rows: the shared DB accumulates rows across tests.
        var ours = results.Where(c => ids.Contains(c.ChunkId)).Select(c => c.ChunkId).ToList();
        Assert.Equal(ids.OrderBy(i => i), ours);
        Assert.All(results, c => Assert.Null(c.VectorId));
    }

    [SkippableFact]
    public async Task GetUnembeddedChunksAfterAsync_Uses_Keyset_Pagination()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for keyset pagination.");
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await _fx.SeedChunkAsync(sectionId, $"Keyset chunk {i}."));
        }

        await using var ctx = _fx.CreateContext();
        var page = (await new ActSectionChunkRepository(ctx)
            .GetUnembeddedChunksAfterAsync(afterChunkId: ids[1], batchSize: 100)).ToList();

        Assert.All(page, c => Assert.True(c.ChunkId > ids[1]));
        var ours = page.Where(c => ids.Contains(c.ChunkId)).Select(c => c.ChunkId).ToList();
        Assert.Equal(ids.Skip(2).ToList(), ours); // ids[0..1] excluded by the keyset
    }

    [SkippableFact]
    public async Task UpdateBatchEmbeddingInfoAsync_Applies_All_Updates_Across_Rowlock_Batches()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for the 60-row batch test.");
        var chunkIds = new List<int>();
        for (var i = 0; i < 60; i++)
        {
            chunkIds.Add(await _fx.SeedChunkAsync(sectionId, $"Batch chunk {i}."));
        }

        // 60 updates -> the Chunk(25) loop issues 3 ExecuteSqlRawAsync batches
        // (25 + 25 + 10). Every parameterized UPDATE must land.
        var updates = chunkIds
            .Select((id, i) => (chunkId: id, vectorId: $"vec-batch-{i:D3}", contentHash: $"hash-{i:D3}"))
            .ToList();
        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx).UpdateBatchEmbeddingInfoAsync(updates);
        }

        await using var verify = _fx.CreateContext();
        foreach (var (chunkId, vectorId, contentHash) in updates)
        {
            var chunk = await verify.ActSectionChunks
                .AsNoTracking()
                .SingleAsync(c => c.ChunkId == chunkId);
            Assert.Equal(vectorId, chunk.VectorId);
            Assert.Equal(contentHash, chunk.ContentHash);
            Assert.NotNull(chunk.LastEmbeddedAt);
        }
        var stillPending = await verify.ActSectionChunks
            .CountAsync(c => c.SectionId == sectionId && c.VectorId == null);
        Assert.Equal(0, stillPending);
    }

    [SkippableFact]
    public async Task UpdateBatchEmbeddingInfoAsync_Empty_List_Is_A_NoOp()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();
        // Must not throw and must not open a doomed ExecuteSqlRawAsync with an
        // empty statement string.
        await new ActSectionChunkRepository(ctx).UpdateBatchEmbeddingInfoAsync(new List<(int, string, string)>());
    }
}
```

- [ ] **Step 2: Run the class with the DB reachable**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~ActSectionChunkRepositorySqlTests" -v minimal`
Expected: PASS — 6 tests, including the two `ExecuteUpdateAsync`/`ExecuteSqlRawAsync` paths InMemory could never run.

- [ ] **Step 3: Confirm the InMemory unit tests still exclude these methods without conflicts**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~Repositories" -v minimal`
Expected: PASS — no regressions in the T-1.14 suite; this task adds coverage in the IntegrationTests project only and touches no repository source.

---

### Task 4: Constraint violations, server defaults, and transaction commit/rollback

**Files:**
- Test: `tests/MuktoAin.IntegrationTests/Repositories/RepositoryAndConstraintSqlTests.cs`

**Interfaces:**
- Consumes: `SqlDatabaseFixture` from Task 1 (`DatabaseAvailable`, `CreateContext()`, `GetDistrictIdAsync`, `GetCategoryIdAsync`, `GetUserIdAsync`, `SeedChunkAsync` not needed here), collection `"MuktoAinSqlDb"`. Exercises real-schema constraints: `UQ_USER_Email` and the `IX_USER_NormalizedUserName` filtered unique index (scripts/02), `UQ_CHAT_SESSION_SessionKey` filtered unique index (scripts/08 + 09_fix), and explicit `Database.BeginTransactionAsync` semantics.

- [ ] **Step 1: Write the test class**

```csharp
// tests/MuktoAin.IntegrationTests/Repositories/RepositoryAndConstraintSqlTests.cs
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: behaviors only a real SQL Server can demonstrate and that EF InMemory
// silently accepts (or never models): unique-index violations, filtered
// indexes with NULL semantics, and explicit transaction commit/rollback.
[Collection("MuktoAinSqlDb")]
public class RepositoryAndConstraintSqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public RepositoryAndConstraintSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Duplicate_User_Email_Violates_Unique_Constraint()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var email = $"dup-{Guid.NewGuid():N}@test.local";

        await using var ctx = _fx.CreateContext();
        ctx.Users.Add(new User { FullName = "First", Email = email, Role = UserRole.Citizen });
        await ctx.SaveChangesAsync();

        ctx.Users.Add(new User { FullName = "Second", Email = email, Role = UserRole.Citizen });
        // UQ_USER_Email (scripts/02_schema.sql) must reject the second row.
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Filtered_SessionKey_Index_Allows_Many_Null_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();

        // Logged-in sessions have NULL SessionKey; the filtered index
        // (UQ_CHAT_SESSION_SessionKey ... WHERE SessionKey IS NOT NULL, per
        // scripts/08 + scripts/09_fix) must allow arbitrarily many NULLs.
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session A" });
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session B" });
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session C" });

        await ctx.SaveChangesAsync();

        Assert.All(ctx.ChatSessions.Local, s => Assert.True(s.ChatSessionId > 0));
    }

    [SkippableFact]
    public async Task Filtered_SessionKey_Index_Rejects_Duplicate_Guest_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var key = Guid.NewGuid().ToString("N")[..22]; // matches ChatService's 22-char keys

        await using (var ctx = _fx.CreateContext())
        {
            ctx.ChatSessions.Add(new ChatSession { SessionKey = key, Title = "Seed" });
            await ctx.SaveChangesAsync();
        }

        await using var second = _fx.CreateContext();
        second.ChatSessions.Add(new ChatSession { SessionKey = key, Title = "Collision" });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Case_Insert_Persists_With_All_Required_Foreign_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        await using var ctx = _fx.CreateContext();
        var c = new Case
        {
            CategoryId = categoryId,
            DistrictId = (byte)districtId,
            Title = "FK chain test",
            Description = "Seeded by T-3.3",
            Language = "en",
            Status = CaseStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Cases.Add(c);
        await ctx.SaveChangesAsync();

        // Re-query through a new context to prove the full FK chain
        // (CASE -> DISTRICT, CASE -> CASE_CATEGORY) resolved server-side.
        await using var verify = _fx.CreateContext();
        var stored = await verify.Cases
            .Include(x => x.District)
            .Include(x => x.Category)
            .SingleAsync(x => x.CaseId == c.CaseId);
        Assert.Equal("IntegrationTest District", stored.District.Name);
        Assert.Equal("IntegrationTest Category", stored.Category.Name);
        Assert.Equal(CaseStatus.Submitted, stored.Status);
    }

    [SkippableFact]
    public async Task Transaction_Commit_Persists_Row()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        int caseId;
        await using (var ctx = _fx.CreateContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var c = new Case
            {
                CategoryId = categoryId,
                DistrictId = (byte)districtId,
                Title = "Commit test",
                Description = "d",
                Language = "en",
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.Cases.Add(c);
            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
            caseId = c.CaseId;
        }

        await using var verify = _fx.CreateContext();
        Assert.NotNull(await verify.Cases.FindAsync(caseId));
    }

    [SkippableFact]
    public async Task Transaction_Rollback_Leaves_No_Row()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        int caseId;
        await using (var ctx = _fx.CreateContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var c = new Case
            {
                CategoryId = categoryId,
                DistrictId = (byte)districtId,
                Title = "Rollback test",
                Description = "d",
                Language = "en",
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.Cases.Add(c);
            await ctx.SaveChangesAsync(); // row exists inside the uncommitted tx
            await tx.RollbackAsync();
            caseId = c.CaseId;
        }

        await using var verify = _fx.CreateContext();
        Assert.Null(await verify.Cases.FindAsync(caseId));
    }
}
```

- [ ] **Step 2: Run the class with the DB reachable**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~RepositoryAndConstraintSqlTests" -v minimal`
Expected: PASS — 6 tests. `Duplicate_User_Email_Violates_Unique_Constraint` and `Filtered_SessionKey_Index_Rejects_Duplicate_Guest_Keys` prove the unique/filtered indexes InMemory cannot evaluate actually fire.

- [ ] **Step 3: Verify a genuine violation produces a SQL-level exception (not silent success)**

Temporarily run just one failure-path test with extra verbosity:
`dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~Filtered_SessionKey_Index_Rejects_Duplicate_Guest_Keys" -v normal`
Expected: PASS with a `DbUpdateException` observed inside the test (check the log output shows the assertion passed, i.e. the second `SaveChangesAsync` threw). If it FAILS with "ThrowsAsync failed: no exception", the filtered index from `scripts/09_fix_chat_session_sessionkey_unique_index.sql` did not materialize in the test database — investigate the fixture's script replay before proceeding.

---

### Task 5: Full-suite verification and dependency-plan bookkeeping

**Files:**
- Modify: `plans/Dependency_plan.md:151` (T-3.3 checkbox)

**Interfaces:**
- Consumes: everything from Tasks 1–4. No production code changes anywhere in `src/`.

- [ ] **Step 1: Run the whole IntegrationTests project with the DB reachable**

Run: `dotnet test tests/MuktoAin.IntegrationTests -v minimal`
Expected: PASS — 21 T-3.3 tests (2 smoke + 7 ActSection + 6 chunk + 6 constraint) plus the 2 pre-existing `RagRetrievalSmokeTests` (they don't join the collection and use mocks, so they run even without a DB).

- [ ] **Step 2: Re-run with the DB unreachable to prove the CI-safety exit criterion**

Run (PowerShell):
```powershell
$env:MUKTOAIN_TEST_CONNECTION_STRING = "Server=127.0.0.1,1;Database=master;User Id=sa;Password=not-real;Connect Timeout=2"
dotnet test tests/MuktoAin.IntegrationTests -v minimal
Remove-Item Env:MUKTOAIN_TEST_CONNECTION_STRING
```
Expected: PASS — all T-3.3 tests skipped, `RagRetrievalSmokeTests` still passed. Zero failures.

- [ ] **Step 3: Run the complete solution test suite (no regressions)**

Run: `dotnet test -v minimal`
Expected: PASS — all unit tests (314+) plus integration tests; nothing in `src/` changed, so unit-test counts are unchanged.

- [ ] **Step 4: Record completion in plans/Dependency_plan.md**

Flip line 151 from:

```markdown
- [ ] **[T-3.3]** Repository & DB Integration Tests — *Hrittika* `[Blocked by: T-1.12]`
```

to the completed style used by every other finished entry in that file (checkbox `[x]`, `~~strikethrough~~`, with a completion summary):

```markdown
- [x] ~~**[T-3.3]** Repository & DB Integration Tests — *Hrittika* `[Blocked by: T-1.12]`~~ — xUnit integration suite in `tests/MuktoAin.IntegrationTests/` (21 tests): a `SqlDatabaseFixture` recreates a dedicated `MuktoAin_IntegrationTest` database by replaying `scripts/02+08+09_part_b+09_fix` (plus best-effort `03_fulltext`) through a GO-batch splitter, then covers the raw-SQL paths deferred from T-1.14 — `FromSqlRaw`/STRING_SPLIT section lookups, `CONTAINSTABLE` phrase/ranking/apostrophe/injection-safety behavior, `ExecuteUpdateAsync` and `ExecuteSqlRawAsync` ROWLOCK batch embedding writes — plus unique-index violations (`UQ_USER_Email`, filtered `UQ_CHAT_SESSION_SessionKey`), FK-chain inserts, and transaction commit/rollback. Every test skips automatically when no SQL Server is reachable (env var `MUKTOAIN_TEST_CONNECTION_STRING` → LocalDB → SQLEXPRESS probe), so CI without a DB passes.
```

(Per AGENTS.md §6: do NOT commit — leave all changes in the working tree for Shads.)
