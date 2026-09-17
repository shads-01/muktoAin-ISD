# Acts Management & Scenario Mapping Services (Hrittika) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the two Checkpoint-3 admin services Hrittika owes Erin's E-3.2 wiring — `ActsManagementService` (T-3.1: admin Act CRUD + SHA-256 content-hash re-indexing) and `ScenarioMappingService` (T-3.2: admin CRUD over the FR-18 scenario keyword→section mappings) — each fully unit-tested and registered in DI.

**Architecture:** Both services are Application-layer classes (`MuktoAin.Application.Services`) behind Domain/Application interfaces, consuming the existing repository seams (`IActRepository`, `IActSectionRepository`, `IActSectionChunkRepository`, `IScenarioMappingRepository`) plus `IVectorStore` — never `AppDbContext` or Infrastructure concretes. Where the existing repositories lack a query the admin surface needs (paged listing, FK-safety counts, chunk-by-act fetch, stale-marking, cascade delete), the repository interfaces are extended first (Task 1). The service DTOs are plain C# records in `MuktoAin.Application.DTOs`, the exact shapes Erin's `/Admin/Acts` and `/Admin/Scenarios` controllers will consume. Re-indexing recomputes SHA-256 over chunk text, compares against the stored `ACT_SECTION_CHUNK.ContentHash`, and resets `VectorId` on mismatches so Shads's `EmbeddingBatchJob` re-embeds them on its next pass — no direct Qdrant/Gemini calls from this service.

**Tech Stack:** .NET 8 / C# 12, ASP.NET Core MVC (services only — no controllers here), EF Core repositories on MSSQL, xUnit + Moq (services) and EF InMemory (repositories), `System.Security.Cryptography.SHA256`.

**Spec:** `.agent/spec/requirements.md` §1 — **FR-17** (Legislative Corpus Ingestion & Sync: "Batch ingestion and incremental re-embedding based on section `ContentHash`" → `ActsManagementService`) and **FR-18** (Admin Management Console: "Comprehensive management of users, lawyer verifications, scenario mappings, and system logs"). Also `.agent/spec/design.md` §3 step 5 (scenario mappings merged into RAG context) and `plans/Dependency_plan.md` lines 133–134.

---

## Global Constraints

- **No git operations.** AGENTS.md §6: agents never commit/stage/push — Shads is the sole committer. Every task ends with a *record-completion* step that edits `plans/Dependency_plan.md`, never a commit step.
- **Clean Architecture:** Application services may only reference Domain interfaces + Application DTOs. No `AppDbContext`, no `QdrantVectorStore`, no `EmbeddingBatchJob`, no `GeminiClient` in Application code. Qdrant access goes through the Domain seam `MuktoAin.Domain.Interfaces.Services.IVectorStore`.
- **SHA-256 format invariant:** any computed hash MUST equal `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()` — byte-identical to `EmbeddingBatchJob.ComputeSha256` (`src/MuktoAin.Infrastructure/VectorStore/EmbeddingBatchJob.cs:718-722`), because these hashes are compared against the `ContentHash` column the batch job wrote.
- **Staleness contract with `EmbeddingBatchJob`:** the job's work query is `WHERE VectorId IS NULL` (`IActSectionChunkRepository.GetUnembeddedChunksAsync`). "Marking stale" therefore means setting `VectorId = NULL` + `ContentHash = <recomputed>` + `LastEmbeddedAt = NULL` — never deleting rows.
- **Schema column limits** (`scripts/02_schema.sql`): `ACT.Title NVARCHAR(500)`, `ACT.ActNumber NVARCHAR(50)`, `ACT.PublicationDate NVARCHAR(100)`, `ACT.Language NVARCHAR(20)`, `ACT.SourceUrl NVARCHAR(1000)`, `SCENARIO_MAPPING.ScenarioKeyword NVARCHAR(200)`, `SCENARIO_MAPPING.Notes NVARCHAR(500)`. Service validation enforces these.
- **Act natural key is (Title, Year)** — the idempotency key `ActImportService` uses (`src/MuktoAin.Infrastructure/Data/Seeding/ActImportService.cs:70-74`). Create/update must preserve uniqueness.
- **All SQL parameterized.** New repo methods use EF LINQ (`EF.Functions.Like`) or `ExecuteSqlInterpolatedAsync` — never string concatenation.
- **`EnableRetryOnFailure` is active** on `AppDbContext` (FIX-DB-1): any explicit transaction must run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`, or EF throws at `BeginTransactionAsync`.
- **Unit-test conventions:** xUnit + Moq; services tested with mocked repositories (`tests/MuktoAin.UnitTests/Services/*`); repositories tested with EF InMemory via `TestDbContextFactory.Create()` (`tests/MuktoAin.UnitTests/Repositories/TestDbContextFactory.cs`). Methods that use `FromSqlRaw`/`ExecuteUpdateAsync`/raw SQL are **not** InMemory-testable — they are implemented but deferred to T-3.3 integration tests (established precedent, T-1.14 note in `plans/Dependency_plan.md`).
- **Repository conventions** (`src/MuktoAin.Infrastructure/Repositories/Repository.cs`): `Update`/`Delete` do NOT save — callers call `SaveChangesAsync()` explicitly; `GetByIdAsync(object id)` takes `object` (boxed int is fine).
- **Test run commands** (from repo root): full suite `dotnet test tests/MuktoAin.UnitTests`; filtered `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~<TestClassName>"`.

---

### Task 1: Repository Extensions (paged acts, FK-safety counts, chunks-by-act, stale marking, cascade delete)

**Files:**
- Modify: `src/MuktoAin.Domain/Interfaces/Repositories/IActRepository.cs`
- Modify: `src/MuktoAin.Infrastructure/Repositories/ActRepository.cs`
- Modify: `src/MuktoAin.Domain/Interfaces/Repositories/IActSectionRepository.cs`
- Modify: `src/MuktoAin.Infrastructure/Repositories/ActSectionRepository.cs`
- Modify: `src/MuktoAin.Domain/Interfaces/Repositories/IActSectionChunkRepository.cs`
- Modify: `src/MuktoAin.Infrastructure/Repositories/ActSectionChunkRepository.cs`
- Test: `tests/MuktoAin.UnitTests/Repositories/ActRepositoryTests.cs` (extend)

**Interfaces:**
- Consumes: existing `Repository<T>` base (`_context`, `_dbSet`), `AppDbContext` DbSets `CaseActReferences`, `ScenarioMappings`.
- Produces (used by Tasks 2–4 via Moq mocks):
  - `IActRepository`: `Task<(IReadOnlyList<Act> Items, int TotalCount)> GetPagedAsync(string? keyword, int page, int pageSize)`; `Task<bool> ExistsByTitleYearAsync(string title, int year, int? excludeActId = null)`; `Task<int> DeleteWithChildrenAsync(int actId)`
  - `IActSectionRepository`: `Task<int> CountCaseReferencesAsync(int actId)`; `Task<int> CountScenarioMappingsAsync(int actId)`
  - `IActSectionChunkRepository`: `Task<IEnumerable<ActSectionChunk>> GetByActIdAsync(int actId)`; `Task MarkStaleAsync(int chunkId, string contentHash)`

- [ ] **Step 1: Write the failing repository tests**

Append to `tests/MuktoAin.UnitTests/Repositories/ActRepositoryTests.cs` (add `using Microsoft.EntityFrameworkCore;` and `using MuktoAin.Infrastructure.Data;` at the top):

```csharp
[Fact]
public async Task GetPagedAsync_ReturnsRequestedPageWithTitleFilter()
{
    using var context = TestDbContextFactory.Create();
    context.Acts.AddRange(
        NewAct("The Labour Act, 2006"),
        NewAct("The Labour Welfare Act, 2010"),
        NewAct("The Customs Act, 1969"));
    await context.SaveChangesAsync();

    var repo = new ActRepository(context);

    var (items, total) = await repo.GetPagedAsync("Labour", page: 1, pageSize: 1);

    Assert.Equal(2, total);
    var act = Assert.Single(items);
    Assert.Contains("Labour", act.Title);
    // Sections are included so the service can compute SectionCount cheaply.
    Assert.NotNull(act.Sections);
}

[Fact]
public async Task GetPagedAsync_SecondPage_SkipsFirstRow()
{
    using var context = TestDbContextFactory.Create();
    context.Acts.AddRange(
        NewAct("The Labour Act, 2006"),
        NewAct("The Labour Welfare Act, 2010"));
    await context.SaveChangesAsync();

    var repo = new ActRepository(context);

    var (items, total) = await repo.GetPagedAsync("Labour", page: 2, pageSize: 1);

    Assert.Equal(2, total);
    var act = Assert.Single(items);
    Assert.Equal("The Labour Welfare Act, 2010", act.Title);
}

[Fact]
public async Task GetPagedAsync_NullKeyword_ReturnsAllActs()
{
    using var context = TestDbContextFactory.Create();
    context.Acts.AddRange(NewAct("Act A"), NewAct("Act B"));
    await context.SaveChangesAsync();

    var repo = new ActRepository(context);

    var (items, total) = await repo.GetPagedAsync(null, page: 1, pageSize: 10);

    Assert.Equal(2, total);
    Assert.Equal(2, items.Count);
}

[Fact]
public async Task ExistsByTitleYearAsync_ReturnsTrueForExistingTitleYearPair()
{
    using var context = TestDbContextFactory.Create();
    context.Acts.Add(NewAct("The Labour Act, 2006"));
    await context.SaveChangesAsync();

    var repo = new ActRepository(context);

    Assert.True(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2006));
    Assert.False(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2007));
}

[Fact]
public async Task ExistsByTitleYearAsync_ExcludeActId_IgnoresThatRow()
{
    using var context = TestDbContextFactory.Create();
    var act = NewAct("The Labour Act, 2006");
    context.Acts.Add(act);
    await context.SaveChangesAsync();

    var repo = new ActRepository(context);

    Assert.False(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, excludeActId: act.ActId));
}
```

Append to `tests/MuktoAin.UnitTests/Repositories/ActSectionRepositoryTests.cs` (add `using MuktoAin.Domain.Entities;` and `using MuktoAin.Infrastructure.Data;` if missing — check the file's existing usings first):

```csharp
[Fact]
public async Task CountScenarioMappingsAsync_CountsMappingsAcrossTheActsSections()
{
    using var context = TestDbContextFactory.Create();
    var act = new Act { Title = "Labour Act", Year = 2006 };
    var s1 = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "wages due" };
    var s2 = new ActSection { Act = act, OrdinalPosition = 2, SectionText = "overtime" };
    context.Acts.Add(act);
    context.ActSections.AddRange(s1, s2);
    context.ScenarioMappings.AddRange(
        new ScenarioMapping { Section = s1, ScenarioKeyword = "বেতন বাকি" },
        new ScenarioMapping { Section = s1, ScenarioKeyword = "wages unpaid" });
    await context.SaveChangesAsync();

    var repo = new ActSectionRepository(context);

    Assert.Equal(2, await repo.CountScenarioMappingsAsync(act.ActId));
    Assert.Equal(0, await repo.CountScenarioMappingsAsync(999));
}

[Fact]
public async Task CountCaseReferencesAsync_CountsCitationsAcrossTheActsSections()
{
    using var context = TestDbContextFactory.Create();
    var act = new Act { Title = "Labour Act", Year = 2006 };
    var section = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "text" };
    var @case = new Case { Title = "t", Description = "d" };
    context.Acts.Add(act);
    context.ActSections.Add(section);
    context.Cases.Add(@case);
    context.CaseActReferences.Add(new CaseActReference { Case = @case, Section = section });
    await context.SaveChangesAsync();

    var repo = new ActSectionRepository(context);

    Assert.Equal(1, await repo.CountCaseReferencesAsync(act.ActId));
    Assert.Equal(0, await repo.CountCaseReferencesAsync(999));
}
```

> Note: the InMemory provider performs relationship fixup from the set navigation properties (`Section = s1`, `Case = @case`), so `m.Section.ActId` and `r.Section.ActId` translate correctly without explicit FK ids.

Append to `tests/MuktoAin.UnitTests/Repositories/ActSectionChunkRepositoryTests.cs` (keep the same usings style as that file):

```csharp
[Fact]
public async Task GetByActIdAsync_ReturnsAllChunksOfTheAct_RegardlessOfEmbedState()
{
    using var context = TestDbContextFactory.Create();
    var act = new Act { Title = "Labour Act", Year = 2006 };
    var section = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "text" };
    context.Acts.Add(act);
    context.ActSections.Add(section);
    context.ActSectionChunks.AddRange(
        new ActSectionChunk { Section = section, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1", LastEmbeddedAt = DateTime.UtcNow },
        new ActSectionChunk { Section = section, ChunkOrder = 2, ChunkText = "b", TokenCount = 1 });
    await context.SaveChangesAsync();

    var repo = new ActSectionChunkRepository(context);

    var chunks = (await repo.GetByActIdAsync(act.ActId)).ToList();

    Assert.Equal(2, chunks.Count);
}
```

(`MarkStaleAsync` and `DeleteWithChildrenAsync` use `ExecuteUpdateAsync`/raw SQL — deliberately NOT tested here; the InMemory provider cannot translate them. They are covered by T-3.3 integration tests, matching the T-1.14 precedent.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActRepositoryTests|FullyQualifiedName~ActSectionRepositoryTests|FullyQualifiedName~ActSectionChunkRepositoryTests"`
Expected: FAIL — compile errors `IActRepository does not contain a definition for 'GetPagedAsync'`, `'CountScenarioMappingsAsync'`, `'CountCaseReferencesAsync'`, `'GetByActIdAsync'` (the new methods do not exist yet).

- [ ] **Step 3: Add the interface methods**

`src/MuktoAin.Domain/Interfaces/Repositories/IActRepository.cs` becomes:

```csharp
using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActRepository : IRepository<Act>
{
    Task<Act?> GetWithSectionsAsync(int actId);
    Task<IEnumerable<Act>> SearchByTitleAsync(string query);

    // T-3.1: admin paging for the /Admin/Acts list. LIKE filter on Title (same
    // rationale as SearchByTitleAsync -- no FTS index exists on ACT.Title), with
    // the total count so the view can render a pager. Page/pageSize are clamped
    // by the service, not here. Sections are Included so the page can show
    // SectionCount without a second query.
    Task<(IReadOnlyList<Act> Items, int TotalCount)> GetPagedAsync(string? keyword, int page, int pageSize);

    // ActImportService's natural key is (Title, Year); create/update must preserve it.
    Task<bool> ExistsByTitleYearAsync(string title, int year, int? excludeActId = null);

    // T-3.1 / FR-17: full cascade delete ACT -> ACT_FOOTNOTE / ACT_SECTION ->
    // ACT_SECTION_CHUNK. The schema FKs are NO ACTION (no ON DELETE CASCADE in
    // scripts/02_schema.sql), so children are deleted explicitly, chunk-first,
    // in one transaction. Returns 1 when the ACT row was deleted, 0 when absent.
    // Raw SQL -- not unit-testable on InMemory; T-3.3 integration coverage.
    Task<int> DeleteWithChildrenAsync(int actId);
}
```

`src/MuktoAin.Domain/Interfaces/Repositories/IActSectionRepository.cs`:

```csharp
using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActSectionRepository : IRepository<ActSection>
{
    Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds);
    Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults);

    // T-3.1 delete guard: these two FKs are NO ACTION in the schema, so any hit
    // must block the act delete before SQL ever throws.
    Task<int> CountCaseReferencesAsync(int actId);
    Task<int> CountScenarioMappingsAsync(int actId);
}
```

`src/MuktoAin.Domain/Interfaces/Repositories/IActSectionChunkRepository.cs` — add inside the interface (keep the existing default-method `UpdateBatchEmbeddingInfoAsync` untouched):

```csharp
    /// <summary>T-3.1: every chunk of an act in any embed state — the re-index
    /// scan and the pre-delete Qdrant vector cleanup both stream from here.</summary>
    Task<IEnumerable<ActSectionChunk>> GetByActIdAsync(int actId);

    /// <summary>T-3.1 staleness stamp: clears the Qdrant pointer so the
    /// EmbeddingBatchJob's "VectorId IS NULL" work query re-embeds the chunk,
    /// and stores the recomputed hash. ExecuteUpdateAsync is not translatable
    /// on InMemory — T-3.3 integration coverage.</summary>
    Task MarkStaleAsync(int chunkId, string contentHash);
```

- [ ] **Step 4: Implement the repository methods**

In `src/MuktoAin.Infrastructure/Repositories/ActRepository.cs` add:

```csharp
    public async Task<(IReadOnlyList<Act> Items, int TotalCount)> GetPagedAsync(string? keyword, int page, int pageSize)
    {
        var query = _dbSet.AsNoTracking();

        // Same EF.Functions.Like rationale as SearchByTitleAsync: parameterized
        // LIKE, never string concatenation.
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Title, pattern));
        }

        var total = await query.CountAsync();
        var items = await query
            .Include(a => a.Sections) // page-sized; lets the service show SectionCount
            .OrderBy(a => a.Title).ThenBy(a => a.Year)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> ExistsByTitleYearAsync(string title, int year, int? excludeActId = null)
        => await _dbSet.AnyAsync(a =>
            a.Title == title
            && a.Year == year
            && (excludeActId == null || a.ActId != excludeActId));

    public async Task<int> DeleteWithChildrenAsync(int actId)
    {
        // Program.cs configures EnableRetryOnFailure (FIX-DB-1) -- an explicit
        // transaction must execute through the execution strategy or EF throws.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE c FROM [dbo].[ACT_SECTION_CHUNK] AS c
                INNER JOIN [dbo].[ACT_SECTION] AS s ON s.[SectionId] = c.[SectionId]
                WHERE s.[ActId] = {actId}");
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT_FOOTNOTE] WHERE [ActId] = {actId}");
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT_SECTION] WHERE [ActId] = {actId}");
            var deleted = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT] WHERE [ActId] = {actId}");

            await transaction.CommitAsync();
            return deleted;
        });
    }
```

In `src/MuktoAin.Infrastructure/Repositories/ActSectionRepository.cs` add:

```csharp
    public async Task<int> CountCaseReferencesAsync(int actId)
        => await _context.CaseActReferences.CountAsync(r => r.Section.ActId == actId);

    public async Task<int> CountScenarioMappingsAsync(int actId)
        => await _context.ScenarioMappings.CountAsync(m => m.Section.ActId == actId);
```

In `src/MuktoAin.Infrastructure/Repositories/ActSectionChunkRepository.cs` add:

```csharp
    public async Task<IEnumerable<ActSectionChunk>> GetByActIdAsync(int actId)
        => await _dbSet
            .AsNoTracking()
            .Where(c => c.Section.ActId == actId)
            .OrderBy(c => c.ChunkId)
            .ToListAsync();

    public async Task MarkStaleAsync(int chunkId, string contentHash)
    {
        // Single round-trip UPDATE (same ExecuteUpdateAsync pattern as
        // UpdateEmbeddingInfoAsync). Nulling VectorId re-enrolls the chunk in the
        // EmbeddingBatchJob's filtered-index work query; LastEmbeddedAt is cleared
        // because the row is no longer represented in Qdrant.
        await _dbSet
            .Where(c => c.ChunkId == chunkId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.VectorId, (string?)null)
                .SetProperty(c => c.ContentHash, contentHash)
                .SetProperty(c => c.LastEmbeddedAt, (DateTime?)null));
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActRepositoryTests|FullyQualifiedName~ActSectionRepositoryTests|FullyQualifiedName~ActSectionChunkRepositoryTests"`
Expected: PASS — all pre-existing tests in those classes plus the 8 new ones. Build clean (the new interface methods are implemented, so nothing else breaks).

---

### Task 2: `ActsManagementService` — DTOs, interface, list & detail

**Files:**
- Create: `src/MuktoAin.Application/DTOs/ActManagementDto.cs`
- Create: `src/MuktoAin.Application/Services/IActsManagementService.cs`
- Create: `src/MuktoAin.Application/Services/ActsManagementService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs` (create)

**Interfaces:**
- Consumes: Task 1's `GetPagedAsync` / `GetByActIdAsync`; existing `IActRepository.GetWithSectionsAsync`.
- Produces (consumed by Tasks 3–4 and by Erin's E-3.2 controller):
  - `IActsManagementService` with `Task<ActPageDto> GetActsAsync(string? keyword, int page, int pageSize)` and `Task<ActDetailDto?> GetActAsync(int actId)` (create/update/delete/reindex methods are added in Tasks 3–4 — declare the full interface now, implement incrementally).
  - DTOs: `ActListDto`, `ActPageDto`, `ActSectionDto`, `ActDetailDto`, `ActSaveDto`, `ActSaveResultDto`, `ActDeleteResultDto`, `ActReindexResultDto` (all in `MuktoAin.Application.DTOs`).

- [ ] **Step 1: Create the DTO file**

`src/MuktoAin.Application/DTOs/ActManagementDto.cs`:

```csharp
namespace MuktoAin.Application.DTOs;

// T-3.1 read/save models for the /Admin/Acts management surface (E-3.2 wiring).
// Column limits mirror scripts/02_schema.sql (ACT table).

public record ActListDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string Language,
    bool IsRepealed,
    int SectionCount,
    string SourceUrl,
    DateTime ImportedAt);

public record ActPageDto(
    IReadOnlyList<ActListDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record ActSectionDto(
    int SectionId,
    int OrdinalPosition,
    string? SectionNumber,
    string? SectionTitle,
    string SectionText,
    int ChunkCount,
    int StaleChunkCount);

public record ActDetailDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string PublicationDate,
    string Language,
    bool IsRepealed,
    int TokenCount,
    string SourceUrl,
    DateTime ImportedAt,
    IReadOnlyList<ActSectionDto> Sections);

public record ActSaveDto(
    string Title,
    string ActNumber,
    int Year,
    string PublicationDate,
    string Language,
    bool IsRepealed,
    string SourceUrl);

public record ActSaveResultDto(bool Success, string? Error, int? ActId)
{
    public static ActSaveResultDto Ok(int actId) => new(true, null, actId);
    public static ActSaveResultDto Fail(string error) => new(false, error, null);
}

public record ActDeleteResultDto(bool Success, string? Error)
{
    public static ActDeleteResultDto Ok() => new(true, null);
    public static ActDeleteResultDto Fail(string error) => new(false, error);
}

// FR-17: result of the SHA-256 content-hash integrity scan for one act.
// Fresh = stored hash matches recomputed; Stale = mismatch (re-embed queued via
// MarkStaleAsync); Pending = never stamped by EmbeddingBatchJob (ContentHash null).
public record ActReindexResultDto(
    int ActId,
    int TotalChunks,
    int FreshChunks,
    int StaleChunks,
    int PendingChunks);
```

- [ ] **Step 2: Create the interface**

`src/MuktoAin.Application/Services/IActsManagementService.cs`:

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.1 / FR-17 + FR-18: admin CRUD over the legislative corpus plus the
// SHA-256 content-hash integrity re-index that drives EmbeddingBatchJob's
// incremental re-embedding. Erin's E-3.2 controller consumes this interface,
// never the repositories directly (Application-layer boundary).
public interface IActsManagementService
{
    Task<ActPageDto> GetActsAsync(string? keyword, int page, int pageSize);
    Task<ActDetailDto?> GetActAsync(int actId);
    Task<ActSaveResultDto> CreateActAsync(ActSaveDto dto);
    Task<ActSaveResultDto> UpdateActAsync(int actId, ActSaveDto dto);
    Task<ActDeleteResultDto> DeleteActAsync(int actId);
    Task<ActReindexResultDto?> ReindexActAsync(int actId);
}
```

- [ ] **Step 3: Write the failing service tests (list & detail)**

Create `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs`:

```csharp
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class ActsManagementServiceTests
{
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly Mock<IActSectionChunkRepository> _chunkRepo = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<ILogger<ActsManagementService>> _logger = new();
    private readonly ActsManagementService _service;

    public ActsManagementServiceTests()
    {
        _service = new ActsManagementService(
            _actRepo.Object,
            _sectionRepo.Object,
            _chunkRepo.Object,
            _vectorStore.Object,
            _logger.Object);
    }

    private static Act NewAct(int id = 1, string title = "The Labour Act, 2006") => new()
    {
        ActId = id,
        Title = title,
        ActNumber = "2006",
        Year = 2006,
        PublicationDate = "01/10/2006",
        Language = "english",
        SourceUrl = "http://example.com/act",
        ImportedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    // ---------- GetActsAsync ----------

    [Fact]
    public async Task GetActsAsync_ClampsPageAndPageSize()
    {
        _actRepo
            .Setup(r => r.GetPagedAsync(null, 1, 100))
            .ReturnsAsync((new List<Act>(), 0));

        var result = await _service.GetActsAsync(keyword: null, page: 0, pageSize: 500);

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
        _actRepo.Verify(r => r.GetPagedAsync(null, 1, 100), Times.Once);
    }

    [Fact]
    public async Task GetActsAsync_TrimsKeywordAndMapsRowsToListDtos()
    {
        var act = NewAct();
        act.Sections.Add(new ActSection { OrdinalPosition = 1, SectionText = "s1" });
        act.Sections.Add(new ActSection { OrdinalPosition = 2, SectionText = "s2" });
        _actRepo
            .Setup(r => r.GetPagedAsync("labour", 1, 20))
            .ReturnsAsync((new List<Act> { act } as IReadOnlyList<Act>, 1));

        var result = await _service.GetActsAsync("  labour  ", 1, 20);

        Assert.Equal(1, result.TotalCount);
        var dto = Assert.Single(result.Items);
        Assert.Equal(act.ActId, dto.ActId);
        Assert.Equal(act.Title, dto.Title);
        Assert.Equal(2, dto.SectionCount);
        Assert.Equal(act.ImportedAt, dto.ImportedAt);
        _actRepo.Verify(r => r.GetPagedAsync("labour", 1, 20), Times.Once);
    }

    // ---------- GetActAsync ----------

    [Fact]
    public async Task GetActAsync_UnknownId_ReturnsNull()
    {
        _actRepo.Setup(r => r.GetWithSectionsAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.GetActAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActAsync_MapsSectionsOrderedByOrdinal_AndCountsStaleChunks()
    {
        var act = NewAct();
        act.Sections.Add(new ActSection
        {
            SectionId = 10,
            ActId = act.ActId,
            OrdinalPosition = 2,
            SectionTitle = "Second",
            SectionText = "second text"
        });
        act.Sections.Add(new ActSection
        {
            SectionId = 11,
            ActId = act.ActId,
            OrdinalPosition = 1,
            SectionTitle = "First",
            SectionText = "first text"
        });
        _actRepo.Setup(r => r.GetWithSectionsAsync(act.ActId)).ReturnsAsync(act);

        var staleChunk = new ActSectionChunk
        {
            ChunkId = 1,
            SectionId = 10,
            ChunkOrder = 1,
            ChunkText = "changed text",
            TokenCount = 2,
            VectorId = "v-old",
            ContentHash = "0000000000000000000000000000000000000000000000000000000000000000"
        };
        var freshChunk = new ActSectionChunk
        {
            ChunkId = 2,
            SectionId = 10,
            ChunkOrder = 2,
            ChunkText = "unchanged text",
            TokenCount = 1,
            VectorId = "v-fresh",
            ContentHash = Sha256("unchanged text"),
            LastEmbeddedAt = DateTime.UtcNow
        };
        var pendingChunk = new ActSectionChunk
        {
            ChunkId = 3,
            SectionId = 10,
            ChunkOrder = 3,
            ChunkText = "never embedded",
            TokenCount = 2
        };
        _chunkRepo
            .Setup(r => r.GetByActIdAsync(act.ActId))
            .ReturnsAsync(new[] { staleChunk, freshChunk, pendingChunk });

        var result = await _service.GetActAsync(act.ActId);

        Assert.NotNull(result);
        // Sections come back in OrdinalPosition order regardless of load order.
        Assert.Equal(new[] { 1, 2 }, result!.Sections.Select(s => s.OrdinalPosition));
        var second = result.Sections.Single(s => s.SectionId == 10);
        Assert.Equal(3, second.ChunkCount);
        Assert.Equal(1, second.StaleChunkCount); // pending is NOT stale; fresh is not stale
        var first = result.Sections.Single(s => s.SectionId == 11);
        Assert.Equal(0, first.ChunkCount);
        Assert.Equal(0, first.StaleChunkCount);
    }

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: FAIL — compile error `The type or namespace name 'ActsManagementService' could not be found`.

- [ ] **Step 5: Implement the service (list & detail members)**

Create `src/MuktoAin.Application/Services/ActsManagementService.cs` with the class skeleton and the two read methods (create/update/delete/reindex bodies arrive in Tasks 3–4 — declare them now so the interface compiles):

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

// T-3.1 / FR-17: admin CRUD over the legislative corpus plus the SHA-256
// content-hash integrity re-index (FR-17's "incremental re-embedding based on
// section ContentHash"). The service never touches Qdrant or EmbeddingBatchJob
// directly: marking a chunk stale (VectorId = NULL) enrolls it in the
// EmbeddingBatchJob's existing work query on its next loop pass.
public class ActsManagementService : IActsManagementService
{
    private readonly IActRepository _actRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActSectionChunkRepository _chunkRepo;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ActsManagementService> _logger;

    private const int MaxPageSize = 100;

    public ActsManagementService(
        IActRepository actRepo,
        IActSectionRepository sectionRepo,
        IActSectionChunkRepository chunkRepo,
        IVectorStore vectorStore,
        ILogger<ActsManagementService> logger)
    {
        _actRepo = actRepo;
        _sectionRepo = sectionRepo;
        _chunkRepo = chunkRepo;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<ActPageDto> GetActsAsync(string? keyword, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (acts, total) = await _actRepo.GetPagedAsync(keyword, page, pageSize);

        return new ActPageDto(
            acts.Select(a => new ActListDto(
                a.ActId,
                a.Title,
                a.ActNumber,
                a.Year,
                a.Language,
                a.IsRepealed,
                a.Sections.Count,
                a.SourceUrl,
                a.ImportedAt)).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<ActDetailDto?> GetActAsync(int actId)
    {
        var act = await _actRepo.GetWithSectionsAsync(actId);
        if (act is null) return null;

        var chunks = (await _chunkRepo.GetByActIdAsync(actId)).ToList();
        var chunksBySection = chunks
            .GroupBy(c => c.SectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sections = act.Sections
            .OrderBy(s => s.OrdinalPosition)
            .Select(s =>
            {
                var sectionChunks = chunksBySection.GetValueOrDefault(s.SectionId, []);
                return new ActSectionDto(
                    s.SectionId,
                    s.OrdinalPosition,
                    s.SectionNumber,
                    s.SectionTitle,
                    s.SectionText,
                    sectionChunks.Count,
                    sectionChunks.Count(c => c.ContentHash is not null
                        && !string.Equals(c.ContentHash, ComputeSha256(c.ChunkText), StringComparison.Ordinal)));
            })
            .ToList();

        return new ActDetailDto(
            act.ActId,
            act.Title,
            act.ActNumber,
            act.Year,
            act.PublicationDate,
            act.Language,
            act.IsRepealed,
            act.TokenCount,
            act.SourceUrl,
            act.ImportedAt,
            sections);
    }

    public Task<ActSaveResultDto> CreateActAsync(ActSaveDto dto)
    {
        throw new NotImplementedException(); // Task 3
    }

    public Task<ActSaveResultDto> UpdateActAsync(int actId, ActSaveDto dto)
    {
        throw new NotImplementedException(); // Task 3
    }

    public Task<ActDeleteResultDto> DeleteActAsync(int actId)
    {
        throw new NotImplementedException(); // Task 3
    }

    public Task<ActReindexResultDto?> ReindexActAsync(int actId)
    {
        throw new NotImplementedException(); // Task 4
    }

    // MUST match EmbeddingBatchJob.ComputeSha256 (Infrastructure/VectorStore/
    // EmbeddingBatchJob.cs) exactly -- lowercase hex of the UTF-8 bytes -- because
    // these hashes are compared against the ContentHash column the job wrote.
    private static string ComputeSha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
```

> `Microsoft.Extensions.Logging` resolves via the `Microsoft.Extensions.Logging.Abstractions` reference the Application project already carries (see `RagContextBuilder.cs`).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: PASS — the 4 list/detail tests green (the create/update/delete/reindex tests don't exist yet).

---

### Task 3: `ActsManagementService` — create, update, delete (FK guards + best-effort Qdrant cleanup)

**Files:**
- Modify: `src/MuktoAin.Application/Services/ActsManagementService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs` (append)

**Interfaces:**
- Consumes: `IActRepository.GetByIdAsync/AddAsync/UpdateAsync/SaveChangesAsync/ExistsByTitleYearAsync/DeleteWithChildrenAsync`, `IActSectionRepository.CountCaseReferencesAsync/CountScenarioMappingsAsync`, `IActSectionChunkRepository.GetByActIdAsync`, `IVectorStore.DeleteAsync(string vectorId)`.
- Produces: working `CreateActAsync`/`UpdateActAsync` (returns `ActSaveResultDto`) and `DeleteActAsync` (returns `ActDeleteResultDto`) per the Task 2 interface.

- [ ] **Step 1: Write the failing tests (create/update/delete)**

Append to `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs`:

```csharp
    // ---------- CreateActAsync ----------

    [Fact]
    public async Task CreateActAsync_BlankTitle_FailsWithoutTouchingRepositories()
    {
        var result = await _service.CreateActAsync(new ActSaveDto("   ", "1", 2006, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Equal("Title is required.", result.Error);
        _actRepo.Verify(r => r.AddAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task CreateActAsync_YearOutOfRange_Fails()
    {
        var result = await _service.CreateActAsync(new ActSaveDto("Some Act", "", 1500, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Equal("Year must be between 1600 and 2100.", result.Error);
    }

    [Fact]
    public async Task CreateActAsync_DuplicateTitleYear_Fails()
    {
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, null))
            .ReturnsAsync(true);

        var result = await _service.CreateActAsync(
            new ActSaveDto("The Labour Act, 2006", "", 2006, "", "english", false, ""));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _actRepo.Verify(r => r.AddAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task CreateActAsync_Valid_AddsActWithTrimmedFieldsAndReturnsId()
    {
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, null))
            .ReturnsAsync(false);
        _actRepo
            .Setup(r => r.AddAsync(It.IsAny<Act>()))
            .Callback<Act>(a => a.ActId = 42)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateActAsync(
            new ActSaveDto("  The Labour Act, 2006  ", " 2006 ", 2006, " 01/10/2006 ", "  english  ", false, " http://example.com "));

        Assert.True(result.Success);
        Assert.Equal(42, result.ActId);
        _actRepo.Verify(r => r.AddAsync(It.Is<Act>(a =>
            a.Title == "The Labour Act, 2006"
            && a.ActNumber == "2006"
            && a.Year == 2006
            && a.Language == "english"
            && a.TokenCount == 0)), Times.Once);
        _actRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ---------- UpdateActAsync ----------

    [Fact]
    public async Task UpdateActAsync_UnknownId_Fails()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.UpdateActAsync(999, new ActSaveDto("t", "", 2006, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task UpdateActAsync_TitleYearTakenByAnotherAct_Fails()
    {
        var act = NewAct(id: 1);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("Other Act", 1999, 1))
            .ReturnsAsync(true);

        var result = await _service.UpdateActAsync(1, new ActSaveDto("Other Act", "", 1999, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Contains("Another act", result.Error);
        _actRepo.Verify(r => r.UpdateAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActAsync_Valid_UpdatesMetadataOnly()
    {
        var act = NewAct(id: 1);
        act.Sections.Add(new ActSection { OrdinalPosition = 1, SectionText = "existing text" });
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour (Amended) Act, 2006", 2006, 1))
            .ReturnsAsync(false);

        var result = await _service.UpdateActAsync(1, new ActSaveDto(
            "The Labour (Amended) Act, 2006", "2006-A", 2006, "05/10/2006", "english", IsRepealed: true, "http://example.com/amended"));

        Assert.True(result.Success);
        Assert.Equal(1, result.ActId);
        Assert.Equal("The Labour (Amended) Act, 2006", act.Title);
        Assert.Equal("2006-A", act.ActNumber);
        Assert.True(act.IsRepealed);
        // Metadata-only: sections untouched, so existing chunk hashes stay valid.
        Assert.Empty(act.Sections.Single().Chunks);
        _actRepo.Verify(r => r.UpdateAsync(act), Times.Once);
        _actRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ---------- DeleteActAsync ----------

    [Fact]
    public async Task DeleteActAsync_UnknownId_Fails()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.DeleteActAsync(999);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_BlockedWhenCaseCitationsReferenceTheAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(3);

        var result = await _service.DeleteActAsync(1);

        Assert.False(result.Success);
        Assert.Contains("3 case citation", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_BlockedWhenScenarioMappingsReferenceTheAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(2);

        var result = await _service.DeleteActAsync(1);

        Assert.False(result.Success);
        Assert.Contains("2 scenario mapping", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_Clean_DeletesQdrantVectorsThenCascadeDeletes()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(0);
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1" },
            new ActSectionChunk { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "b", TokenCount = 1 }, // never embedded
            new ActSectionChunk { ChunkId = 3, SectionId = 10, ChunkOrder = 3, ChunkText = "c", TokenCount = 1, VectorId = "v-2", ContentHash = "h2" },
        });

        var result = await _service.DeleteActAsync(1);

        Assert.True(result.Success);
        _vectorStore.Verify(v => v.DeleteAsync("v-1"), Times.Once);
        _vectorStore.Verify(v => v.DeleteAsync("v-2"), Times.Once);
        _vectorStore.Verify(v => v.DeleteAsync(It.IsAny<string>()), Times.Exactly(2));
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(1), Times.Once);
    }

    [Fact]
    public async Task DeleteActAsync_QdrantOutage_StillDeletesAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(0);
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1" },
        });
        _vectorStore
            .Setup(v => v.DeleteAsync("v-1"))
            .ThrowsAsync(new HttpRequestException("qdrant down"));

        var result = await _service.DeleteActAsync(1);

        // Orphaned points are recoverable (FIX-QDRANT-1 reconciliation); a wedged
        // delete is not. SQL deletion must win.
        Assert.True(result.Success);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(1), Times.Once);
    }
```

> `HttpRequestException` (used by the Qdrant-outage test) resolves via the `using System.Net.Http;` directive that Task 2 Step 3 already placed at the top of the test file.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: FAIL — `NotImplementedException` thrown from `CreateActAsync`/`UpdateActAsync`/`DeleteActAsync` (12 new tests failing, earlier tests still passing).

- [ ] **Step 3: Implement create/update/delete**

Replace the three `NotImplementedException` bodies in `src/MuktoAin.Application/Services/ActsManagementService.cs`:

```csharp
    public async Task<ActSaveResultDto> CreateActAsync(ActSaveDto dto)
    {
        var validationError = Validate(dto);
        if (validationError is not null) return ActSaveResultDto.Fail(validationError);

        var title = dto.Title.Trim();
        if (await _actRepo.ExistsByTitleYearAsync(title, dto.Year))
        {
            return ActSaveResultDto.Fail(
                $"An act titled '{title}' already exists for {dto.Year} (the (Title, Year) natural key is shared with the import pipeline).");
        }

        var act = new Act
        {
            Title = title,
            ActNumber = dto.ActNumber.Trim(),
            Year = dto.Year,
            PublicationDate = dto.PublicationDate.Trim(),
            Language = string.IsNullOrWhiteSpace(dto.Language) ? "unknown" : dto.Language.Trim(),
            IsRepealed = dto.IsRepealed,
            // Metadata-only row: sections arrive via the T-1.8 import pipeline, so
            // TokenCount starts at 0 like a freshly-registered act shell.
            TokenCount = 0,
            SourceUrl = dto.SourceUrl.Trim(),
            ImportedAt = DateTime.UtcNow
        };

        await _actRepo.AddAsync(act);
        await _actRepo.SaveChangesAsync();
        return ActSaveResultDto.Ok(act.ActId);
    }

    public async Task<ActSaveResultDto> UpdateActAsync(int actId, ActSaveDto dto)
    {
        var validationError = Validate(dto);
        if (validationError is not null) return ActSaveResultDto.Fail(validationError);

        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null)
        {
            return ActSaveResultDto.Fail($"Act {actId} was not found.");
        }

        var title = dto.Title.Trim();
        if (await _actRepo.ExistsByTitleYearAsync(title, dto.Year, excludeActId: actId))
        {
            return ActSaveResultDto.Fail($"Another act titled '{title}' already exists for {dto.Year}.");
        }

        // Metadata only. Sections/footnotes/chunks are untouched: untouched chunk
        // text means every stored ContentHash stays valid, so no re-index needed.
        act.Title = title;
        act.ActNumber = dto.ActNumber.Trim();
        act.Year = dto.Year;
        act.PublicationDate = dto.PublicationDate.Trim();
        act.Language = string.IsNullOrWhiteSpace(dto.Language) ? act.Language : dto.Language.Trim();
        act.IsRepealed = dto.IsRepealed;
        act.SourceUrl = dto.SourceUrl.Trim();

        await _actRepo.UpdateAsync(act);
        await _actRepo.SaveChangesAsync();
        return ActSaveResultDto.Ok(act.ActId);
    }

    public async Task<ActDeleteResultDto> DeleteActAsync(int actId)
    {
        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null)
        {
            return ActDeleteResultDto.Fail($"Act {actId} was not found.");
        }

        // FK-safety guards BEFORE any SQL runs -- both FKs are NO ACTION in
        // scripts/02_schema.sql, so letting SQL throw would surface a 500.
        var caseReferences = await _sectionRepo.CountCaseReferencesAsync(actId);
        if (caseReferences > 0)
        {
            return ActDeleteResultDto.Fail(
                $"Cannot delete: {caseReferences} case citation(s) in CASE_ACT_REFERENCE reference this act's sections.");
        }

        var mappings = await _sectionRepo.CountScenarioMappingsAsync(actId);
        if (mappings > 0)
        {
            return ActDeleteResultDto.Fail(
                $"Cannot delete: {mappings} scenario mapping(s) point at this act's sections. Remove them on the Scenarios page first.");
        }

        // Best-effort Qdrant cleanup BEFORE the SQL cascade: orphaned vector
        // points are recoverable via reconciliation (FIX-QDRANT-1), but a Qdrant
        // outage must never wedge the admin delete.
        var chunks = await _chunkRepo.GetByActIdAsync(actId);
        foreach (var vectorId in chunks
                     .Where(c => c.VectorId is not null)
                     .Select(c => c.VectorId!))
        {
            try
            {
                await _vectorStore.DeleteAsync(vectorId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "DeleteActAsync: Qdrant delete failed for {VectorId} (orphaned point is reconcilable): {Message}",
                    vectorId, ex.Message);
            }
        }

        await _actRepo.DeleteWithChildrenAsync(actId);
        return ActDeleteResultDto.Ok();
    }

    private static string? Validate(ActSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return "Title is required.";
        if (dto.Title.Trim().Length > 500) return "Title must be 500 characters or fewer (ACT.Title NVARCHAR(500)).";
        if (dto.ActNumber.Trim().Length > 50) return "Act number must be 50 characters or fewer (ACT.ActNumber NVARCHAR(50)).";
        if (dto.Year is < 1600 or > 2100) return "Year must be between 1600 and 2100.";
        if (dto.PublicationDate.Trim().Length > 100) return "Publication date must be 100 characters or fewer.";
        if (dto.Language.Trim().Length > 20) return "Language must be 20 characters or fewer.";
        if (dto.SourceUrl.Trim().Length > 1000) return "Source URL must be 1000 characters or fewer.";
        return null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: PASS — all 16 tests in the class green.

---

### Task 4: `ActsManagementService` — SHA-256 re-index + DI registration (T-3.1 complete)

**Files:**
- Modify: `src/MuktoAin.Application/Services/ActsManagementService.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (insert near line 197, after the `IUserManagementService` registration)
- Test: `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs` (append)

**Interfaces:**
- Consumes: `IActSectionChunkRepository.GetByActIdAsync/MarkStaleAsync` (Task 1), `IActRepository.GetByIdAsync`.
- Produces: `Task<ActReindexResultDto?> ReindexActAsync(int actId)` (null when the act doesn't exist) — consumed by E-3.2's "Re-index" button; DI registration `IActsManagementService → ActsManagementService`.

- [ ] **Step 1: Write the failing re-index tests**

Append to `tests/MuktoAin.UnitTests/Services/ActsManagementServiceTests.cs`:

```csharp
    // ---------- ReindexActAsync ----------

    [Fact]
    public async Task ReindexActAsync_UnknownId_ReturnsNull()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.ReindexActAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReindexActAsync_ClassifiesFreshStalePending_AndStampsStaleChunks()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            // Fresh: stored hash matches the current chunk text.
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "unchanged", TokenCount = 1, VectorId = "v-1", ContentHash = Sha256("unchanged") },
            // Stale: text was edited after the last embed -- stored hash no longer matches.
            new ActSectionChunk { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "edited", TokenCount = 1, VectorId = "v-2", ContentHash = "stale-hash-from-last-embed" },
            // Pending: never stamped by EmbeddingBatchJob (its own work query will pick it up).
            new ActSectionChunk { ChunkId = 3, SectionId = 10, ChunkOrder = 3, ChunkText = "never embedded", TokenCount = 2 },
        });

        var result = await _service.ReindexActAsync(1);

        Assert.NotNull(result);
        Assert.Equal(1, result!.ActId);
        Assert.Equal(3, result.TotalChunks);
        Assert.Equal(1, result.FreshChunks);
        Assert.Equal(1, result.StaleChunks);
        Assert.Equal(1, result.PendingChunks);

        // Only the stale chunk is re-enrolled: VectorId nulled (so the job's
        // "VectorId IS NULL" query re-embeds it) and the recomputed hash stored.
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(2, Sha256("edited")),
            Times.Once);
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(It.IsAny<int>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ReindexActAsync_StoresHashInEmbeddingBatchJobFormat_LowercaseHex()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "বাংলা টেক্সট", TokenCount = 3, VectorId = "v-1", ContentHash = "stale" },
        });

        await _service.ReindexActAsync(1);

        // Same expression as EmbeddingBatchJob.ComputeSha256 -- lowercase hex of
        // the UTF-8 bytes -- so the stored hash stays comparable across systems.
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(1, Sha256("বাংলা টেক্সট")),
            Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: FAIL — `ReindexActAsync` throws `NotImplementedException` (2 new tests failing).

- [ ] **Step 3: Implement `ReindexActAsync`**

Replace the `NotImplementedException` body in `src/MuktoAin.Application/Services/ActsManagementService.cs`:

```csharp
    public async Task<ActReindexResultDto?> ReindexActAsync(int actId)
    {
        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null) return null;

        int fresh = 0, stale = 0, pending = 0;
        foreach (var chunk in await _chunkRepo.GetByActIdAsync(actId))
        {
            // ContentHash is null only for rows EmbeddingBatchJob has not stamped
            // yet -- those are already enrolled in the job's "VectorId IS NULL"
            // work query, so there is nothing to re-enroll here.
            if (chunk.ContentHash is null)
            {
                pending++;
                continue;
            }

            var hash = ComputeSha256(chunk.ChunkText);
            if (string.Equals(chunk.ContentHash, hash, StringComparison.Ordinal))
            {
                fresh++;
                continue;
            }

            // Text changed since the last embed. Nulling VectorId re-enrolls the
            // chunk in EmbeddingBatchJob's next pass; storing the recomputed hash
            // keeps the dedupe scan's "hash matches, skip" check correct.
            await _chunkRepo.MarkStaleAsync(chunk.ChunkId, hash);
            stale++;
        }

        return new ActReindexResultDto(actId, fresh + stale + pending, fresh, stale, pending);
    }
```

- [ ] **Step 4: Register in DI**

In `src/MuktoAin.Web/Program.cs`, directly after the line `builder.Services.AddScoped<IUserManagementService, UserManagementService>();` (~line 197), add:

```csharp
    // T-3.1: admin Acts CRUD + SHA-256 content-hash re-indexing (FR-17, wires to E-3.2).
    builder.Services.AddScoped<IActsManagementService, ActsManagementService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ActsManagementServiceTests"`
Expected: PASS — all 17 tests in the class green.

- [ ] **Step 6: Verify the whole solution builds**

Run: `dotnet build MuktoAin.sln`
Expected: Build succeeded, 0 errors (confirms the new DI registration resolves and Application/Domain boundaries hold).

---

### Task 5: `ScenarioMappingService` — DTOs, interface, list & detail

**Files:**
- Create: `src/MuktoAin.Application/DTOs/ScenarioMappingDto.cs`
- Create: `src/MuktoAin.Application/Services/IScenarioMappingService.cs`
- Create: `src/MuktoAin.Application/Services/ScenarioMappingService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/ScenarioMappingServiceTests.cs` (create)

**Interfaces:**
- Consumes: `IScenarioMappingRepository` (incl. `GetAllAsync`, `SearchByKeywordAsync`, generic CRUD), `IActSectionRepository.GetByIdAsync/GetBySectionIdsAsync`, `IActRepository.GetByIdAsync/GetAllAsync`.
- Produces (consumed by Task 6 and Erin's E-3.2 controller):
  - `IScenarioMappingService`: `Task<IReadOnlyList<ScenarioMappingDto>> GetAllAsync()`; `Task<ScenarioMappingDto?> GetByIdAsync(int mappingId)`; `Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto)`; `Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto)`; `Task<bool> DeleteAsync(int mappingId)`.
  - DTOs: `ScenarioMappingDto`, `ScenarioMappingSaveDto`, `ScenarioMappingSaveResult`.

- [ ] **Step 1: Create the DTO file**

`src/MuktoAin.Application/DTOs/ScenarioMappingDto.cs`:

```csharp
namespace MuktoAin.Application.DTOs;

// T-3.2 read/save models for the /Admin/Scenarios keyword-boost console (E-3.2).
// The SCENARIO_MAPPING schema (scripts/02_schema.sql) is keyword -> section:
// ScenarioKeyword NVARCHAR(200), Notes NVARCHAR(500), SectionId FK. There are no
// category/boost columns -- the FR-18 "boost" is realized by RagContextBuilder,
// which merges every mapped section whose ScenarioKeyword appears in the citizen
// query into the retrieved context at 1.0f curated-prior relevance
// (src/MuktoAin.Application/Services/RagContextBuilder.cs, MergeScenarioPriorsAsync).
// Choosing a section via this admin surface IS the section hint; the keyword IS
// the boost trigger.

public record ScenarioMappingDto(
    int MappingId,
    int SectionId,
    string ActTitle,
    string? SectionNumber,
    string ScenarioKeyword,
    string? Notes);

public record ScenarioMappingSaveDto(
    int? MappingId,
    int SectionId,
    string ScenarioKeyword,
    string? Notes);

public record ScenarioMappingSaveResult(bool Success, string? Error, ScenarioMappingDto? Mapping)
{
    public static ScenarioMappingSaveResult Ok(ScenarioMappingDto mapping) => new(true, null, mapping);
    public static ScenarioMappingSaveResult Fail(string error) => new(false, error, null);
}
```

- [ ] **Step 2: Create the interface**

`src/MuktoAin.Application/Services/IScenarioMappingService.cs`:

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.2 / FR-18: admin CRUD over the hand-curated SCENARIO_MAPPING table that
// RagContextBuilder (T-2.3) consumes as FR-18 keyword priors. Validation must
// keep the table clean because every row here directly shapes RAG grounding:
// a bogus SectionId would silently no-op, a duplicate keyword would merge the
// same section twice.
public interface IScenarioMappingService
{
    Task<IReadOnlyList<ScenarioMappingDto>> GetAllAsync();
    Task<ScenarioMappingDto?> GetByIdAsync(int mappingId);
    Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto);
    Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto);
    Task<bool> DeleteAsync(int mappingId);
}
```

- [ ] **Step 3: Write the failing service tests (list & detail)**

Create `tests/MuktoAin.UnitTests/Services/ScenarioMappingServiceTests.cs`:

```csharp
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class ScenarioMappingServiceTests
{
    private readonly Mock<IScenarioMappingRepository> _mappingRepo = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly ScenarioMappingService _service;

    public ScenarioMappingServiceTests()
    {
        _service = new ScenarioMappingService(_mappingRepo.Object, _sectionRepo.Object, _actRepo.Object);
    }

    private static (Act Act, ActSection Section) NewSection(int sectionId = 10, int actId = 1)
    {
        var act = new Act { ActId = actId, Title = "The Labour Act, 2006", Year = 2006 };
        var section = new ActSection
        {
            SectionId = sectionId,
            ActId = actId,
            Act = act,
            OrdinalPosition = 1,
            SectionText = "wages shall be paid"
        };
        return (act, section);
    }

    // ---------- GetAllAsync ----------

    [Fact]
    public async Task GetAllAsync_EnrichesMappingsWithActTitleAndSectionNumber()
    {
        var (act, section) = NewSection();
        var mappings = new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = section.SectionId, ScenarioKeyword = "বেতন বাকি", Notes = "staple scenario" },
        };
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(mappings);
        _sectionRepo
            .Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(ids => ids.Single() == section.SectionId)))
            .ReturnsAsync(new[] { section });
        _actRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { act });

        var result = await _service.GetAllAsync();

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.MappingId);
        Assert.Equal(10, dto.SectionId);
        Assert.Equal("The Labour Act, 2006", dto.ActTitle);
        Assert.Equal("বেতন বাকি", dto.ScenarioKeyword);
        Assert.Equal("staple scenario", dto.Notes);
    }

    [Fact]
    public async Task GetAllAsync_OrphanedSectionId_RendersWithEmptyActTitle()
    {
        var mappings = new List<ScenarioMapping>
        {
            new() { MappingId = 2, SectionId = 999, ScenarioKeyword = "ghost" },
        };
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(mappings);
        _sectionRepo
            .Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(Array.Empty<ActSection>());
        _actRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<Act>());

        var result = await _service.GetAllAsync();

        var dto = Assert.Single(result);
        Assert.Equal(string.Empty, dto.ActTitle);
        // SectionNumber is null because the whole corpus is imported with null
        // SectionNumbers (ActImportService design).
        Assert.Null(dto.SectionNumber);
    }

    [Fact]
    public async Task GetAllAsync_EmptyTable_ReturnsEmptyList()
    {
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>());

        var result = await _service.GetAllAsync();

        Assert.Empty(result);
        // Short-circuits: no section/act fetch for an empty table.
        _sectionRepo.Verify(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
        _actRepo.Verify(r => r.GetAllAsync(), Times.Never);
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.GetByIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingMapping_ReturnsEnrichedDto()
    {
        var (act, section) = NewSection();
        _mappingRepo
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" });
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);

        var result = await _service.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("The Labour Act, 2006", result!.ActTitle);
        Assert.Equal("wages unpaid", result.ScenarioKeyword);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ScenarioMappingServiceTests"`
Expected: FAIL — compile error `The type or namespace name 'ScenarioMappingService' could not be found`.

- [ ] **Step 5: Implement the service (read members + shared helpers)**

Create `src/MuktoAin.Application/Services/ScenarioMappingService.cs`:

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// T-3.2 / FR-18: admin CRUD over the hand-curated SCENARIO_MAPPING keyword-boost
// table. The enrichment pattern (load all mappings — ~26 rows, load the mapped
// sections, load acts) mirrors RagContextBuilder.MergeScenarioPriorsAsync and
// AdminController.Scenarios, which already treat the table as small by design.
public class ScenarioMappingService : IScenarioMappingService
{
    private readonly IScenarioMappingRepository _mappingRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActRepository _actRepo;

    private const int MaxKeywordLength = 200;   // SCENARIO_MAPPING.ScenarioKeyword NVARCHAR(200)
    private const int MaxNotesLength = 500;     // SCENARIO_MAPPING.Notes NVARCHAR(500)

    public ScenarioMappingService(
        IScenarioMappingRepository mappingRepo,
        IActSectionRepository sectionRepo,
        IActRepository actRepo)
    {
        _mappingRepo = mappingRepo;
        _sectionRepo = sectionRepo;
        _actRepo = actRepo;
    }

    public async Task<IReadOnlyList<ScenarioMappingDto>> GetAllAsync()
    {
        var mappings = (await _mappingRepo.GetAllAsync())
            .OrderBy(m => m.MappingId)
            .ToList();
        if (mappings.Count == 0) return [];

        var sections = (await _sectionRepo.GetBySectionIdsAsync(mappings.Select(m => m.SectionId).Distinct()))
            .ToDictionary(s => s.SectionId);

        // 1,484 acts in memory is the established pattern here (RagContextBuilder
        // does exactly this on every retrieval); the table is small and static.
        var acts = (await _actRepo.GetAllAsync()).ToDictionary(a => a.ActId);

        return mappings.Select(m =>
        {
            sections.TryGetValue(m.SectionId, out var section);
            var act = section is null ? null : acts.GetValueOrDefault(section.ActId);
            return ToDto(m, section, act);
        }).ToList();
    }

    public async Task<ScenarioMappingDto?> GetByIdAsync(int mappingId)
    {
        var mapping = await _mappingRepo.GetByIdAsync(mappingId);
        if (mapping is null) return null;

        var section = await _sectionRepo.GetByIdAsync(mapping.SectionId);
        var act = section is null ? null : await _actRepo.GetByIdAsync(section.ActId);
        return ToDto(mapping, section, act);
    }

    public Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto)
    {
        throw new NotImplementedException(); // Task 6
    }

    public Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto)
    {
        throw new NotImplementedException(); // Task 6
    }

    public Task<bool> DeleteAsync(int mappingId)
    {
        throw new NotImplementedException(); // Task 6
    }

    private static ScenarioMappingDto ToDto(ScenarioMapping m, ActSection? section, Act? act)
        => new(
            m.MappingId,
            m.SectionId,
            act?.Title ?? string.Empty,
            section?.SectionNumber,
            m.ScenarioKeyword,
            m.Notes);
}
```

> `sections.TryGetValue(m.SectionId, out var section)` yields `null` for the orphaned-section case (class-typed out var), which `ToDto` maps to an empty act title — exactly what the mocked test asserts.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ScenarioMappingServiceTests"`
Expected: PASS — the 5 read tests green.

---

### Task 6: `ScenarioMappingService` — create, update, delete + DI registration (T-3.2 complete)

**Files:**
- Modify: `src/MuktoAin.Application/Services/ScenarioMappingService.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (insert immediately after the `IActsManagementService` registration from Task 4)
- Test: `tests/MuktoAin.UnitTests/Services/ScenarioMappingServiceTests.cs` (append)

**Interfaces:**
- Consumes: `IScenarioMappingRepository` generic CRUD (`AddAsync`, `UpdateAsync`, `DeleteAsync`, `SaveChangesAsync`, `GetByIdAsync`, `GetAllAsync`), `IActSectionRepository.GetByIdAsync`, `IActRepository.GetByIdAsync`.
- Produces: working `CreateAsync`/`UpdateAsync` (returns `ScenarioMappingSaveResult` with the enriched saved DTO) and `DeleteAsync` (returns `bool`); DI registration `IScenarioMappingService → ScenarioMappingService`.

- [ ] **Step 1: Write the failing tests (create/update/delete)**

Append to `tests/MuktoAin.UnitTests/Services/ScenarioMappingServiceTests.cs` (inside the test class, before the closing brace):

```csharp
    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_BlankKeyword_Fails()
    {
        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, "   ", null));

        Assert.False(result.Success);
        Assert.Equal("Keyword is required.", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_KeywordOver200Chars_Fails()
    {
        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, new string('k', 201), null));

        Assert.False(result.Success);
        Assert.Contains("200 characters or fewer", result.Error);
    }

    [Fact]
    public async Task CreateAsync_SectionNotFound_Fails()
    {
        _sectionRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ActSection?)null);

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 999, "বেতন বাকি", null));

        Assert.False(result.Success);
        Assert.Contains("999", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DuplicateKeywordOnSameSection_Fails()
    {
        var (act, section) = NewSection();
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = 10, ScenarioKeyword = "বেতন বাকি" },
        });

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, "বেতন বাকি", null));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SameKeywordOnDifferentSection_Succeeds()
    {
        var (act, section) = NewSection(sectionId: 10);
        var other = new ActSection { SectionId = 20, ActId = 1, Act = act, OrdinalPosition = 2, SectionText = "other" };
        _sectionRepo.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(other);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = 10, ScenarioKeyword = "বেতন বাকি" },
        });

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 20, "বেতন বাকি", null));

        Assert.True(result.Success);
        _mappingRepo.Verify(r => r.AddAsync(It.Is<ScenarioMapping>(m =>
            m.SectionId == 20 && m.ScenarioKeyword == "বেতন বাকি")), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_Valid_TrimsKeywordAndNotes_AndReturnsEnrichedDto()
    {
        var (act, section) = NewSection();
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>());

        var result = await _service.CreateAsync(
            new ScenarioMappingSaveDto(null, 10, "  বেতন বাকি  ", "  staple garment-worker scenario  "));

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.Equal("বেতন বাকি", result.Mapping!.ScenarioKeyword);
        Assert.Equal("The Labour Act, 2006", result.Mapping.ActTitle);
        _mappingRepo.Verify(r => r.AddAsync(It.Is<ScenarioMapping>(m =>
            m.SectionId == 10
            && m.ScenarioKeyword == "বেতন বাকি"
            && m.Notes == "staple garment scenario")), Times.Once);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_UnknownMapping_Fails()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.UpdateAsync(999, new ScenarioMappingSaveDto(999, 10, "k", null));

        Assert.False(result.Success);
        Assert.Contains("999", result.Error);
    }

    [Fact]
    public async Task UpdateAsync_KeptKeywordOnSameMapping_IsNotADuplicate()
    {
        var (act, section) = NewSection();
        var existing = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid", Notes = null };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping> { existing });

        var result = await _service.UpdateAsync(1, new ScenarioMappingSaveDto(1, 10, "wages unpaid", "updated notes"));

        Assert.True(result.Success);
        Assert.Equal("updated notes", existing.Notes);
        _mappingRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateKeywordOnAnotherMapping_Fails()
    {
        var (act, section) = NewSection();
        var existing = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" };
        var other = new ScenarioMapping { MappingId = 2, SectionId = 10, ScenarioKeyword = "overtime pay" };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping> { existing, other });

        var result = await _service.UpdateAsync(1, new ScenarioMappingSaveDto(1, 10, "Overtime Pay", null));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _mappingRepo.Verify(r => r.UpdateAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_UnknownMapping_ReturnsFalse()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.DeleteAsync(999);

        Assert.False(result);
        _mappingRepo.Verify(r => r.DeleteAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ExistingMapping_DeletesAndSaves_ReturnsTrue()
    {
        var mapping = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(mapping);

        var result = await _service.DeleteAsync(1);

        Assert.True(result);
        _mappingRepo.Verify(r => r.DeleteAsync(mapping), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ScenarioMappingServiceTests"`
Expected: FAIL — `NotImplementedException` from `CreateAsync`/`UpdateAsync`/`DeleteAsync` (11 new tests failing).

- [ ] **Step 3: Implement create/update/delete**

Replace the three `NotImplementedException` bodies in `src/MuktoAin.Application/Services/ScenarioMappingService.cs`:

```csharp
    public async Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto)
    {
        var (error, section, act) = await ValidateAndResolveAsync(dto, excludeMappingId: null);
        if (error is not null) return ScenarioMappingSaveResult.Fail(error);

        var mapping = new ScenarioMapping
        {
            SectionId = dto.SectionId,
            ScenarioKeyword = dto.ScenarioKeyword.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        };

        await _mappingRepo.AddAsync(mapping);
        await _mappingRepo.SaveChangesAsync();
        return ScenarioMappingSaveResult.Ok(ToDto(mapping, section, act));
    }

    public async Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto)
    {
        var existing = await _mappingRepo.GetByIdAsync(mappingId);
        if (existing is null)
        {
            return ScenarioMappingSaveResult.Fail($"Mapping {mappingId} was not found.");
        }

        var (error, section, act) = await ValidateAndResolveAsync(dto, excludeMappingId: mappingId);
        if (error is not null) return ScenarioMappingSaveResult.Fail(error);

        existing.SectionId = dto.SectionId;
        existing.ScenarioKeyword = dto.ScenarioKeyword.Trim();
        existing.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await _mappingRepo.UpdateAsync(existing);
        await _mappingRepo.SaveChangesAsync();
        return ScenarioMappingSaveResult.Ok(ToDto(existing, section, act));
    }

    public async Task<bool> DeleteAsync(int mappingId)
    {
        var mapping = await _mappingRepo.GetByIdAsync(mappingId);
        if (mapping is null) return false;

        await _mappingRepo.DeleteAsync(mapping);
        await _mappingRepo.SaveChangesAsync();
        return true;
    }

    // Single validation pass shared by Create/Update. Returns the resolved
    // section + act so the caller can build the enriched DTO without a re-read.
    // excludeMappingId lets Update keep its own (SectionId, Keyword) pair.
    private async Task<(string? Error, ActSection? Section, Act? Act)> ValidateAndResolveAsync(
        ScenarioMappingSaveDto dto, int? excludeMappingId)
    {
        if (dto.SectionId <= 0) return ("Section is required.", null, null);
        if (string.IsNullOrWhiteSpace(dto.ScenarioKeyword)) return ("Keyword is required.", null, null);
        if (dto.ScenarioKeyword.Trim().Length > MaxKeywordLength)
        {
            return ($"Keyword must be {MaxKeywordLength} characters or fewer (SCENARIO_MAPPING.ScenarioKeyword NVARCHAR(200)).", null, null);
        }
        if (dto.Notes is not null && dto.Notes.Trim().Length > MaxNotesLength)
        {
            return ($"Notes must be {MaxNotesLength} characters or fewer (SCENARIO_MAPPING.Notes NVARCHAR(500)).", null, null);
        }

        var section = await _sectionRepo.GetByIdAsync(dto.SectionId);
        if (section is null)
        {
            return ($"Section {dto.SectionId} was not found. SCENARIO_MAPPING.SectionId is a real FK to ACT_SECTION.", null, null);
        }

        var keyword = dto.ScenarioKeyword.Trim();
        var duplicate = (await _mappingRepo.GetAllAsync()).Any(m =>
            m.MappingId != excludeMappingId
            && m.SectionId == dto.SectionId
            && string.Equals(m.ScenarioKeyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            return ($"A mapping for keyword '{keyword}' already exists on section {dto.SectionId}.", null, null);
        }

        var act = await _actRepo.GetByIdAsync(section.ActId);
        return (null, section, act);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ScenarioMappingServiceTests"`
Expected: PASS — all 16 tests in the class green.

- [ ] **Step 5: Register in DI**

In `src/MuktoAin.Web/Program.cs`, directly after the Task 4 registration line, add:

```csharp
    // T-3.2: admin CRUD over FR-18 scenario keyword boosts (wires to E-3.2).
    builder.Services.AddScoped<IScenarioMappingService, ScenarioMappingService>();
```

- [ ] **Step 6: Verify the whole solution builds**

Run: `dotnet build MuktoAin.sln`
Expected: Build succeeded, 0 errors.

---

### Task 7: Full-suite verification & record completion in `plans/Dependency_plan.md`

**Files:**
- Modify: `plans/Dependency_plan.md` (lines 133–134)

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- Produces: green full unit-test suite; T-3.1 and T-3.2 flipped to `[x]` with strikethrough in the dependency plan (mandatory per AGENTS.md §5).

- [ ] **Step 1: Run the full unit-test suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS — all pre-existing tests plus the 41 new ones (17 `ActsManagementServiceTests` + 16 `ScenarioMappingServiceTests` + 8 new repository tests). Zero regressions.

- [ ] **Step 2: Sanity-check DI graph**

Run: `dotnet run --project src/MuktoAin.Web` (Development) and confirm startup completes with no DI resolution errors, then stop the app.
Expected: no `Unable to resolve service for type 'MuktoAin.Application.Services.IActsManagementService'` (or `IScenarioMappingService`) errors at startup.

- [ ] **Step 3: Record completion in `plans/Dependency_plan.md`** *(mandatory — AGENTS.md §5; NO git commit, Shads commits)*

Edit `plans/Dependency_plan.md` line 133 from:

```markdown
- [ ] **[T-3.1]** `ActsManagementService.cs` (Admin CRUD & SHA256 Re-indexing) — *Hrittika* `[Blocked by: T-1.8, S-1.8] [Wires to E-3.2]`
```

to:

```markdown
- [x] ~~**[T-3.1]** `ActsManagementService.cs` (Admin CRUD & SHA256 Re-indexing) — *Hrittika* `[Blocked by: T-1.8, S-1.8] [Wires to E-3.2]`~~ — implemented `IActsManagementService`/`ActsManagementService` in Application (paged+keyword-filtered act list, act detail with per-section stale-chunk counts, metadata create/update guarded by the (Title, Year) natural key, delete blocked by CASE_ACT_REFERENCE/SCENARIO_MAPPING FK counts with best-effort Qdrant vector cleanup, and `ReindexActAsync` recomputing EmbeddingBatchJob-format SHA-256 over `ChunkText`, marking stale chunks via `VectorId=NULL` for automatic re-embedding); extended `IActRepository`/`IActSectionRepository`/`IActSectionChunkRepository` with paged search, FK-safety counts, chunks-by-act, `MarkStaleAsync`, and transactional `DeleteWithChildrenAsync` (execution-strategy wrapped for EnableRetryOnFailure); registered in DI; ready for Erin's E-3.2 wiring; verified by unit tests
```

and line 134 from:

```markdown
- [ ] **[T-3.2]** `ScenarioMappingService.cs` (Admin Keyword Boosts for FR-18) — *Hrittika* `[Blocked by: T-1.12] [Wires to E-3.2]`
```

to:

```markdown
- [x] ~~**[T-3.2]** `ScenarioMappingService.cs` (Admin Keyword Boosts for FR-18) — *Hrittika* `[Blocked by: T-1.12] [Wires to E-3.2]`~~ — implemented `IScenarioMappingService`/`ScenarioMappingService` with enriched list/detail (act title + section number resolved like `RagContextBuilder`/`AdminController.Scenarios`), validated create/update (keyword required, NVARCHAR(200)/NVARCHAR(500) column limits, section-exists FK check, case-insensitive duplicate keyword-per-section guard with self-exclusion on update) and delete; registered in DI; ready for Erin's E-3.2 wiring; verified by unit tests
```

- [ ] **Step 4: Leave the working tree for Shads to review and commit**

Run: `git status`
Expected: modified/created files unstaged (per AGENTS.md §6, agents never `git add`/`git commit` — Shads is the sole committer). Do nothing further.

---

## Self-Review Notes (resolved during planning)

1. **Spec coverage:** FR-17's "incremental re-embedding based on section `ContentHash`" → Task 4 (`ReindexActAsync` + `MarkStaleAsync`, feeding `EmbeddingBatchJob`'s existing `VectorId IS NULL` work query). FR-18's "management of … scenario mappings" → Tasks 5–6. Task 1 exists because the four repo queries the services need did not exist.
2. **No category/boost columns:** the task brief said "category → keywords → boost/section hints", but the real 14-entity schema's `SCENARIO_MAPPING` is exactly (MappingId, SectionId, ScenarioKeyword NVARCHAR(200), Notes NVARCHAR(500)) — no category or numeric-boost column exists anywhere in the 14-entity schema. The FR-18 "boost" is the curated-prior merge in `RagContextBuilder` (1.0f relevance). The service CRUDs exactly what the schema supports; adding columns would violate the 14-entity integrity rule in AGENTS.md §2. Flagged as an assumption below.
3. **Type consistency:** `ActReindexResultDto`/`ActSaveResultDto`/`ActDeleteResultDto`/`ScenarioMappingSaveResult` names and shapes are identical across Tasks 2/3/4/5/6 and the DI/E-3.2 notes; repo method signatures in Task 1 match every Moq setup in Tasks 2–6 (tuple return `(IReadOnlyList<Act> Items, int TotalCount)` included).
4. **`IRepository<T>.GetByIdAsync(object id)`** is called with plain `int` arguments throughout — matches the T-1.4 amendment and the existing `CategoryService`/test usage.

## Assumptions & Open Questions

1. **Scenario mapping shape (resolved against the real schema):** no category/boost columns exist; the plan deliberately does not invent them. If the team later wants a numeric boost weight or per-category grouping, that is a schema change (out of scope for T-3.2's "CRUD over the existing entity").
2. **Create-act is metadata-only:** an admin-created Act starts with zero sections/chunks; section content still enters via the T-1.8 import pipeline or a future re-import flow. Manual section editing was not in the task brief and is intentionally out of scope (YAGNI; it would also invalidate chunk hashes in ways `ReindexActAsync` is designed to *detect*, not author).
3. **Delete guards delete-anything-else:** acts referenced by `CASE_ACT_REFERENCE` or `SCENARIO_MAPPING` rows are blocked, not cascade-deleted — silently destroying audit trail (FR-12) or curated priors (FR-18) would be worse than a blocked delete with a clear message.
4. **`MarkStaleAsync`/`DeleteWithChildrenAsync` InMemory gap:** they use `ExecuteUpdateAsync`/raw SQL and are implemented but not InMemory-tested here — deferred to T-3.3 repository & DB integration tests exactly as T-1.14 did for `FromSqlRaw`/`ExecuteUpdateAsync` methods.
5. **E-3.2 handoff:** Erin's controller should inject `IActsManagementService` and `IScenarioMappingService` (both registered in DI by Tasks 4 and 6). The existing `AdminController.Corpus`/`Scenarios` actions that query repos directly can migrate to these services during E-3.2 — not done here to avoid touching Erin's in-flight work.
