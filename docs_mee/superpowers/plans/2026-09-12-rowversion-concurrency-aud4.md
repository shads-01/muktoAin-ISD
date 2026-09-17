# RowVersion Concurrency Tokens (AUD-4, Hrittika) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add database-level optimistic concurrency (`rowversion`) tokens to `GENERATED_DOCUMENT`, `CASE`, and `PAYMENT_ORDER` so the lawyer-claim race, case-status transitions, and payment mark-paid/refund paths fail loudly instead of silently last-write-wins.

**Architecture:** EF Core optimistic concurrency via `byte[] RowVersion` properties mapped with `IsRowVersion()` (SQL Server `rowversion` column type — auto-updated by the engine on every write). Concurrent writers get `DbUpdateConcurrencyException`; the two known race call sites (`LawyerReviewService.ClaimAsync`, `PaymentService.MarkPaidAsync`) catch it and surface a `false`/friendly failure instead of corrupting state. Schema change ships as a hand-written idempotent T-SQL script run in SSMS (no EF migrations — repo convention).

**Tech Stack:** .NET 8, EF Core 8 (SQL Server provider), xUnit + Moq, T-SQL in SSMS.

**Spec:** `docs/PROJECT_AUDIT_REPORT.md` (finding: missing DB-level concurrency control on lawyer claim / payment double-processing / case status TOCTOU) + `plans/Dependency_plan.md` AUD-4.

## Global Constraints

- **NO git commits, ever.** Per `AGENTS.md` §6, Shads is the sole committer. Where the writing-plans skill says "Commit", this plan says "Record completion in `plans/Dependency_plan.md`".
- **No EF migrations.** Schema changes are hand-written idempotent T-SQL scripts under `scripts/` (`SET NOCOUNT ON`, `IF NOT EXISTS`-style guards, safe to re-run). Script number **13** is reserved for this task per the numbering-coordination note in `plans/Dependency_plan.md` (11=notifications, 12=issuperadmin, 14=admin-audit, 15=payment-transaction-id).
- SQL must be fully parameterized in application code; the SSMS script is DDL only.
- Unit tests: xUnit + Moq in `tests/MuktoAin.UnitTests`, EF InMemory where possible; `rowversion` value-generation itself is SQL-Server-only and is covered by a manual SSMS verification step + T-3.3 integration tests, NOT by InMemory.
- Existing public method signatures must not break callers: `ClaimAsync` stays `Task<bool>`; `MarkPaidAsync(int, string)` keeps its signature.

---

### Task 1: `RowVersion` properties + EF configuration (concurrency metadata verified by test)

**Files:**
- Modify: `src/MuktoAin.Domain/Entities/GeneratedDocument.cs`
- Modify: `src/MuktoAin.Domain/Entities/Case.cs`
- Modify: `src/MuktoAin.Domain/Entities/PaymentOrder.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/GeneratedDocumentConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/CaseConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/PaymentOrderConfiguration.cs`
- Test: `tests/MuktoAin.UnitTests/Infrastructure/RowVersionConfigurationTests.cs` (create)

**Interfaces:**
- Consumes: existing `AppDbContext` (`builder.ApplyConfiguration` pattern — read `src/MuktoAin.Infrastructure/Data/AppDbContext.cs` `OnModelCreating` to confirm each configuration is already applied there; if a configuration class is registered via `ApplyConfiguration`, no DbContext edit is needed).
- Produces: `public byte[] RowVersion { get; set; }` on all three entities, mapped as SQL Server `rowversion` concurrency tokens. Later tasks (and AUD-11's idempotency guards) rely on `DbUpdateConcurrencyException` being thrown by EF when the token mismatches.

- [ ] **Step 1: Write the failing configuration test**

Create `tests/MuktoAin.UnitTests/Infrastructure/RowVersionConfigurationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Data;
using Xunit;

namespace MuktoAin.UnitTests.Infrastructure;

public class RowVersionConfigurationTests
{
    [Fact]
    public void GeneratedDocument_RowVersion_IsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("rv-probe-doc").Options;
        using var ctx = new AppDbContext(options);
        var prop = ctx.Model.FindEntityType(typeof(GeneratedDocument))!
            .FindProperty("RowVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
    }

    [Fact]
    public void Case_RowVersion_IsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("rv-probe-case").Options;
        using var ctx = new AppDbContext(options);
        var prop = ctx.Model.FindEntityType(typeof(Case))!.FindProperty("RowVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
    }

    [Fact]
    public void PaymentOrder_RowVersion_IsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("rv-probe-order").Options;
        using var ctx = new AppDbContext(options);
        var prop = ctx.Model.FindEntityType(typeof(PaymentOrder))!.FindProperty("RowVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~RowVersionConfigurationTests"`
Expected: **FAIL to compile** with "'GeneratedDocument' does not contain a definition for 'RowVersion'" (properties don't exist yet). If it compiles, the entity files already changed — investigate before proceeding.

- [ ] **Step 3: Add the property to all three entities**

In each of `src/MuktoAin.Domain/Entities/GeneratedDocument.cs`, `Case.cs`, `PaymentOrder.cs`, add at the end of the class body (match the file's existing pragma/nullable style):

```csharp
    /// <summary>SQL Server rowversion token for optimistic concurrency (AUD-4).</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
```

(ensure `using System;` or `using System;` equivalent resolves `Array` — most files already have implicit/usings).

- [ ] **Step 4: Map the property in each EF configuration**

In `GeneratedDocumentConfiguration.cs`, `CaseConfiguration.cs`, `PaymentOrderConfiguration.cs` (inside the existing `Configure`/`IEntityTypeConfiguration<TEntity>.Configure` method, next to the existing `builder.ToTable(...)` / property mappings):

```csharp
        builder.Property(e => e.RowVersion)
            .IsRowVersion()
            .HasColumnName("RowVersion");
```

`.IsRowVersion()` marks it `ValueGeneratedOnAddOrUpdate` + concurrency token on SQL Server; on the real DB the column type will be `rowversion` (created in Task 2), which auto-updates on every write.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~RowVersionConfigurationTests"`
Expected: PASS (3/3). Then run the full unit suite: `dotnet test tests/MuktoAin.UnitTests` — expected: all existing tests still pass (new nullable byte[] columns don't affect InMemory behavior).

### Task 2: Idempotent SSMS script `scripts/13_add_rowversion_columns.sql`

**Files:**
- Create: `scripts/13_add_rowversion_columns.sql`

**Interfaces:**
- Consumes: tables `[dbo].[GENERATED_DOCUMENT]`, `[dbo].[CASE]`, `[dbo].[PAYMENT_ORDER]` (created by `scripts/02_schema.sql` and `scripts/08_redesign_tables.sql`).
- Produces: `RowVersion rowversion NOT NULL` column on all three tables. EF's `IsRowVersion()` mapping from Task 1 matches by column name `RowVersion`.

- [ ] **Step 1: Write the script**

Create `scripts/13_add_rowversion_columns.sql`:

```sql
-- scripts/13_add_rowversion_columns.sql
-- AUD-4: optimistic concurrency tokens for GENERATED_DOCUMENT, CASE, PAYMENT_ORDER.
-- Idempotent: safe to re-run. Run in SSMS against the MuktoAin database.
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[GENERATED_DOCUMENT]
        ADD [RowVersion] ROWVERSION;
    PRINT 'GENERATED_DOCUMENT.RowVersion added.';
END
ELSE
    PRINT 'GENERATED_DOCUMENT.RowVersion already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[CASE]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[CASE]
        ADD [RowVersion] ROWVERSION;
    PRINT 'CASE.RowVersion added.';
END
ELSE
    PRINT 'CASE.RowVersion already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER]
        ADD [RowVersion] ROWVERSION;
    PRINT 'PAYMENT_ORDER.RowVersion added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.RowVersion already exists.';
GO
```

(`ROWVERSION` columns are implicitly `NOT NULL` and engine-maintained — no default needed. `CASE` must stay bracketed: reserved word.)

- [ ] **Step 2: Verify idempotency locally**

Run the script **twice** in SSMS (or `sqlcmd -S .\SQLEXPRESS -d MuktoAin -i scripts\13_add_rowversion_columns.sql`). Expected: first run prints three "added" messages; second run prints three "already exists" messages with no errors. Confirm columns exist:

```sql
SELECT t.name AS tbl, c.name AS col, t.name AS type_name
FROM sys.columns c JOIN sys.types ty ON c.user_type_id = ty.user_type_id
JOIN sys.tables t ON c.object_id = t.object_id
WHERE c.name = 'RowVersion';  -- expect 3 rows, type rowversion
```

- [ ] **Step 3: Record completion in `plans/Dependency_plan.md`** *(mandatory — AGENTS.md §5; NO git commit, Shads commits)*

Flip the AUD-4 line to `- [x] ~~**[AUD-4]** RowVersion Concurrency Tokens on Schema — *Hrittika* …~~` and append a short note: *"RowVersion tokens added to GENERATED_DOCUMENT/CASE/PAYMENT_ORDER (EF IsRowVersion) + scripts/13_add_rowversion_columns.sql (idempotent); config metadata verified by 3 unit tests; runtime conflict handling covered by AUD-11 guards + T-3.3 DB tests."* Do NOT run `git add`/`git commit` — leave changes in the working tree for Shads.

---

### Task 3: Surface `DbUpdateConcurrencyException` as a clean `false` in the two known race paths

**Files:**
- Modify: `src/MuktoAin.Application/Services/LawyerReviewService.cs` (`ClaimAsync`, line ~100)
- Modify: `src/MuktoAin.Application/Services/PaymentService.cs` (`MarkPaidAsync`, line ~92)
- Test: `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs` (extend)
- Test: `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `IRepository<GeneratedDocument>.SaveChangesAsync()`, `IRepository<PaymentOrder>.SaveChangesAsync()` (existing Moq'd interfaces), `DbUpdateConcurrencyException` from `Microsoft.EntityFrameworkCore`.
- Produces: no signature change — `ClaimAsync` and `MarkPaidAsync` keep returning `Task<bool>`; both now return `false` (instead of throwing) when a concurrent writer won the rowversion race. Callers (`LawyerController`, `PaymentController`) already handle `false`.

- [ ] **Step 1: Write the failing tests**

In `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs` (follow the existing Moq fixture style in that file — reuse its existing `_docRepo` mock setup helpers), add:

```csharp
    [Fact]
    public async Task ClaimAsync_ReturnsFalse_WhenConcurrencyConflict()
    {
        var doc = new GeneratedDocument
        {
            Id = 7, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = null
        };
        _docRepoMock.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(doc);
        _docRepoMock
            .Setup(r => r.SaveChangesAsync())
            .ThrowsAsync(new DbUpdateConcurrencyException("rowversion conflict"));

        var svc = CreateService();
        var ok = await svc.ClaimAsync(7, lawyerProfileId: 42);

        Assert.False(ok);
    }
```

In `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs` (follow existing fixture patterns in that file), add the analogous test for `MarkPaidAsync`: mock the order repository's `SaveChangesAsync` to throw `DbUpdateConcurrencyException`, assert the method returns `false` (or its existing failure representation — read the method's current return shape first and assert against that shape).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~ClaimAsync_ReturnsFalse_WhenConcurrencyConflict|FullyQualifiedName~MarkPaidAsync_Concurrency"`
Expected: FAIL — the exception currently propagates out of the service.

- [ ] **Step 3: Implement the catch**

In `LawyerReviewService.ClaimAsync`, wrap the mutation + save:

```csharp
        d.AssignedLawyerProfileId = lawyerProfileId;
        d.ClaimedAt = DateTime.UtcNow;
        try
        {
            await _docRepo.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // another lawyer claimed first (AUD-4)
        }
```

In `PaymentService.MarkPaidAsync`, add the same `try/catch (DbUpdateConcurrencyException)` around the `SaveChangesAsync` call, returning the method's existing failure value (`false`). Add `using Microsoft.EntityFrameworkCore;` to both files if not present.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~Concurrency"`
Expected: PASS. Then full suite: `dotnet test tests/MuktoAin.UnitTests` — expected: all pass.

- [ ] **Step 5: Build the full solution**

Run: `dotnet build MuktoAin.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Record completion in `plans/Dependency_plan.md`**

The AUD-4 checkbox itself is flipped only once — in Task 2's record step. For THIS task, record the user-visible behavior change (claim race now returns `false` instead of a 500) by appending a `[R-30]`-style note to the Redesign Wave section. Add:

```markdown
- [x] **[R-30]** Lawyer claim / payment mark-paid race surfaced as clean failure — *Hrittika* (AUD-4 follow-through: `DbUpdateConcurrencyException` from the new rowversion tokens is caught in `LawyerReviewService.ClaimAsync` and `PaymentService.MarkPaidAsync`, returning `false` instead of an unhandled 500; unit tests added for both paths).
```

Do NOT run `git commit` — leave changes in the working tree for Shads.

---

## Post-Implementation Notes (informational)

- **InMemory caveat:** EF InMemory honors concurrency tokens for *value comparison* but does not emulate SQL Server's automatic `rowversion` increment; true cross-connection conflict tests land in T-3.3 (`docs/superpowers/plans/2026-09-12-repository-db-integration-tests.md`) against `MuktoAin_IntegrationTest` — add a claim-race integration test there after both plans land.
- **AUD-11 interplay:** Arpita's `PaymentService` idempotency guards (status checks before mark-paid/refund) are the *business-level* safety net; the rowversion token here is the *database-level* backstop. Both ship independently; neither depends on the other's code.
- **EF InMemory note for reviewers:** `IsRowVersion()` on InMemory sets the token but leaves the value `Array.Empty<byte>()`; the configuration tests assert the *metadata* (IsConcurrencyToken), which is the portable contract.
