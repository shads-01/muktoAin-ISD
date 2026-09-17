# Admin Audit Trail, Pagination & Payment Idempotency (Arpita) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three audit findings in one pass — AUD-7 (a real `AdminAuditLog` administrative audit trail replacing the fake AI_LOG-based dashboard panel), AUD-8 (pagination on the Lawyer review queue and Admin Users/AiLogs pages), and AUD-11 (idempotency guards on `PaymentService.MarkPaidAsync`/`RefundAsync` so double admin clicks can't double-process money).

**Architecture:** `AdminAuditLog` is a new Domain entity mapped onto a new `[dbo].[ADMIN_AUDIT_LOG]` table via an `IEntityTypeConfiguration` (the schema itself lands as a hand-written, idempotent T-SQL script `scripts/14_add_admin_audit_log.sql` — no EF migrations, per project rule). A fail-safe `AdminAuditService` behind `IAdminAuditService` writes audit rows and is injected into `UserManagementService`, `LawyerVerificationService`, `PaymentService`, and `AdminController`. Pagination follows the pattern already established by `LawyerController.History`: total-count + `Skip`/`Take` + a `.pagination` Razor control block.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core (`AppDbContext` maps onto a hand-authored MSSQL schema), xUnit + Moq, Razor + Bootstrap-5-flavored CSS classes already in the site.

**Spec:** `docs/PROJECT_AUDIT_REPORT.md` — Admin Scope #2 (no idempotency guard on financial admin actions), #4 (no pagination on Users/AiLogs), #5 (no real administrative audit log), and Lawyer Scope #4 (no pagination on the review queue).

## Global Constraints

- **NO git commits, ever.** AGENTS.md §6: Shads is the sole committer. All "commit" steps in the skill default are replaced with "Record completion in `plans/Dependency_plan.md`" steps.
- **No EF migrations.** Schema changes are hand-written T-SQL in `scripts/`, idempotent (`SET NOCOUNT ON;` + `IF OBJECT_ID(...) IS NULL` guards), executed manually in SSMS.
- **SQL script numbering:** `scripts/14_add_admin_audit_log.sql` is reserved for AUD-7 per the "SQL script numbering coordination" note in `plans/Dependency_plan.md` (lines 220-224). Do not renumber other scripts.
- **Clean Architecture:** the `AdminAuditLog` entity lives in `MuktoAin.Domain` (no external deps); the audit service in `MuktoAin.Application`; EF configuration + SQL in `MuktoAin.Infrastructure`; DI + views in `MuktoAin.Web`.
- **Repo conventions:** entity PKs are named `<Entity>Id` (e.g. `AdminAuditLogId` — note: `Arpita_plan.md` sketches `Id`, but every existing entity in `src/MuktoAin.Domain/Entities/` uses the `<Entity>Id` convention; this plan follows the repo convention). Configurations map with `ToTable("NAME", "dbo")`. Tables are UPPER_SNAKE, enums stored as `INT`.
- **Test command:** `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~<TestClass>"`; full build is `dotnet build MuktoAin.slnx`.
- **Bengali numerals:** the Lawyer-facing Queue view may reuse the `ToBn` helper pattern from `Views/Lawyer/History.cshtml`; Admin views (`Users`, `AiLogs`) are English-only and use plain numerals.
- All existing tests must keep passing. Constructor signature changes called out in a task MUST be applied to every construction site listed in that task.

---

### Task 1: AUD-11 — PaymentService Idempotency Guards

**Files:**
- Modify: `src/MuktoAin.Application/Services/PaymentService.cs:92-127` (`MarkPaidAsync`, `RefundAsync`)
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs:451-477` (`RefundOrder`, `MarkOrderPaid`)
- Test: `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs`

**Interfaces:**
- Consumes: existing `PaymentStatus` enum (`Pending=0, Paid=1, Failed=2, Refunded=3`) in `src/MuktoAin.Domain/Enums/PaymentStatus.cs`; `IRepository<PaymentOrder>` (`GetByIdAsync(object id)`, `SaveChangesAsync()`).
- Produces: `MarkPaidAsync(int paymentOrderId, string gatewayRef)` now throws `InvalidOperationException` when the order is already `Paid` or `Refunded`, and `RefundAsync(int paymentOrderId)` throws `InvalidOperationException` when the order is not `Paid`. (Task 6 later appends an *optional* `int? actingAdminId = null` parameter — callers here stay source-compatible.)
- `AdminController.RefundOrder`/`MarkOrderPaid` catch `InvalidOperationException` and surface `TempData["Error"]` instead of a 500.

- [ ] **Step 1: Write the failing tests**

Open `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs` and add these tests inside `PaymentServiceTests` (after the existing `CreateHonorariumOrderAsync_ResolvesLawyerFromCasesClaimedDocument` test). Note the class already has `_orderRepo` (`Mock<IRepository<PaymentOrder>>`) and `_service` fields:

```csharp
    // AUD-11: a second admin click (or resent form) on "Mark Paid" must never
    // re-process the order -- GetLawyerEarningsAsync sums ALL Paid rows, so a
    // duplicate mark-paid directly inflates a lawyer's payable balance.
    [Fact]
    public async Task MarkPaidAsync_AlreadyPaid_ThrowsAndPreservesOriginalState()
    {
        var paidAt = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var order = new PaymentOrder
        {
            PaymentOrderId = 1,
            Status = PaymentStatus.Paid,
            GatewayRef = "SBX-ORIGINAL",
            PaidAt = paidAt
        };
        _orderRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.MarkPaidAsync(1, "SBX-DUPLICATE"));

        Assert.Equal(PaymentStatus.Paid, order.Status);
        Assert.Equal("SBX-ORIGINAL", order.GatewayRef);
        Assert.Equal(paidAt, order.PaidAt);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAsync_AlreadyRefunded_Throws()
    {
        var order = new PaymentOrder { PaymentOrderId = 2, Status = PaymentStatus.Refunded };
        _orderRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(order);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.MarkPaidAsync(2, "SBX-AFTER-REFUND"));

        Assert.Equal(PaymentStatus.Refunded, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    // AUD-11: RefundAsync previously SILENTLY no-op'd on non-Paid orders; now it
    // must reject loudly so an admin sees why nothing happened.
    [Fact]
    public async Task RefundAsync_NotPaid_ThrowsInvalidOperationException()
    {
        var order = new PaymentOrder { PaymentOrderId = 3, Status = PaymentStatus.Pending };
        _orderRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(order);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RefundAsync(3));

        Assert.Equal(PaymentStatus.Pending, order.Status);
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task RefundAsync_PaidOrder_RefundsAndReversesLedger()
    {
        var order = new PaymentOrder
        {
            PaymentOrderId = 4,
            Status = PaymentStatus.Paid,
            CaseId = 10,
            Purpose = PaymentPurpose.Honorarium
        };
        _orderRepo.Setup(r => r.GetByIdAsync(4)).ReturnsAsync(order);
        var c = new Case { CaseId = 10, HonorariumPaid = true };
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(c);

        await _service.RefundAsync(4);

        Assert.Equal(PaymentStatus.Refunded, order.Status);
        Assert.NotNull(order.RefundedAt);
        Assert.False(c.HonorariumPaid); // ledger reversal
        _orderRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"`
Expected: FAIL — `MarkPaidAsync_AlreadyPaid_*` and `RefundAsync_NotPaid_*` throw nothing today (current code overwrites status / silently returns), so `Assert.ThrowsAsync` fails.

- [ ] **Step 3: Implement the guards in PaymentService**

Replace the two methods in `src/MuktoAin.Application/Services/PaymentService.cs` (currently lines 92-100 and 110-127) with:

```csharp
    // Sandbox "IPN confirmed" action. AUD-11 idempotency guard: an order that is
    // already Paid (or already Refunded) can never be re-processed — a duplicate
    // mark-paid would double-count in GetLawyerEarningsAsync and inflate the
    // lawyer's payable balance (docs/PROJECT_AUDIT_REPORT.md, Admin Scope #2).
    public async Task MarkPaidAsync(int paymentOrderId, string gatewayRef)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        if (o.Status is PaymentStatus.Paid or PaymentStatus.Refunded)
            throw new InvalidOperationException(
                $"Order {paymentOrderId} is already {o.Status} and cannot be marked Paid again.");
        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();
    }
```

```csharp
    // AUD-11: only a Paid order can be refunded — Pending/Failed/Refunded states
    // are rejected loudly (was: silent no-op return, hiding admin mistakes).
    public async Task RefundAsync(int paymentOrderId)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        if (o.Status != PaymentStatus.Paid)
            throw new InvalidOperationException(
                $"Order {paymentOrderId} is {o.Status}; only Paid orders can be refunded.");
        o.Status = PaymentStatus.Refunded;
        o.RefundedAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        if (o.CaseId.HasValue && o.Purpose == PaymentPurpose.Honorarium)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null)
            {
                c.HonorariumPaid = false; // ledger reversed
                await _caseRepo.SaveChangesAsync();
            }
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"`
Expected: PASS (all 5 tests, including the pre-existing one).

- [ ] **Step 5: Make AdminController surface the rejection instead of a 500**

In `src/MuktoAin.Web/Controllers/AdminController.cs` replace `RefundOrder` (lines 451-458) and `MarkOrderPaid` (lines 469-477) with:

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundOrder(int orderId)
    {
        try
        {
            await _paymentService.RefundAsync(orderId);
            TempData["Success"] = "Order refunded (sandbox) — ledger reversed.";
        }
        catch (InvalidOperationException ex)
        {
            // AUD-11: double-refund / refund-of-unpaid attempt — tell the admin,
            // don't 500.
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Transactions));
    }
```

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrderPaid(int orderId)
    {
        // Sandbox gateway confirm (in lieu of real SSLCommerz IPN)
        try
        {
            await _paymentService.MarkPaidAsync(orderId, $"SBX-{Guid.NewGuid().ToString("N")[..12].ToUpper()}");
            TempData["Success"] = "Order marked Paid (sandbox gateway).";
        }
        catch (InvalidOperationException ex)
        {
            // AUD-11: double mark-paid attempt — show why the order was untouched.
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Transactions));
    }
```

- [ ] **Step 6: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Change `- [ ] **[AUD-11]**` to `- [x]` and wrap that entire line in `~~...~~` strikethrough, appending a one-line summary of what was implemented (match the style of completed entries like `[A-2.6]`).

---

### Task 2: AUD-7 — AdminAuditLog Entity + EF Configuration + SQL Script

**Files:**
- Create: `src/MuktoAin.Domain/Entities/AdminAuditLog.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/AdminAuditLogConfiguration.cs`
- Create: `scripts/14_add_admin_audit_log.sql`
- Modify: `src/MuktoAin.Infrastructure/Data/AppDbContext.cs` (add one `DbSet` line)

**Interfaces:**
- Consumes: the `PaymentOrderConfiguration` pattern (`IEntityTypeConfiguration<T>` with `ToTable`); the `[dbo].[USER]` table whose PK is `UserId` (proven by `scripts/08_redesign_tables.sql:27` FK).
- Produces: `MuktoAin.Domain.Entities.AdminAuditLog` with properties `AdminAuditLogId (int)`, `AdminUserId (int)`, `AdminUser (User?)`, `TargetUserId (int?)`, `TargetEntityId (int?)`, `Details (string?)`, `Action (string)`, `CreatedAt (DateTime)`; `AppDbContext.AdminAuditLogs` DbSet. Tasks 3-7 consume these.

- [ ] **Step 1: Create the entity**

Create `src/MuktoAin.Domain/Entities/AdminAuditLog.cs`:

```csharp
namespace MuktoAin.Domain.Entities;

// AUD-7: real administrative audit trail (docs/PROJECT_AUDIT_REPORT.md — Admin
// Scope #5). The Admin dashboard "Audit Logs" panel was populated from AI_LOG
// rows (AI calls), not admin actions; nothing recorded WHO suspended a user,
// verified a lawyer, refunded a payment, or deleted a scenario mapping.
// Append-only by design — no code ever updates or deletes these rows.
public class AdminAuditLog
{
    public int AdminAuditLogId { get; set; }

    // The acting admin's [dbo].[USER].UserId (FK)
    public int AdminUserId { get; set; }
    public User? AdminUser { get; set; }

    // Machine-readable action name, e.g. "SuspendUser", "ApproveLawyerVerification",
    // "RefundOrder", "MarkOrderPaid", "DeleteScenario"
    public string Action { get; set; } = string.Empty;

    // Optional: the user the action was performed ON (suspensions, verifications)
    public int? TargetUserId { get; set; }

    // Optional: PK of the affected entity (payment order, lawyer profile, scenario mapping)
    public int? TargetEntityId { get; set; }

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

Create `src/MuktoAin.Infrastructure/Data/Configurations/AdminAuditLogConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

// Maps onto [dbo].[ADMIN_AUDIT_LOG] from scripts/14_add_admin_audit_log.sql (AUD-7).
// AdminUser FK mirrors LawyerProfileConfiguration's FK-to-USER pattern; TargetUserId
// is intentionally NOT configured as an EF relationship (it is a soft reference —
// the audit row must survive even if the target user is ever hard-deleted).
public class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
    {
        builder.ToTable("ADMIN_AUDIT_LOG", "dbo");
        builder.HasKey(l => l.AdminAuditLogId);
        builder.Property(l => l.Action).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Details).HasMaxLength(1000);
        builder.HasOne(l => l.AdminUser)
               .WithMany()
               .HasForeignKey(l => l.AdminUserId);
    }
}
```

- [ ] **Step 3: Register the DbSet in AppDbContext**

In `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`, add one line to the "Redesign 2026-09" block (after line 44, `public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();`):

```csharp
    // AUD-7 (scripts/14_add_admin_audit_log.sql)
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
```

(No registration call needed for the configuration — `OnModelCreating` already calls `ApplyConfigurationsFromAssembly`.)

- [ ] **Step 4: Create the SQL script**

Create `scripts/14_add_admin_audit_log.sql` (idempotent, SSMS style matching `scripts/08_redesign_tables.sql`):

```sql
/* ============================================================
   MuktoAin — Admin audit trail (2026-09-12, AUD-7)
   Real administrative action log: who suspended/reactivated a
   user, verified/rejected a lawyer, refunded/marked-paid an
   order, deleted a scenario mapping (docs/PROJECT_AUDIT_REPORT.md
   — Admin Scope #5).
   IDEMPOTENT: safe to re-run; CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[ADMIN_AUDIT_LOG]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ADMIN_AUDIT_LOG] (
        AdminAuditLogId INT IDENTITY(1,1) NOT NULL,
        AdminUserId     INT               NOT NULL,
        [Action]        NVARCHAR(50)      NOT NULL,
        TargetUserId    INT               NULL,
        TargetEntityId  INT               NULL,
        Details         NVARCHAR(1000)    NULL,
        CreatedAt       DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ADMIN_AUDIT_LOG PRIMARY KEY (AdminAuditLogId),
        CONSTRAINT FK_ADMIN_AUDIT_LOG_AdminUser FOREIGN KEY (AdminUserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_ADMIN_AUDIT_LOG_TargetUser FOREIGN KEY (TargetUserId)
            REFERENCES [dbo].[USER] (UserId)
    );
    CREATE INDEX IX_ADMIN_AUDIT_LOG_CreatedAt ON [dbo].[ADMIN_AUDIT_LOG] (CreatedAt DESC);
    CREATE INDEX IX_ADMIN_AUDIT_LOG_AdminUserId ON [dbo].[ADMIN_AUDIT_LOG] (AdminUserId);
    CREATE INDEX IX_ADMIN_AUDIT_LOG_Action ON [dbo].[ADMIN_AUDIT_LOG] ([Action]);
END
GO
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

---

### Task 3: AUD-7 — IAdminAuditService + AdminAuditService (fail-safe) + DI + Unit Tests

**Files:**
- Create: `src/MuktoAin.Application/Services/IAdminAuditService.cs`
- Create: `src/MuktoAin.Application/Services/AdminAuditService.cs`
- Create: `tests/MuktoAin.UnitTests/Services/AdminAuditServiceTests.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (one DI line)

**Interfaces:**
- Consumes: `AdminAuditLog` (Task 2), `IRepository<AdminAuditLog>` (generic, auto-registered at `Program.cs:128`).
- Produces: `IAdminAuditService.LogAdminActionAsync(int adminUserId, string action, int? targetUserId = null, int? targetEntityId = null, string? details = null)` — contractually fail-safe (never throws). Tasks 4-7 inject `IAdminAuditService`.

- [ ] **Step 1: Write the failing tests**

Create `tests/MuktoAin.UnitTests/Services/AdminAuditServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

// AUD-7: the audit service is contractually fail-safe — an audit-write failure
// (DB down, transient fault) must never break the admin action that triggered it.
public class AdminAuditServiceTests
{
    private readonly Mock<IRepository<AdminAuditLog>> _repo = new();
    private readonly AdminAuditService _service;

    public AdminAuditServiceTests()
    {
        _service = new AdminAuditService(_repo.Object, Mock.Of<ILogger<AdminAuditService>>());
    }

    [Fact]
    public async Task LogAdminActionAsync_WritesRowWithAllFields()
    {
        AdminAuditLog? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<AdminAuditLog>()))
            .Callback<AdminAuditLog>(l => captured = l)
            .Returns(Task.CompletedTask);

        await _service.LogAdminActionAsync(1, "SuspendUser",
            targetUserId: 5, targetEntityId: null, details: "spam account");

        Assert.NotNull(captured);
        Assert.Equal(1, captured!.AdminUserId);
        Assert.Equal("SuspendUser", captured.Action);
        Assert.Equal(5, captured.TargetUserId);
        Assert.Null(captured.TargetEntityId);
        Assert.Equal("spam account", captured.Details);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task LogAdminActionAsync_WhenRepoThrows_DoesNotPropagate()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<AdminAuditLog>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        // Must complete without throwing — audit failure can never break the
        // admin action it records.
        await _service.LogAdminActionAsync(1, "RefundOrder", targetEntityId: 9);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminAuditServiceTests"`
Expected: BUILD FAIL with "type or namespace 'AdminAuditService' could not be found".

- [ ] **Step 3: Create the interface**

Create `src/MuktoAin.Application/Services/IAdminAuditService.cs`:

```csharp
namespace MuktoAin.Application.Services;

// AUD-7: append-only administrative audit trail. Implementations MUST be
// fail-safe — LogAdminActionAsync never throws, so a failed audit write can
// never turn into a 500 for the admin action it is recording.
public interface IAdminAuditService
{
    Task LogAdminActionAsync(
        int adminUserId,
        string action,
        int? targetUserId = null,
        int? targetEntityId = null,
        string? details = null);
}
```

- [ ] **Step 4: Create the implementation**

Create `src/MuktoAin.Application/Services/AdminAuditService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// AUD-7. Fail-safe by contract: any exception from the repository write is
// logged and swallowed. The admin action being audited has already succeeded
// by the time this is called; losing its audit row is logged (LogError), never
// rethrown (docs/PROJECT_AUDIT_REPORT.md — Admin Scope #5).
public class AdminAuditService : IAdminAuditService
{
    private readonly IRepository<AdminAuditLog> _repo;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(IRepository<AdminAuditLog> repo, ILogger<AdminAuditService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task LogAdminActionAsync(
        int adminUserId,
        string action,
        int? targetUserId = null,
        int? targetEntityId = null,
        string? details = null)
    {
        try
        {
            await _repo.AddAsync(new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                TargetUserId = targetUserId,
                TargetEntityId = targetEntityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
            await _repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write admin audit log (admin {AdminUserId}, action {Action})",
                adminUserId, action);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminAuditServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Register in DI**

In `src/MuktoAin.Web/Program.cs`, immediately after line 239 (`builder.Services.AddScoped<PaymentService>();`), add:

```csharp
    // AUD-7: administrative audit trail (fail-safe writer).
    builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
```

- [ ] **Step 7: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

---

### Task 4: AUD-7 — Wire Audit into UserManagementService (Suspend/Unsuspend)

**Files:**
- Modify: `src/MuktoAin.Application/Services/UserManagementService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/UserManagementServiceTests.cs`

**Interfaces:**
- Consumes: `IAdminAuditService.LogAdminActionAsync(int, string, int?, int?, string?)` (Task 3).
- Produces: `UserManagementService(UserManager<User> userManager, IAdminAuditService audit)` — new constructor signature. Every `new UserManagementService(...)` call site must be updated: only `tests/MuktoAin.UnitTests/Services/UserManagementServiceTests.cs:19` (DI resolves the rest automatically via `AddScoped<IUserManagementService, UserManagementService>()` at `Program.cs:197`). Emits audit actions `"SuspendUser"` / `"UnsuspendUser"`.

- [ ] **Step 1: Update the existing test constructor + add audit assertions**

In `tests/MuktoAin.UnitTests/Services/UserManagementServiceTests.cs`:

Add using:

```csharp
using Moq;
```

(already present) and change the field/constructor block (lines 11-20) to:

```csharp
    private readonly Mock<IUserStore<User>> _userStoreMock = new();
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<IAdminAuditService> _auditMock = new();
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        _userManagerMock = new Mock<UserManager<User>>(
            _userStoreMock.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _service = new UserManagementService(_userManagerMock.Object, _auditMock.Object);
    }
```

Then add these tests at the end of the class:

```csharp
    // AUD-7: the audit row records WHO flipped the status, on WHOM, to WHAT.
    [Fact]
    public async Task SetAccountStatusAsync_WhenSuspensionSucceeds_LogsSuspendAudit()
    {
        var user = new User { Id = 5, Role = UserRole.Citizen, AccountStatus = AccountStatus.Active };
        _userManagerMock.Setup(m => m.FindByIdAsync("5")).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(m => m.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        await _service.SetAccountStatusAsync(5, AccountStatus.Suspended, actingAdminId: 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "SuspendUser", 5, null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenReactivationSucceeds_LogsUnsuspendAudit()
    {
        var user = new User { Id = 5, Role = UserRole.Citizen, AccountStatus = AccountStatus.Suspended };
        _userManagerMock.Setup(m => m.FindByIdAsync("5")).ReturnsAsync(user);
        _userManagerMock.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        await _service.SetAccountStatusAsync(5, AccountStatus.Active, actingAdminId: 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "UnsuspendUser", 5, null, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task SetAccountStatusAsync_WhenForbidden_LogsNoAudit()
    {
        var adminUser = new User { Id = 2, Role = UserRole.Admin };
        _userManagerMock.Setup(m => m.FindByIdAsync("2")).ReturnsAsync(adminUser);

        await _service.SetAccountStatusAsync(2, AccountStatus.Suspended, 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>()),
            Times.Never);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~UserManagementServiceTests"`
Expected: BUILD FAIL — `UserManagementService` has no 2-arg constructor.

- [ ] **Step 3: Wire the audit service into UserManagementService**

Replace the whole content of `src/MuktoAin.Application/Services/UserManagementService.cs` with:

```csharp
using Microsoft.AspNetCore.Identity;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Services;

// S-3.6 / FR-18: admin user management -- list users, suspend/activate accounts.
// Two methods wrapping UserManager (per Shads_plan Step 3.8): Identity handles
// account creation via registration; this covers only the admin-list and
// suspend/activate gap. Ceiling: list + status toggle.
// AUD-7: every successful status flip writes an AdminAuditLog row (fail-safe).
public class UserManagementService(UserManager<User> userManager, IAdminAuditService audit) : IUserManagementService
{
    public Task<IEnumerable<UserListDto>> GetAllUsersAsync()
    {
        var users = userManager.Users
            .OrderBy(u => u.Id)
            .Select(u => new UserListDto(
                u.Id,
                u.FullName,
                u.Email ?? string.Empty,
                u.Role.ToString(),
                u.AccountStatus.ToString()))
            .ToList();

        return Task.FromResult<IEnumerable<UserListDto>>(users);
    }

    public async Task<bool> SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId)
    {
        // Guard rails: admins are never suspendable, and an admin cannot flip
        // their own account status (prevents self-lockout).
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        if (user.Role == UserRole.Admin || user.Id == actingAdminId)
        {
            return false;
        }

        user.AccountStatus = status;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return false;
        }

        // On suspension, rotate the security stamp so already-issued auth cookies
        // are rejected on the next request instead of lingering for 8 hours.
        if (status == AccountStatus.Suspended)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        // AUD-7: record who changed what, on whom. Fail-safe — an audit failure
        // must not surface to the admin after the status change already succeeded.
        await audit.LogAdminActionAsync(
            actingAdminId,
            status == AccountStatus.Suspended ? "SuspendUser" : "UnsuspendUser",
            targetUserId: userId,
            details: $"AccountStatus -> {status}");

        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~UserManagementServiceTests"`
Expected: PASS (8 tests: 5 existing + 3 new).

- [ ] **Step 5: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

---

### Task 5: AUD-7 — Wire Audit into LawyerVerificationService (Verify/Reject)

**Files:**
- Modify: `src/MuktoAin.Application/Services/LawyerVerificationService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/LawyerVerificationServiceTests.cs`

**Interfaces:**
- Consumes: `IAdminAuditService` (Task 3).
- Produces: `LawyerVerificationService(IRepository<LawyerProfile> profileRepo, IAdminAuditService audit)` — new constructor. Call sites to update: `tests/MuktoAin.UnitTests/Services/LawyerVerificationServiceTests.cs:17` (DI auto-resolves `Program.cs:231`). Emits `"ApproveLawyerVerification"` / `"RejectLawyerVerification"` with `targetUserId = profile.UserId`, `targetEntityId = lawyerProfileId`, rejection `reason` in `Details`.

- [ ] **Step 1: Update the test constructor + add audit tests**

In `tests/MuktoAin.UnitTests/Services/LawyerVerificationServiceTests.cs`, change the fields/constructor (lines 12-18) to:

```csharp
    private readonly Mock<IRepository<LawyerProfile>> _profileRepo = new();
    private readonly Mock<IAdminAuditService> _auditMock = new();
    private readonly LawyerVerificationService _service;

    public LawyerVerificationServiceTests()
    {
        _service = new LawyerVerificationService(_profileRepo.Object, _auditMock.Object);
    }
```

Add at the end of the class:

```csharp
    // AUD-7: bar-verification decisions must record which admin decided, on
    // which profile, and the rejection reason.
    [Fact]
    public async Task VerifyAsync_Approve_LogsAuditWithProfileTarget()
    {
        var profile = new LawyerProfile { LawyerProfileId = 3, UserId = 42 };
        _profileRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(profile);

        await _service.VerifyAsync(3, adminUserId: 1, approve: true);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "ApproveLawyerVerification", 42, 3, null), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_Reject_LogsAuditWithReason()
    {
        var profile = new LawyerProfile { LawyerProfileId = 4, UserId = 42 };
        _profileRepo.Setup(r => r.GetByIdAsync(4)).ReturnsAsync(profile);

        await _service.VerifyAsync(4, adminUserId: 1, approve: false, reason: "Bar number not found");

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "RejectLawyerVerification", 42, 4, "Bar number not found"), Times.Once);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~LawyerVerificationServiceTests"`
Expected: BUILD FAIL — no 2-arg constructor.

- [ ] **Step 3: Wire the audit service in**

Replace the whole content of `src/MuktoAin.Application/Services/LawyerVerificationService.cs` with:

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class LawyerVerificationService
{
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly IAdminAuditService _audit;

    public LawyerVerificationService(IRepository<LawyerProfile> profileRepo, IAdminAuditService audit)
    {
        _profileRepo = profileRepo;
        _audit = audit;
    }

    public async Task<int> ApplyAsync(int userId, LawyerApplicationDto dto)
    {
        var existing = await _profileRepo.GetAllAsync();
        if (existing.Any(p => p.UserId == userId))
            throw new InvalidOperationException("Verification already submitted");

        var profile = new LawyerProfile
        {
            UserId = userId,
            BarRegistrationNumber = dto.BarRegistrationNumber,
            Specialization = dto.Specialization,
            VerificationStatus = VerificationStatus.Pending
        };

        await _profileRepo.AddAsync(profile);
        await _profileRepo.SaveChangesAsync();
        return profile.LawyerProfileId;
    }

    public async Task VerifyAsync(int lawyerProfileId, int adminUserId, bool approve, string? reason = null)
    {
        var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
        if (profile == null) throw new ArgumentException("Profile not found");

        profile.VerificationStatus = approve
            ? VerificationStatus.Approved
            : VerificationStatus.Rejected;
        profile.VerifiedByAdminId = adminUserId;
        profile.VerifiedAt = DateTime.UtcNow;
        profile.RejectionReason = approve ? null : (reason ?? string.Empty);

        await _profileRepo.SaveChangesAsync();

        // AUD-7: bar-verification decision is a human-in-the-loop safeguard
        // action — record which admin made it, on whom, and why (rejections).
        await _audit.LogAdminActionAsync(
            adminUserId,
            approve ? "ApproveLawyerVerification" : "RejectLawyerVerification",
            targetUserId: profile.UserId,
            targetEntityId: lawyerProfileId,
            details: approve ? null : reason);
    }

    public async Task<IEnumerable<LawyerProfile>> GetPendingApplicationsAsync()
    {
        var all = await _profileRepo.GetAllAsync();
        return all.Where(p => p.VerificationStatus == VerificationStatus.Pending);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~LawyerVerificationServiceTests"`
Expected: PASS (8 tests: 6 existing + 2 new).

- [ ] **Step 5: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

---

### Task 6: AUD-7 — Wire Audit into PaymentService (Refund / Mark-Paid)

**Files:**
- Modify: `src/MuktoAin.Application/Services/PaymentService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs`
- Modify: `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` (2 constructor call sites)

**Interfaces:**
- Consumes: `IAdminAuditService` (Task 3); the AUD-11 guards from Task 1.
- Produces: `PaymentService(IRepository<PaymentOrder> orderRepo, IRepository<PayoutRequest> payoutRepo, IRepository<LawyerProfile> lawyerRepo, ICaseRepository caseRepo, UserManager<User> userManager, IAdminAuditService auditService)` and overloads `MarkPaidAsync(int paymentOrderId, string gatewayRef, int? actingAdminId = null)` / `RefundAsync(int paymentOrderId, int? actingAdminId = null)` — the new parameter is OPTIONAL so all existing callers (AdminController, PaymentController) compile unchanged. Emits `"MarkOrderPaid"` / `"RefundOrder"` audit rows **only when `actingAdminId.HasValue`** (the sandbox PaymentController auto-mark flow passes nothing and correctly produces no admin-audit row).

- [ ] **Step 1: Update both test construction sites**

In `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs` add a field next to the other mocks (after line 23):

```csharp
    private readonly Mock<IAdminAuditService> _auditMock = new();
```

and change the constructor body (lines 26-30) to:

```csharp
    public PaymentServiceTests()
    {
        _service = new PaymentService(
            _orderRepo.Object, _payoutRepo.Object, _lawyerRepo.Object, _caseRepo.Object,
            NewUserManager(), _auditMock.Object);
    }
```

Add this test at the end of the class:

```csharp
    // AUD-7: the admin's mark-paid action is recorded with order + gateway ref.
    [Fact]
    public async Task MarkPaidAsync_WithActingAdmin_LogsAudit()
    {
        var order = new PaymentOrder { PaymentOrderId = 7, Status = PaymentStatus.Pending, Amount = 500m };
        _orderRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(order);

        await _service.MarkPaidAsync(7, "SBX-ABC123", actingAdminId: 1);

        _auditMock.Verify(a => a.LogAdminActionAsync(
            1, "MarkOrderPaid", null, 7, It.IsAny<string?>()), Times.Once);
    }
```

In `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs`, both `new PaymentService(` calls (lines 25-30 and 77-82) gain the same extra last argument. For example the first becomes:

```csharp
        var paymentService = new PaymentService(
            _orderRepo.Object,
            Mock.Of<IRepository<PayoutRequest>>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            Mock.Of<ICaseRepository>(),
            NewUserManager(),
            Mock.Of<IAdminAuditService>());
```

(the second, at line 77, keeps its `caseRepo.Object` argument and appends `Mock.Of<IAdminAuditService>()` identically).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"`
Expected: BUILD FAIL — no 6-arg constructor.

- [ ] **Step 3: Wire the audit service into PaymentService**

In `src/MuktoAin.Application/Services/PaymentService.cs`:

Add the field next to the others (after line 22):

```csharp
    private readonly IAdminAuditService _audit;
```

Change the constructor (lines 24-36) to:

```csharp
    public PaymentService(
        IRepository<PaymentOrder> orderRepo,
        IRepository<PayoutRequest> payoutRepo,
        IRepository<LawyerProfile> lawyerRepo,
        ICaseRepository caseRepo,
        UserManager<User> userManager,
        IAdminAuditService audit)
    {
        _orderRepo = orderRepo;
        _payoutRepo = payoutRepo;
        _lawyerRepo = lawyerRepo;
        _caseRepo = caseRepo;
        _userManager = userManager;
        _audit = audit;
    }
```

Replace `MarkPaidAsync` (the guarded version from Task 1) with:

```csharp
    // Sandbox "IPN confirmed" action. AUD-11 idempotency guard: an order that is
    // already Paid (or already Refunded) can never be re-processed — a duplicate
    // mark-paid directly inflates the lawyer's payable balance in
    // GetLawyerEarningsAsync (docs/PROJECT_AUDIT_REPORT.md, Admin Scope #2).
    // AUD-7: when an admin triggers this (AdminController.MarkOrderPaid), the
    // action is recorded; the sandbox citizen-side auto-mark passes no admin id
    // and correctly produces no admin-audit row.
    public async Task MarkPaidAsync(int paymentOrderId, string gatewayRef, int? actingAdminId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        if (o.Status is PaymentStatus.Paid or PaymentStatus.Refunded)
            throw new InvalidOperationException(
                $"Order {paymentOrderId} is already {o.Status} and cannot be marked Paid again.");
        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        if (actingAdminId.HasValue)
        {
            await _audit.LogAdminActionAsync(
                actingAdminId.Value, "MarkOrderPaid",
                targetEntityId: paymentOrderId,
                details: $"GatewayRef {gatewayRef} · {o.Amount:0.00} BDT");
        }
    }
```

Replace `RefundAsync` (the guarded version from Task 1) with:

```csharp
    // AUD-11: only a Paid order can be refunded — Pending/Failed/Refunded states
    // are rejected loudly so an admin sees why nothing happened.
    // AUD-7: admin-triggered refunds are recorded (ledger reversal noted).
    public async Task RefundAsync(int paymentOrderId, int? actingAdminId = null)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        if (o.Status != PaymentStatus.Paid)
            throw new InvalidOperationException(
                $"Order {paymentOrderId} is {o.Status}; only Paid orders can be refunded.");
        o.Status = PaymentStatus.Refunded;
        o.RefundedAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        if (o.CaseId.HasValue && o.Purpose == PaymentPurpose.Honorarium)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null)
            {
                c.HonorariumPaid = false; // ledger reversed
                await _caseRepo.SaveChangesAsync();
            }
        }

        if (actingAdminId.HasValue)
        {
            await _audit.LogAdminActionAsync(
                actingAdminId.Value, "RefundOrder",
                targetEntityId: paymentOrderId,
                details: $"{o.Amount:0.00} BDT ledger reversed");
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentServiceTests"`
Expected: PASS (6 tests: 5 from Task 1 + 1 new).

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~PaymentControllerTests"`
Expected: PASS (all existing tests — the two real-constructed `PaymentService` instances now compile with the 6-arg ctor).

- [ ] **Step 5: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

---

### Task 7: AUD-7 — AdminController Wiring + Real Audit Panel on the Dashboard

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (constructor, `DeleteScenario`, `RefundOrder`, `MarkOrderPaid`, `BuildAdminDashboardViewModelAsync` audit-stream block at lines 567-580)
- Modify: `tests/MuktoAin.UnitTests/Controllers/PaymentControllerTests.cs` — NO changes needed (it never constructs `AdminController`; verified by grep)

**Interfaces:**
- Consumes: `IAdminAuditService` (Task 3), `AppDbContext.AdminAuditLogs` (Task 2), guarded `MarkPaidAsync(orderId, gatewayRef, actingAdminId)` / `RefundAsync(orderId, actingAdminId)` (Task 6).
- Produces: dashboard `AdminDashboardViewModel.AuditLogs` populated from the latest 10 `AdminAuditLog` rows (same `SystemAuditLogItemViewModel` shape, so `Views/Admin/Dashboard.cshtml` needs **no changes**).

- [ ] **Step 1: Inject IAdminAuditService into AdminController**

In `src/MuktoAin.Web/Controllers/AdminController.cs` add the field (after line 33, `private readonly GeminiClient _geminiClient;`):

```csharp
    private readonly IAdminAuditService _audit;
```

Add the parameter to the constructor signature (after `GeminiClient geminiClient`):

```csharp
        PaymentService paymentService,
        GeminiClient geminiClient,
        IAdminAuditService audit)
```

and the assignment (after `_geminiClient = geminiClient;`):

```csharp
        _audit = audit;
```

(`AdminControllerTests` never constructs `AdminController`, so no test constructor updates are needed.)

- [ ] **Step 2: Log scenario deletion (Admin Scope #5/#6 — hard delete, previously unrecorded)**

Replace `DeleteScenario` (lines 358-371) with:

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScenario(int mappingId)
    {
        var all = await _scenarioRepo.GetAllAsync();
        var m = all.FirstOrDefault(x => x.MappingId == mappingId);
        if (m != null)
        {
            await _scenarioRepo.DeleteAsync(m);
            await _scenarioRepo.SaveChangesAsync();

            // AUD-7: DeleteScenario is a HARD delete with no soft-delete flag —
            // the audit row is the only surviving record of what was removed.
            var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                out var id) ? id : 0;
            await _audit.LogAdminActionAsync(
                adminId, "DeleteScenario",
                targetEntityId: mappingId,
                details: $"Keyword '{m.ScenarioKeyword}' (SectionId {m.SectionId}) hard-deleted.");
        }
        TempData["Success"] = "Mapping deleted.";
        return RedirectToAction(nameof(Scenarios));
    }
```

- [ ] **Step 3: Pass the acting admin id into the payment actions**

Replace the `RefundOrder` and `MarkOrderPaid` bodies (the versions from Task 1) so the admin id flows into the service:

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundOrder(int orderId)
    {
        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        try
        {
            await _paymentService.RefundAsync(orderId, adminId);
            TempData["Success"] = "Order refunded (sandbox) — ledger reversed.";
        }
        catch (InvalidOperationException ex)
        {
            // AUD-11: double-refund / refund-of-unpaid attempt — show why the
            // order was untouched instead of 500-ing.
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Transactions));
    }
```

```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrderPaid(int orderId)
    {
        // Sandbox gateway confirm (in lieu of real SSLCommerz IPN)
        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        try
        {
            await _paymentService.MarkPaidAsync(orderId, $"SBX-{Guid.NewGuid().ToString("N")[..12].ToUpper()}", adminId);
            TempData["Success"] = "Order marked Paid (sandbox gateway).";
        }
        catch (InvalidOperationException ex)
        {
            // AUD-11: double mark-paid attempt — the order was left untouched.
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Transactions));
    }
```

- [ ] **Step 4: Replace the fake AI_LOG audit panel with real AdminAuditLog rows**

In `BuildAdminDashboardViewModelAsync`, replace the audit-stream block (lines 567-580, from the comment `// Audit stream = latest AI_LOG + review events...` through the closing `.ToList();` of the `model.AuditLogs = ...` assignment) with:

```csharp
            // AUD-7: real administrative audit trail. The previous "audit" panel
            // was fed from AI_LOG rows (AI calls, not admin actions — audit
            // report Admin Scope #5). Latest 10 ADMIN_AUDIT_LOG rows; actor
            // names resolved from the users list already loaded above.
            var auditEntries = await _dbContext.AdminAuditLogs
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .ToListAsync();
            model.AuditLogs = auditEntries
                .Select(l => new SystemAuditLogItemViewModel
                {
                    Timestamp = l.CreatedAt.ToString("HH:mm"),
                    Action = l.Action,
                    Actor = users.FirstOrDefault(u => u.Id == l.AdminUserId)?.FullName
                            ?? $"Admin #{l.AdminUserId}",
                    Status = "Success",
                    Details = string.Join(" · ", new[] {
                            l.Details,
                            l.TargetUserId.HasValue ? $"User #{l.TargetUserId}" : null,
                            l.TargetEntityId.HasValue ? $"Entity #{l.TargetEntityId}" : null
                        }.Where(p => !string.IsNullOrWhiteSpace(p)))
                })
                .ToList();
```

(`aiLogsToday` is still consumed above this block by `model.AiCallsToday` / `model.AiFailureRate` — those keep working. The `catch` block at line 582-586 already resets `model.AuditLogs` to an empty list on failure; the dashboard-log-severity fix there is AUD-12, owned by Shads — do NOT touch it in this task.)

- [ ] **Step 5: Build the solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run the affected test classes**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminControllerTests|FullyQualifiedName~PaymentControllerTests|FullyQualifiedName~UserManagementServiceTests"`
Expected: PASS.

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Change `- [ ] **[AUD-7]**` to `- [x]` and wrap that entire line in `~~...~~` strikethrough, appending a one-line summary (entity + fail-safe service + SQL script 14 + wiring into user/lawyer/payment/scenario-delete flows + real dashboard audit panel).

---

### Task 8: AUD-8 — Pagination on LawyerReviewService.GetQueueAsync (+ LawyerController.Queue + Queue.cshtml)

**Files:**
- Modify: `src/MuktoAin.Application/DTOs/LawyerReviewDto.cs` (append one record)
- Modify: `src/MuktoAin.Application/Services/LawyerReviewService.cs:56-97` (`GetQueueAsync`)
- Modify: `src/MuktoAin.Web/Controllers/LawyerController.cs:91-125` (`Queue`)
- Modify: `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs:148-156` (`LawyerQueueViewModel`)
- Modify: `src/MuktoAin.Web/Views/Lawyer/Queue.cshtml`
- Test: `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs`

**Interfaces:**
- Consumes: existing `QueueItemDto` (unchanged).
- Produces: `QueuePageDto(int TotalCount, IReadOnlyList<QueueItemDto> Items)`; `LawyerReviewService.GetQueueAsync(int? lawyerProfileId = null, string? filter = "All", int page = 1, int pageSize = 20)` returning `Task<QueuePageDto>` — **signature change**; its only caller is `LawyerController.Queue` (verified by grep), updated in Step 4.

- [ ] **Step 1: Write the failing paging tests**

In `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs`, add at the end of the class:

```csharp
    // AUD-8: queue paging — TotalCount reflects the FULL filtered pool while
    // Items are page-sliced BEFORE the expensive per-document enrichment loop
    // (each enriched item costs a case fetch + category + district lookups).
    private void SetUpQueueOfDocs(int count)
    {
        var docs = new List<GeneratedDocument>();
        for (var i = 1; i <= count; i++)
        {
            docs.Add(new GeneratedDocument
            {
                DocumentId = i,
                CaseId = 100 + i,
                Status = DocumentStatus.UnderReview,
                CreatedAt = new DateTime(2026, 9, 1).AddHours(i)
            });
        }
        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(docs);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(It.IsAny<int>()))
            .ReturnsAsync((int caseId) => new Case
            {
                CaseId = caseId, Title = $"Case {caseId}", CategoryId = 1, DistrictId = 1
            });
        _categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new CaseCategory { CategoryId = 1, Name = "Labour" });
        _districtRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new District { DistrictId = 1, Name = "Dhaka" });
    }

    [Fact]
    public async Task GetQueueAsync_Paging_ReturnsTotalCountAndCorrectPageSlice()
    {
        SetUpQueueOfDocs(25);

        var result = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "All", page: 2, pageSize: 20);

        Assert.Equal(25, result.TotalCount);      // full pool, not the page size
        Assert.Equal(5, result.Items.Count);      // items 21-25
        Assert.Equal(21, result.Items[0].DocumentId); // oldest-first ordering preserved
        Assert.Equal(25, result.Items[4].DocumentId);
    }

    [Fact]
    public async Task GetQueueAsync_Paging_TotalCountRespectsFilter()
    {
        SetUpQueueOfDocs(25);
        // Claim half of the documents so the "Unclaimed" filter sees only 12.
        var claimed = new List<GeneratedDocument>();
        for (var i = 1; i <= 25; i++)
        {
            var doc = new GeneratedDocument
            {
                DocumentId = i, CaseId = 100 + i,
                Status = DocumentStatus.UnderReview,
                CreatedAt = new DateTime(2026, 9, 1).AddHours(i)
            };
            if (i % 2 == 0) doc.AssignedLawyerProfileId = 9; // claimed by someone
            claimed.Add(doc);
        }
        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(claimed);

        var result = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "Unclaimed", page: 1, pageSize: 20);

        Assert.Equal(12, result.TotalCount); // only odd-numbered docs are unclaimed
        Assert.Equal(12, result.Items.Count);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~LawyerReviewServiceTests"`
Expected: BUILD FAIL — `GetQueueAsync` returns `IReadOnlyList<QueueItemDto>`, and `QueuePageDto` does not exist.

- [ ] **Step 3: Add QueuePageDto + implement paging in GetQueueAsync**

Append to `src/MuktoAin.Application/DTOs/LawyerReviewDto.cs` (after `QueueItemDto`, before `ReviewWorkspaceDto`):

```csharp
// AUD-8: queue paging envelope — TotalCount is the FULL filtered pool size
// (for the pager), Items is the current page slice only.
public record QueuePageDto(
    int TotalCount,
    IReadOnlyList<QueueItemDto> Items
);
```

Replace `GetQueueAsync` in `src/MuktoAin.Application/Services/LawyerReviewService.cs` (lines 53-97) with:

```csharp
    // Queue = documents in UnderReview, oldest-first (SLA age shown by the view).
    // filter: "All" (default) | "Unclaimed" | "Mine". CanOpen allows re-entry
    // into the lawyer's OWN claimed doc (ClaimAsync auto-allows same lawyer).
    // AUD-8: paged — the page slice is taken BEFORE the expensive per-document
    // enrichment loop so a large backlog enriches only the visible page.
    public async Task<QueuePageDto> GetQueueAsync(
        int? lawyerProfileId = null, string? filter = "All", int page = 1, int pageSize = 20)
    {
        var docs = (await _docRepo.GetAllAsync())
            .Where(d => d.Status == DocumentStatus.UnderReview)
            .AsEnumerable();

        if (filter == "Unclaimed")
            docs = docs.Where(d => !d.AssignedLawyerProfileId.HasValue);
        else if (filter == "Mine" && lawyerProfileId.HasValue)
            docs = docs.Where(d => d.AssignedLawyerProfileId == lawyerProfileId.Value);

        var ordered = docs.OrderBy(d => d.CreatedAt).ToList();
        var totalCount = ordered.Count;

        var result = new List<QueueItemDto>();
        foreach (var d in ordered.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var c = await _caseRepo.GetWithDocumentsAsync(d.CaseId);
            if (c == null) continue;
            var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
            var district = await _districtRepo.GetByIdAsync(c.DistrictId);
            string? claimedBy = null;
            if (d.AssignedLawyerProfileId.HasValue)
            {
                var p = await _profileRepo.GetByIdAsync(d.AssignedLawyerProfileId.Value);
                claimedBy = p?.BarRegistrationNumber; // admin-safe identifier
            }
            result.Add(new QueueItemDto(
                d.DocumentId,
                d.CaseId,
                SafeDecrypt(c.Title),
                category?.Name ?? "",
                district?.Name ?? "",
                d.Status,
                d.CitizenEdited,
                d.VersionNo,
                claimedBy,
                d.CreatedAt,
                d.ClaimedAt,
                CanOpen: !d.AssignedLawyerProfileId.HasValue
                      || d.AssignedLawyerProfileId == lawyerProfileId));
        }
        return new QueuePageDto(totalCount, result);
    }
```

- [ ] **Step 4: Update LawyerController.Queue to use the paged envelope**

In `src/MuktoAin.Web/Controllers/LawyerController.cs`, add a page-size constant next to `private const int HistoryPageSize = 20;` (line 207) — place it just ABOVE the `Queue` action instead (after line 90):

```csharp
    private const int QueuePageSize = 20;
```

Replace the `Queue` action (lines 93-125) with:

```csharp
    // Queue: documents in the pool, oldest-first (SLA). Filter chips:
    // All (default) · Unclaimed · Mine (my claimed docs, re-enterable).
    [HttpGet]
    public async Task<IActionResult> Queue(string? filter, int page = 1)
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        var queue = await _reviewService.GetQueueAsync(profile.LawyerProfileId, filter, page, QueuePageSize);

        var totalPages = Math.Max((int)Math.Ceiling(queue.TotalCount / (double)QueuePageSize), 1);
        var vm = new LawyerQueueViewModel
        {
            LawyerName = (await _userManager.FindByIdAsync(profile.UserId.ToString()))?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            Specialization = profile.Specialization ?? "",
            PendingCount = queue.TotalCount, // KPI shows the full backlog, not the page
            ActiveFilter = filter ?? "All",
            Page = Math.Max(1, Math.Min(page, totalPages)),
            PageSize = QueuePageSize,
            TotalCount = queue.TotalCount,
            Items = queue.Items.Select(q => new LawyerQueueItemViewModel
            {
                DocumentId = q.DocumentId,
                CaseId = q.CaseId,
                CaseTitle = q.CaseTitle,
                CategoryName = q.CategoryName,
                DistrictName = q.DistrictName,
                CitizenEdited = q.CitizenEdited,
                VersionNo = q.VersionNo,
                ClaimedBy = q.ClaimedBy,
                IsMine = profile.LawyerProfileId != 0 && q.ClaimedBy == profile.BarRegistrationNumber,
                WaitingHours = (int)Math.Max(0, (DateTime.UtcNow - q.CreatedAt).TotalHours),
                CanOpen = q.CanOpen
            }).ToList()
        };
        return View(vm);
    }
```

- [ ] **Step 5: Add paging fields to LawyerQueueViewModel**

In `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs`, change `LawyerQueueViewModel` (lines 148-156) to:

```csharp
public class LawyerQueueViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public string ActiveFilter { get; set; } = "All";

    // AUD-8: pagination (mirrors LawyerHistoryViewModel)
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }

    public List<LawyerQueueItemViewModel> Items { get; set; } = new();
}
```

- [ ] **Step 6: Add page controls to Queue.cshtml**

In `src/MuktoAin.Web/Views/Lawyer/Queue.cshtml`, extend the `@{ ... }` block at the top (lines 3-5) to:

```cshtml
@{
    ViewData["Title"] = "রিভিউ কিউ — মুক্ত আইন";

    // Display-only Bengali numeral rendering, same convention as
    // Lawyer/History.cshtml's pagination.
    static string ToBn(int n) => n.ToString()
        .Select(c => char.IsDigit(c) ? (char)(c - '0' + '০') : c)
        .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c))
        .ToString();

    var totalPages = Model.PageSize > 0
        ? (int)Math.Ceiling(Model.TotalCount / (double)Model.PageSize)
        : 1;
}
```

Then, inside the `else { ... }` block (the table card), insert the pagination block immediately BEFORE the closing `</div>` of the card (i.e., after the `<p class="muted tiny" ...>` note at line 119):

```cshtml
            @if (totalPages > 1)
            {
                <div class="pagination" style="margin-top: 12px">
                    @if (Model.Page > 1)
                    {
                        <a asp-action="Queue" asp-route-filter="@Model.ActiveFilter" asp-route-page="@(Model.Page - 1)"><i data-lucide="chevron-left"></i></a>
                    }
                    @for (var p = 1; p <= totalPages; p++)
                    {
                        @if (p == Model.Page)
                        {
                            <button type="button" class="active" data-page="@p">@ToBn(p)</button>
                        }
                        else
                        {
                            <a asp-action="Queue" asp-route-filter="@Model.ActiveFilter" asp-route-page="@p">@ToBn(p)</a>
                        }
                    }
                    @if (Model.Page < totalPages)
                    {
                        <a asp-action="Queue" asp-route-filter="@Model.ActiveFilter" asp-route-page="@(Model.Page + 1)"><i data-lucide="chevron-right"></i></a>
                    }
                </div>
            }
```

- [ ] **Step 7: Run tests + build**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~LawyerReviewServiceTests"` — Expected: PASS (including the two new paging tests).
Run: `dotnet build MuktoAin.slnx` — Expected: Build succeeded, 0 errors.

---

### Task 9: AUD-8 — Pagination on Admin Users (+ Users.cshtml page controls)

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs:175-195` (`Users`)
- Modify: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs:3-7` (`AdminUsersViewModel`)
- Modify: `src/MuktoAin.Web/Views/Admin/Users.cshtml`

**Interfaces:**
- Consumes: `IUserManagementService.GetAllUsersAsync()` (unchanged — filtering and paging happen controller-side, exactly like `LawyerController.History` does with `GetHistoryAsync`).
- Produces: `AdminUsersViewModel` gains `int Page`, `int PageSize`, `int TotalCount`; `Users(string? role, int page = 1)` action signature.

- [ ] **Step 1: Add paging fields to AdminUsersViewModel**

In `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`, change `AdminUsersViewModel` (lines 3-7) to:

```csharp
public class AdminUsersViewModel
{
    public List<AdminUserRowViewModel> Users { get; set; } = new();
    public string RoleFilter { get; set; } = "All";

    // AUD-8: pagination (the controller returns every user unfiltered today,
    // which does not scale past a few hundred rows — audit report Admin #4).
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
}
```

- [ ] **Step 2: Add paging to the Users action**

In `src/MuktoAin.Web/Controllers/AdminController.cs`, add a page-size constant directly above the `Users` action (before line 175):

```csharp
    private const int AdminListPageSize = 20;
```

Replace the `Users` action (lines 175-195) with:

```csharp
    [HttpGet]
    public async Task<IActionResult> Users(string? role, int page = 1)
    {
        var all = await _userManagement.GetAllUsersAsync();
        var filtered = (string.IsNullOrWhiteSpace(role) || role == "All"
            ? all
            : all.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase))).ToList();

        var totalPages = Math.Max((int)Math.Ceiling(filtered.Count / (double)AdminListPageSize), 1);
        var vm = new AdminUsersViewModel
        {
            RoleFilter = role ?? "All",
            Page = Math.Max(1, Math.Min(page, totalPages)),
            PageSize = AdminListPageSize,
            TotalCount = filtered.Count,
            Users = filtered
                .Skip((Math.Max(1, Math.Min(page, totalPages)) - 1) * AdminListPageSize)
                .Take(AdminListPageSize)
                .Select(u => new AdminUserRowViewModel
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    Status = u.Status
                }).ToList()
        };
        return View(vm);
    }
```

- [ ] **Step 3: Add page controls to Users.cshtml**

In `src/MuktoAin.Web/Views/Admin/Users.cshtml`, extend the top `@{ ... }` block (lines 2-5) to:

```cshtml
@{
    ViewData["Title"] = "Users — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;

    var totalPages = Model.PageSize > 0
        ? (int)Math.Ceiling(Model.TotalCount / (double)Model.PageSize)
        : 1;
}
```

Then insert the pagination block immediately AFTER the closing `</table>` (line 71) and BEFORE the card's closing `</div>` (line 72):

```cshtml
            @if (totalPages > 1)
            {
                <div class="pagination" style="margin-top: 12px">
                    @if (Model.Page > 1)
                    {
                        <a asp-action="Users" asp-route-role="@Model.RoleFilter" asp-route-page="@(Model.Page - 1)"><i data-lucide="chevron-left"></i></a>
                    }
                    @for (var p = 1; p <= totalPages; p++)
                    {
                        @if (p == Model.Page)
                        {
                            <button type="button" class="active" data-page="@p">@p</button>
                        }
                        else
                        {
                            <a asp-action="Users" asp-route-role="@Model.RoleFilter" asp-route-page="@p">@p</a>
                        }
                    }
                    @if (Model.Page < totalPages)
                    {
                        <a asp-action="Users" asp-route-role="@Model.RoleFilter" asp-route-page="@(Model.Page + 1)"><i data-lucide="chevron-right"></i></a>
                    }
                </div>
            }
```

(Admin views use plain numerals — no `ToBn` helper, matching Users.cshtml's English-only style.)

- [ ] **Step 4: Build + run AdminControllerTests**

Run: `dotnet build MuktoAin.slnx` — Expected: Build succeeded, 0 errors.
Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminControllerTests"` — Expected: PASS.

---

### Task 10: AUD-8 — Pagination on Admin AiLogs (replace hardcoded Take(200))

*(completes the AUD-8 audit item together with Task 9, but split into its own reviewable unit)*

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs:400-440` (`AiLogs`)
- Modify: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs:86-91` (`AdminAiLogsViewModel`)
- Modify: `src/MuktoAin.Web/Views/Admin/AiLogs.cshtml`

**Interfaces:**
- Consumes: `IRepository<AiLog>` (`GetAllAsync()`).
- Produces: `AiLogs(string? type, int minLatency = 0, int page = 1)`; `AdminAiLogsViewModel` gains `int Page`, `int PageSize`, `int TotalCount`. Semantics preserved: `CallsToday` counts UNFILTERED rows for today (as before); the type/minLatency filters now apply to the FULL log set before paging (fixes a latent bug — the old code took the newest 200 rows BEFORE filtering).

- [ ] **Step 1: Add paging fields to AdminAiLogsViewModel**

In `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`, change `AdminAiLogsViewModel` (lines 86-91) to:

```csharp
public class AdminAiLogsViewModel
{
    public List<AdminAiLogRowViewModel> Logs { get; set; } = new();
    public int CallsToday { get; set; }
    public double FailureRateToday { get; set; }

    // AUD-8: the controller hardcoded Take(200) — older rows were simply
    // unreachable from the UI. Page size 50 keeps the prompt-inspector cheap.
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
}
```

- [ ] **Step 2: Rewrite the AiLogs action with real paging**

In `src/MuktoAin.Web/Controllers/AdminController.cs`, replace the `AiLogs` action (lines 402-440) with:

```csharp
    [HttpGet]
    public async Task<IActionResult> AiLogs(string? type, int minLatency = 0, int page = 1)
    {
        var all = (await _aiLogRepo.GetAllAsync()).ToList();

        // "Calls today" is a global KPI — intentionally counted BEFORE the
        // type/latency filters (same semantics as the pre-AUD-8 action).
        var today = DateTime.UtcNow.Date;
        var callsToday = all.Count(l => l.CreatedAt >= today);

        // AUD-8: filter the FULL set (the old code took the newest 200 rows
        // BEFORE filtering, so filters silently ignored older matches), then
        // page. No hardcoded Take(200) — older entries are reachable again.
        IEnumerable<AiLog> filtered = all.OrderByDescending(l => l.CreatedAt);
        if (!string.IsNullOrWhiteSpace(type) && type != "All"
            && Enum.TryParse<Domain.Enums.AiRequestType>(type, out var t))
        {
            filtered = filtered.Where(l => l.RequestType == t);
        }
        if (minLatency > 0)
        {
            filtered = filtered.Where(l => l.LatencyMs >= minLatency);
        }
        var filteredList = filtered.ToList();

        var totalPages = Math.Max((int)Math.Ceiling(filteredList.Count / (double)AdminAiLogsPageSize), 1);
        var currentPage = Math.Max(1, Math.Min(page, totalPages));

        var vm = new AdminAiLogsViewModel
        {
            CallsToday = callsToday,
            FailureRateToday = 0, // failure detection = latency outliers; see view
            Page = currentPage,
            PageSize = AdminAiLogsPageSize,
            TotalCount = filteredList.Count,
            Logs = filteredList
                .Skip((currentPage - 1) * AdminAiLogsPageSize)
                .Take(AdminAiLogsPageSize)
                .Select(l => new AdminAiLogRowViewModel
                {
                    LogId = l.LogId,
                    Time = l.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Type = l.RequestType.ToString(),
                    Model = l.ModelUsed,
                    Tokens = l.TokensUsed,
                    LatencyMs = l.LatencyMs,
                    CaseId = l.CaseId,
                    PromptPreview = l.PromptText.Length > 200 ? l.PromptText[..200] + "…" : l.PromptText,
                    ResponsePreview = l.ResponseText.Length > 200 ? l.ResponseText[..200] + "…" : l.ResponseText
                }).ToList()
        };
        return View(vm);
    }
```

And add the page-size constant next to `AdminListPageSize` (Task 9, Step 2):```csharp
    private const int AdminAiLogsPageSize = 50;
```

- [ ] **Step 3: Add page controls to AiLogs.cshtml**

In `src/MuktoAin.Web/Views/Admin/AiLogs.cshtml`, extend the top `@{ ... }` block (lines 2-5) to:

```cshtml
@{
    ViewData["Title"] = "AI Logs — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;

    var totalPages = Model.PageSize > 0
        ? (int)Math.Ceiling(Model.TotalCount / (double)Model.PageSize)
        : 1;
}
```

Also fix the filter form (line 27) so both filters + page survive submission — it already posts `type`/`minLatency` via GET; page resets to 1 on a new filter naturally (no `page` input needed on the form).

Insert the pagination block immediately AFTER the table's closing `</table>` (line 79) and BEFORE the card's closing `</div>` (line 80):

```cshtml
            @if (totalPages > 1)
            {
                <div class="pagination" style="margin-top: 12px">
                    @if (Model.Page > 1)
                    {
                        <a asp-action="AiLogs" asp-route-type="@Context.Request.Query["type"]" asp-route-minLatency="@Context.Request.Query["minLatency"]" asp-route-page="@(Model.Page - 1)"><i data-lucide="chevron-left"></i></a>
                    }
                    @for (var p = 1; p <= totalPages; p++)
                    {
                        @if (p == Model.Page)
                        {
                            <button type="button" class="active" data-page="@p">@p</button>
                        }
                        else
                        {
                            <a asp-action="AiLogs" asp-route-type="@Context.Request.Query["type"]" asp-route-minLatency="@Context.Request.Query["minLatency"]" asp-route-page="@p">@p</a>
                        }
                    }
                    @if (Model.Page < totalPages)
                    {
                        <a asp-action="AiLogs" asp-route-type="@Context.Request.Query["type"]" asp-route-minLatency="@Context.Request.Query["minLatency"]" asp-route-page="@(Model.Page + 1)"><i data-lucide="chevron-right"></i></a>
                    }
                </div>
            }
```

- [ ] **Step 4: Build + full test suite**

Run: `dotnet build MuktoAin.slnx` — Expected: Build succeeded, 0 errors.
Run: `dotnet test tests/MuktoAin.UnitTests` — Expected: all PASS (0 failures).

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Change `- [ ] **[AUD-8]**` to `- [x]` and wrap that entire line in `~~...~~` strikethrough, appending a one-line summary (queue paging via `QueuePageDto` + Admin Users/AiLogs paging + Razor page controls).

---

### Task 11: Full Verification + Manual SSMS Script Run Note

**Files:**
- Verify only — no source changes.
- Reference: `scripts/14_add_admin_audit_log.sql` (must be run manually in SSMS by Shads — record this in the completion note).

**Interfaces:**
- Consumes: everything from Tasks 1-10.
- Produces: verified build + green test suite; updated `plans/Dependency_plan.md`.

- [ ] **Step 1: Full clean build**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors, 0 warnings under default settings.

- [ ] **Step 2: Full unit test suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: all tests PASS (pre-existing ~255+ plus the new ones from Tasks 1, 3, 4, 5, 6, 8: 4 + 2 + 3 + 2 + 1 + 2 = 14 new tests minimum).

- [ ] **Step 3: Verify no test construction site was missed**

Run: `rg "new UserManagementService\(|new LawyerVerificationService\(|new PaymentService\(" tests src --type cs` (or use the Grep tool).
Expected: every hit compiles — if the build passed, this is satisfied; document any hit count in the completion note.

- [ ] **Step 4: Confirm the manual DB step is recorded**

Verify `scripts/14_add_admin_audit_log.sql` exists and is idempotent (re-runnable). Note for Shads (do NOT execute SSMS yourself): the script must be run against the MuktoAin database before the Admin Dashboard's audit panel will populate — until then the panel renders empty (graceful, no error: `model.AuditLogs` is an empty list).

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Confirm all three checkboxes (`AUD-7`, `AUD-8`, `AUD-11`) are flipped to `[x]` with strikethrough summaries (done in Tasks 1, 7, and 10 respectively). If any is still unchecked, complete its recording step now. Do NOT commit — Shads is the sole committer (AGENTS.md §6).
