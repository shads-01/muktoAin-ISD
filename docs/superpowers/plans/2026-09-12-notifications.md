# In-App Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let citizens, lawyers, and admins see in-app notifications for five events (case submitted, document decided, lawyer verified, payment received, new lawyer application) via a polled bell icon and a paginated list page.

**Architecture:** One new `Notification` table (EF entity + SQL script, following this repo's existing "SSMS script + `IEntityTypeConfiguration<T>`" convention) backs a `NotificationService` in the Application layer. Five existing service methods each gain a one-line call to it. A new `NotificationController` exposes JSON polling + a paginated view, using the same `[data-pop]`/`.menu-pop` dropdown mechanism `_Layout.cshtml` already has for the user-avatar menu.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core (SQL Server), xUnit + Moq, Razor Views, vanilla JS (no new libraries).

**Spec:** `docs/superpowers/specs/2026-09-12-notifications-design.md`

## Global Constraints

- No EF migrations — schema changes ship as a numbered idempotent script in `scripts/` (`IF OBJECT_ID(...) IS NULL` / `IF NOT EXISTS` guards), matching every existing script in that folder.
- Never add `Co-Authored-By` or any AI-attribution trailer to commit messages (project CLAUDE.md).
- Do not commit, stage, or push — leave all changes in the working tree (project CLAUDE.md §6; Shads is the sole committer). Every "Commit" step below is written as `git add`/`git commit` for a human to run later, not something to execute automatically.
- Bangla/English pairs for all user-facing text, following the `StatusText`/`data-bn`/`data-en` pattern already used across the app.
- `NotifyAsync`/`NotifyAllAdminsAsync` must never throw out of a caller's business operation (case submission, review, verification, payment) — always caught and logged internally.
- Every read of another user's data by id (`MarkRead`, `Index`, `Unread`) must be scoped to the caller's own `userId` in the query itself, never "fetch then filter."
- After finishing all tasks, update `plans/Dependency_plan.md` per this project's mandatory tracking rule (add a new redesign-wave entry, checked off, describing what was built) — this is itself the final task below.

---

## Task 1: Domain — `NotificationType` enum and `Notification` entity

**Files:**
- Create: `src/MuktoAin.Domain/Enums/NotificationType.cs`
- Create: `src/MuktoAin.Domain/Entities/Notification.cs`

**Interfaces:**
- Produces: `NotificationType` enum (`CaseSubmitted=0, DocumentDecided=1, LawyerVerified=2, PaymentReceived=3, NewLawyerApplication=4`) and `Notification` entity with properties `NotificationId, UserId, Type, RelatedCaseId, RelatedDocumentId, RelatedLawyerProfileId, IsRead, CreatedAt` — every later task in this plan depends on these exact names.

- [ ] **Step 1: Create the enum**

```csharp
namespace MuktoAin.Domain.Enums;

public enum NotificationType
{
    CaseSubmitted = 0,
    DocumentDecided = 1,
    LawyerVerified = 2,
    PaymentReceived = 3,
    NewLawyerApplication = 4
}
```

- [ ] **Step 2: Create the entity**

```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// One row per (user, event). Carries only ids + type — bilingual text is
// rendered at read time by NotificationTextFormatter, never stored, so a
// future new UI language needs no data migration.
public class Notification
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public NotificationType Type { get; set; }

    public int? RelatedCaseId { get; set; }
    public Case? RelatedCase { get; set; }

    public int? RelatedDocumentId { get; set; }
    public GeneratedDocument? RelatedDocument { get; set; }

    public int? RelatedLawyerProfileId { get; set; }
    public LawyerProfile? RelatedLawyerProfile { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 3: Build to confirm no errors**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/MuktoAin.Domain/Enums/NotificationType.cs src/MuktoAin.Domain/Entities/Notification.cs
git commit -m "feat(domain): add Notification entity and NotificationType enum"
```

---

## Task 2: Infrastructure — EF configuration, DbSet, and SQL script

**Files:**
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/NotificationConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`
- Create: `scripts/11_notifications_table.sql`

**Interfaces:**
- Consumes: `Notification` entity (Task 1).
- Produces: `AppDbContext.Notifications` (`DbSet<Notification>`), table `[dbo].[NOTIFICATION]` — Task 3's repository access and every later task's tests rely on this `DbSet` name.

- [ ] **Step 1: Write the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("NOTIFICATION", "dbo");
        builder.HasKey(n => n.NotificationId);
        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.HasIndex(n => n.CreatedAt);

        builder.HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.RelatedCase)
            .WithMany()
            .HasForeignKey(n => n.RelatedCaseId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(n => n.RelatedDocument)
            .WithMany()
            .HasForeignKey(n => n.RelatedDocumentId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(n => n.RelatedLawyerProfile)
            .WithMany()
            .HasForeignKey(n => n.RelatedLawyerProfileId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
```

- [ ] **Step 2: Add the DbSet to AppDbContext**

In `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`, immediately after the existing `public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();` line, add:

```csharp

    // 2026-09-12 (scripts/11_notifications_table.sql)
    public DbSet<Notification> Notifications => Set<Notification>();
```

- [ ] **Step 3: Write the SQL script**

```sql
/* ============================================================
   MuktoAin — Notifications table (2026-09-12)
   IDEMPOTENT: safe to re-run; the CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[NOTIFICATION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NOTIFICATION] (
        NotificationId        INT IDENTITY(1,1) NOT NULL,
        UserId                INT                NOT NULL,
        Type                  INT                NOT NULL,
        RelatedCaseId         INT                NULL,
        RelatedDocumentId     INT                NULL,
        RelatedLawyerProfileId INT               NULL,
        IsRead                BIT                NOT NULL DEFAULT (0),
        CreatedAt             DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_NOTIFICATION PRIMARY KEY (NotificationId),
        CONSTRAINT FK_NOTIFICATION_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId) ON DELETE CASCADE,
        CONSTRAINT FK_NOTIFICATION_Case FOREIGN KEY (RelatedCaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_NOTIFICATION_Document FOREIGN KEY (RelatedDocumentId)
            REFERENCES [dbo].[GENERATED_DOCUMENT] (DocumentId),
        CONSTRAINT FK_NOTIFICATION_LawyerProfile FOREIGN KEY (RelatedLawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
    CREATE INDEX IX_NOTIFICATION_User_IsRead ON [dbo].[NOTIFICATION] (UserId, IsRead);
    CREATE INDEX IX_NOTIFICATION_CreatedAt ON [dbo].[NOTIFICATION] (CreatedAt);
END
GO
```

- [ ] **Step 4: Build to confirm the configuration compiles**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run the script against the local database**

Run the script in SSMS (or `sqlcmd -S <server> -d MuktoAin -i scripts/11_notifications_table.sql`) against your local dev database. This plan's later tests use EF's in-memory/mocked repositories and do not require the real table, but the app itself will throw at runtime against a real DB without it.

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Infrastructure/Data/Configurations/NotificationConfiguration.cs src/MuktoAin.Infrastructure/Data/AppDbContext.cs scripts/11_notifications_table.sql
git commit -m "feat(db): add NOTIFICATION table and EF configuration"
```

---

## Task 3: Application — `NotificationDto`, `NotificationTextFormatter`

**Files:**
- Create: `src/MuktoAin.Application/DTOs/NotificationDto.cs`
- Create: `src/MuktoAin.Web/Controllers/NotificationTextFormatter.cs`
- Test: `tests/MuktoAin.UnitTests/Controllers/NotificationTextFormatterTests.cs`

**Interfaces:**
- Consumes: `Notification`, `NotificationType` (Task 1).
- Produces: `NotificationDto` record and `NotificationTextFormatter.Format(NotificationDto dto) -> (string TextBn, string TextEn, string Url)` — Task 4's `NotificationService.GetRecentAsync`/`GetPagedAsync` return `NotificationDto`; Task 12's controller/views call `Format`.

This mirrors the existing `StatusText` static-helper pattern (`src/MuktoAin.Web/Controllers/StatusText.cs`) — presentation mapping, not business logic, so it lives in `MuktoAin.Web.Controllers` alongside `StatusText`/`MarkdownText`, not in Application.

- [ ] **Step 1: Write the DTO**

```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record NotificationDto(
    int NotificationId,
    NotificationType Type,
    int? RelatedCaseId,
    int? RelatedDocumentId,
    int? RelatedLawyerProfileId,
    bool IsRead,
    DateTime CreatedAt
);
```

- [ ] **Step 2: Write the failing test for the formatter**

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class NotificationTextFormatterTests
{
    [Fact]
    public void Format_CaseSubmitted_LinksToCaseResult()
    {
        var dto = new NotificationDto(1, NotificationType.CaseSubmitted, RelatedCaseId: 42,
            RelatedDocumentId: null, RelatedLawyerProfileId: null, IsRead: false, CreatedAt: DateTime.UtcNow);

        var (textBn, textEn, url) = NotificationTextFormatter.Format(dto);

        Assert.Contains("৪২", textBn); // Bengali numeral rendering isn't required in text; case id may appear in the link only
        Assert.Equal("/Case/Result?id=42", url);
        Assert.False(string.IsNullOrWhiteSpace(textEn));
    }

    [Fact]
    public void Format_LawyerVerified_LinksToLawyerStatus()
    {
        var dto = new NotificationDto(2, NotificationType.LawyerVerified, RelatedCaseId: null,
            RelatedDocumentId: null, RelatedLawyerProfileId: 7, IsRead: false, CreatedAt: DateTime.UtcNow);

        var (_, textEn, url) = NotificationTextFormatter.Format(dto);

        Assert.Equal("/Lawyer/Status", url);
        Assert.False(string.IsNullOrWhiteSpace(textEn));
    }
}
```

Note: the first assertion (`Contains("৪২", textBn)`) is deliberately loose — adjust to whatever exact copy you write in Step 3, the point being the case id is discoverable in the Bangla text or link, not a specific phrasing. If your Step 3 copy doesn't include the numeral in `textBn`, drop that assertion line rather than force numeral formatting the design doesn't ask for.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter NotificationTextFormatterTests`
Expected: FAIL — `NotificationTextFormatter` does not exist.

- [ ] **Step 4: Implement the formatter**

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Web.Controllers;

/// <summary>
/// Bilingual display text + deep-link URL for a Notification, by type.
/// Presentation mapping only — mirrors StatusText's shape and purpose.
/// </summary>
public static class NotificationTextFormatter
{
    public static (string TextBn, string TextEn, string Url) Format(NotificationDto dto) => dto.Type switch
    {
        NotificationType.CaseSubmitted => (
            "আপনার মামলা সফলভাবে জমা হয়েছে।",
            "Your case was submitted successfully.",
            $"/Case/Result?id={dto.RelatedCaseId}"),

        NotificationType.DocumentDecided => (
            "আপনার নথিতে আইনজীবী সিদ্ধান্ত দিয়েছেন।",
            "A lawyer made a decision on your document.",
            $"/Case/Result?id={dto.RelatedCaseId}"),

        NotificationType.LawyerVerified => (
            "আপনার আইনজীবী যাচাইয়ের সিদ্ধান্ত হয়েছে।",
            "Your lawyer verification decision is ready.",
            "/Lawyer/Status"),

        NotificationType.PaymentReceived => (
            "আপনার হিসাবে সম্মানী জমা হয়েছে।",
            "A payment was credited to your balance.",
            "/Lawyer/Payments"),

        NotificationType.NewLawyerApplication => (
            "নতুন আইনজীবী আবেদন যাচাইয়ের অপেক্ষায়।",
            "A new lawyer application is awaiting verification.",
            "/Admin/Lawyers"),

        _ => ("বিজ্ঞপ্তি", "Notification", "/")
    };
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter NotificationTextFormatterTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Application/DTOs/NotificationDto.cs src/MuktoAin.Web/Controllers/NotificationTextFormatter.cs tests/MuktoAin.UnitTests/Controllers/NotificationTextFormatterTests.cs
git commit -m "feat(notifications): add NotificationDto and bilingual text formatter"
```

---

## Task 4: Application — `NotificationService`

**Files:**
- Create: `src/MuktoAin.Application/Services/NotificationService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/NotificationServiceTests.cs`

**Interfaces:**
- Consumes: `IRepository<Notification>` (`GetAllAsync`, `AddAsync`, `SaveChangesAsync` — generic repo, Task 2's `DbSet<Notification>` backs it via existing DI registration `AddScoped(typeof(IRepository<>), typeof(Repository<>))` in `Program.cs`, no new repository interface needed), `IRepository<User>` — but `User` is Identity's aggregate root managed via `UserManager<User>`, not the generic repo (see `AdminController`'s existing pattern), so `NotifyAllAdminsAsync` takes `UserManager<User>` instead.
- Produces: `NotificationService` with the exact method signatures below — Tasks 7–11 (call-site wiring) and Task 12 (controller) depend on these names/signatures verbatim.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IRepository<Notification>> _repo = new();
    private readonly Mock<ILogger<NotificationService>> _logger = new();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _service = new NotificationService(_repo.Object, NewUserManager(), _logger.Object);
    }

    private static UserManager<User> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new UserManager<User>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
    }

    [Fact]
    public async Task NotifyAsync_AddsRowWithRequestedType()
    {
        Notification? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(n => added = n)
            .Returns(Task.CompletedTask);

        await _service.NotifyAsync(userId: 5, NotificationType.CaseSubmitted, caseId: 42);

        Assert.NotNull(added);
        Assert.Equal(5, added!.UserId);
        Assert.Equal(NotificationType.CaseSubmitted, added.Type);
        Assert.Equal(42, added.RelatedCaseId);
        Assert.False(added.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_SwallowsRepositoryException()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<Notification>())).ThrowsAsync(new InvalidOperationException("db down"));

        // Must not throw — the caller (e.g. a lawyer review submission) must
        // succeed even if the notification write fails.
        await _service.NotifyAsync(userId: 5, NotificationType.CaseSubmitted, caseId: 42);
    }

    [Fact]
    public async Task MarkReadAsync_ReturnsFalse_WhenCallerDoesNotOwnNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 99, IsRead = false };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.MarkReadAsync(notificationId: 1, userId: 5);

        Assert.False(ok);
        Assert.False(n.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task MarkReadAsync_MarksRead_WhenCallerOwnsNotification()
    {
        var n = new Notification { NotificationId = 1, UserId = 5, IsRead = false };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification> { n });

        var ok = await _service.MarkReadAsync(notificationId: 1, userId: 5);

        Assert.True(ok);
        Assert.True(n.IsRead);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_NeverReturnsAnotherUsersRows()
    {
        var rows = new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, CreatedAt = DateTime.UtcNow },
            new() { NotificationId = 2, UserId = 99, CreatedAt = DateTime.UtcNow },
        };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(rows);

        var page = await _service.GetPagedAsync(userId: 5, page: 1, pageSize: 20);

        Assert.All(page.Items, i => Assert.Equal(1, i.NotificationId));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task GetUnreadCountAsync_CountsOnlyUnreadForThatUser()
    {
        var rows = new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, IsRead = false },
            new() { NotificationId = 2, UserId = 5, IsRead = true },
            new() { NotificationId = 3, UserId = 99, IsRead = false },
        };
        _repo.Setup(r => r.GetAllAsync()).ReturnsAsync(rows);

        var count = await _service.GetUnreadCountAsync(userId: 5);

        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter NotificationServiceTests`
Expected: FAIL — `NotificationService`, `PagedResult<T>` do not exist.

- [ ] **Step 3: Write `PagedResult<T>` (shared shape, DTOs folder)**

```csharp
namespace MuktoAin.Application.DTOs;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
```

Save as `src/MuktoAin.Application/DTOs/PagedResult.cs`.

- [ ] **Step 4: Implement `NotificationService`**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class NotificationService
{
    private readonly IRepository<Notification> _repo;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Notification> repo, UserManager<User> userManager, ILogger<NotificationService> logger)
    {
        _repo = repo;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task NotifyAsync(
        int userId, NotificationType type,
        int? caseId = null, int? documentId = null, int? lawyerProfileId = null)
    {
        try
        {
            await _repo.AddAsync(new Notification
            {
                UserId = userId,
                Type = type,
                RelatedCaseId = caseId,
                RelatedDocumentId = documentId,
                RelatedLawyerProfileId = lawyerProfileId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the caller's real operation
            // (case submission, review, verification, payment).
            _logger.LogWarning(ex, "Failed to write notification for user {UserId}, type {Type}", userId, type);
        }
    }

    public async Task NotifyAllAdminsAsync(
        NotificationType type, int? caseId = null, int? documentId = null, int? lawyerProfileId = null)
    {
        var admins = _userManager.Users.Where(u => u.Role == UserRole.Admin).ToList();
        foreach (var admin in admins)
        {
            await NotifyAsync(admin.Id, type, caseId, documentId, lawyerProfileId);
        }
    }

    public async Task<IReadOnlyList<NotificationDto>> GetRecentAsync(int userId, int take = 10)
    {
        var all = await _repo.GetAllAsync();
        return all.Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(ToDto)
            .ToList();
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        var all = await _repo.GetAllAsync();
        return all.Count(n => n.UserId == userId && !n.IsRead);
    }

    public async Task<PagedResult<NotificationDto>> GetPagedAsync(int userId, int page, int pageSize)
    {
        var mine = (await _repo.GetAllAsync())
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        var items = mine.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToList();
        return new PagedResult<NotificationDto>(items, mine.Count, page, pageSize);
    }

    public async Task<bool> MarkReadAsync(int notificationId, int userId)
    {
        var all = await _repo.GetAllAsync();
        var n = all.FirstOrDefault(x => x.NotificationId == notificationId);
        if (n == null || n.UserId != userId) return false;

        n.IsRead = true;
        await _repo.SaveChangesAsync();
        return true;
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var mine = (await _repo.GetAllAsync()).Where(n => n.UserId == userId && !n.IsRead);
        foreach (var n in mine) n.IsRead = true;
        await _repo.SaveChangesAsync();
    }

    private static NotificationDto ToDto(Notification n) => new(
        n.NotificationId, n.Type, n.RelatedCaseId, n.RelatedDocumentId, n.RelatedLawyerProfileId,
        n.IsRead, n.CreatedAt);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter NotificationServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 6: Register in DI**

In `src/MuktoAin.Web/Program.cs`, immediately after the existing `builder.Services.AddScoped<PaymentService>();` line, add:

```csharp
builder.Services.AddScoped<NotificationService>();
```

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/MuktoAin.Application/DTOs/PagedResult.cs src/MuktoAin.Application/Services/NotificationService.cs src/MuktoAin.Web/Program.cs tests/MuktoAin.UnitTests/Services/NotificationServiceTests.cs
git commit -m "feat(notifications): add NotificationService with ownership-checked reads"
```

---

## Task 5: Wire `CaseSubmitted`

**Files:**
- Modify: `src/MuktoAin.Application/Services/CaseService.cs`
- Modify: `src/MuktoAin.Application/Services/ChatService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/CaseServiceTests.cs` (already exists — 4-argument `CaseService` construction, see below)
- Modify: `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs` (already exists — also constructs a `CaseService` internally, ripples from this task)
- Create: `tests/MuktoAin.UnitTests/Services/ChatServiceTests.cs` (does not exist yet)

**Interfaces:**
- Consumes: `IRepository<Notification>` (direct write, same rationale as Task 4's note — `CaseService`/`ChatService` don't need the rest of `NotificationService`'s surface).

- [ ] **Step 1: Update `CaseServiceTests.cs`'s fixture and add the failing tests**

`CaseServiceTests.cs` builds one shared `_service` field in its constructor from shared mock fields (`_caseRepo`, `_categoryRepo`, `_districtRepo`, `_encryptionService`). Add a shared `_notificationRepo` field the same way, and thread it into the existing construction. In the class's field list, add:

```csharp
private readonly Mock<IRepository<Notification>> _notificationRepo = new();
```

Change the constructor's last line from:

```csharp
_service = new CaseService(_caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object);
```

to:

```csharp
_service = new CaseService(_caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object, _notificationRepo.Object);
```

Then add two new tests to the file:

```csharp
[Fact]
public async Task SubmitCaseAsync_NotifiesTheSubmittingUser()
{
    Notification? captured = null;
    _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
        .Callback<Notification>(n => captured = n)
        .Returns(Task.CompletedTask);

    var dto = new CaseSubmissionDto(1, 5, "Title", "Desc", "bn", IsAnonymous: false);
    await _service.SubmitCaseAsync(dto, userId: 7);

    Assert.NotNull(captured);
    Assert.Equal(7, captured!.UserId);
    Assert.Equal(NotificationType.CaseSubmitted, captured.Type);
}

[Fact]
public async Task SubmitCaseAsync_SkipsNotification_WhenAnonymous()
{
    var dto = new CaseSubmissionDto(1, 5, "Title", "Desc", "bn", IsAnonymous: true);
    await _service.SubmitCaseAsync(dto, userId: null);

    _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
}
```

- [ ] **Step 2: Fix the ripple in `LawyerReviewServiceTests.cs`**

That file also constructs a `CaseService` internally (it's `LawyerReviewService`'s own dependency, not something it mocks). Add a `_caseServiceNotificationRepo` field and update its constructor's `_caseService = new CaseService(...)` line from:

```csharp
_caseService = new CaseService(
    _caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object);
```

to:

```csharp
_caseService = new CaseService(
    _caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object,
    new Mock<IRepository<Notification>>().Object);
```

(A throwaway mock is enough here — this file's tests exercise `LawyerReviewService.GetHistoryAsync`, never `CaseService.SubmitCaseAsync`, so nothing asserts against it; it only needs to exist so the constructor call compiles.)

- [ ] **Step 3: Run to verify the new tests fail**

Run: `dotnet test --filter CaseServiceTests|LawyerReviewServiceTests`
Expected: FAIL to compile — `CaseService`'s constructor doesn't accept a 5th argument yet.

- [ ] **Step 4: Add the dependency and the call to `CaseService`**

```csharp
private readonly IRepository<Notification> _notificationRepo;

public CaseService(
    ICaseRepository caseRepo,
    IRepository<CaseCategory> categoryRepo,
    IRepository<District> districtRepo,
    IEncryptionService encryptionService,
    IRepository<Notification> notificationRepo)
{
    _caseRepo = caseRepo;
    _categoryRepo = categoryRepo;
    _districtRepo = districtRepo;
    _encryptionService = encryptionService;
    _notificationRepo = notificationRepo;
}
```

At the end of `SubmitCaseAsync`, before `return new CaseSubmissionResultDto(...)`:

```csharp
if (userId.HasValue && !dto.IsAnonymous)
{
    await _notificationRepo.AddAsync(new Notification
    {
        UserId = userId.Value,
        Type = NotificationType.CaseSubmitted,
        RelatedCaseId = caseEntity.CaseId,
        CreatedAt = DateTime.UtcNow
    });
    await _notificationRepo.SaveChangesAsync();
}
```

Add `using MuktoAin.Domain.Enums;` if not already present (needed for `NotificationType`; `MuktoAin.Domain.Interfaces.Repositories` is already imported).

`CaseService` is already `AddScoped<CaseService>()` in `Program.cs` — no DI change needed, since `IRepository<Notification>` resolves automatically through the existing generic registration.

- [ ] **Step 5: Run to verify `CaseServiceTests`/`LawyerReviewServiceTests` pass**

Run: `dotnet test --filter CaseServiceTests|LawyerReviewServiceTests`
Expected: PASS — both files' full suites, not just the new tests (confirms the constructor ripple didn't break anything already passing).

- [ ] **Step 6: Repeat for `ChatService.CommitToCaseAsync`, with a new test file**

`ChatService`'s constructor already takes 10 dependencies (`IRepository<ChatSession> sessionRepo, IRepository<ChatMessage> messageRepo, IRepository<Case> caseRepo, ICaseRepository caseRepoTyped, IRepository<AnswerCache> cacheRepo, IRightsExplanationService rightsService, DocumentService documentService, IEncryptionService encryptionService, IScenarioMappingRepository scenarioRepo, IKeywordSectionSearch keywordSearch`). Add `IRepository<Notification> notificationRepo` as an 11th, and inside `CommitToCaseAsync`, right after `await _sessionRepo.SaveChangesAsync();` (the line that flips the session to `Committed`):

```csharp
if (userId.HasValue && !isAnonymous)
{
    await notificationRepo.AddAsync(new Notification
    {
        UserId = userId.Value,
        Type = NotificationType.CaseSubmitted,
        RelatedCaseId = caseEntity.CaseId,
        CreatedAt = DateTime.UtcNow
    });
    await notificationRepo.SaveChangesAsync();
}
```

Create `tests/MuktoAin.UnitTests/Services/ChatServiceTests.cs` (no existing file to extend — `DocumentService` also needs constructing since `ChatService` depends on it directly, not just its interface; mock `DocumentService`'s own dependencies minimally since it's a concrete class, same pattern as `CaseService` inside `LawyerReviewServiceTests.cs` above):

```csharp
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Documents;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class ChatServiceTests
{
    private readonly Mock<IRepository<ChatSession>> _sessionRepo = new();
    private readonly Mock<IRepository<ChatMessage>> _messageRepo = new();
    private readonly Mock<IRepository<Case>> _caseRepo = new();
    private readonly Mock<ICaseRepository> _caseRepoTyped = new();
    private readonly Mock<IRepository<AnswerCache>> _cacheRepo = new();
    private readonly Mock<IRightsExplanationService> _rightsService = new();
    private readonly Mock<IEncryptionService> _encryptionService = new();
    private readonly Mock<IScenarioMappingRepository> _scenarioRepo = new();
    private readonly Mock<IKeywordSectionSearch> _keywordSearch = new();
    private readonly Mock<IRepository<Notification>> _notificationRepo = new();
    private readonly DocumentService _documentService;
    private readonly ChatService _service;

    public ChatServiceTests()
    {
        var docRepo = new Mock<IRepository<GeneratedDocument>>();
        var districtRepo = new Mock<IRepository<District>>();
        var categoryRepo = new Mock<IRepository<CaseCategory>>();
        var pdfExporter = new Mock<IPdfExporter>();
        var templates = new List<IDocumentTemplate>(); // no templates needed for these tests
        var generator = new DocumentGenerator(templates);
        _documentService = new DocumentService(
            generator, docRepo.Object, _caseRepoTyped.Object, districtRepo.Object, categoryRepo.Object, pdfExporter.Object);

        _service = new ChatService(
            _sessionRepo.Object, _messageRepo.Object, _caseRepo.Object, _caseRepoTyped.Object,
            _cacheRepo.Object, _rightsService.Object, _documentService, _encryptionService.Object,
            _scenarioRepo.Object, _keywordSearch.Object, _notificationRepo.Object);

        _encryptionService.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => s);
    }

    [Fact]
    public async Task CommitToCaseAsync_NotifiesTheCommittingUser()
    {
        var session = new ChatSession { ChatSessionId = 1, UserId = 7, Status = ChatSessionStatus.InProgress };
        _sessionRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(session);
        _messageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ChatMessage>
        {
            new() { ChatSessionId = 1, ChatMessageId = 1, Role = "user", Content = "hello" }
        });
        var savedCase = new Case { CaseId = 99 };
        _caseRepo.Setup(r => r.AddAsync(It.IsAny<Case>()))
            .Callback<Case>(c => c.CaseId = 99)
            .Returns(Task.CompletedTask);
        _caseRepoTyped.Setup(r => r.GetWithDocumentsAsync(99)).ReturnsAsync(savedCase);
        _rightsService.Setup(r => r.ExplainRightsAsync(It.IsAny<Case>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RightsExplanationDto("explanation", new List<CitedSectionDto>(), "disclaimer"));

        Notification? captured = null;
        _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(n => captured = n)
            .Returns(Task.CompletedTask);

        await _service.CommitToCaseAsync(
            chatSessionId: 1, categoryId: 1, districtId: 1, title: "t",
            notificationEmail: null, isAnonymous: false, userId: 7, documentType: "LabourComplaint");

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.UserId);
        Assert.Equal(NotificationType.CaseSubmitted, captured.Type);
    }

    [Fact]
    public async Task CommitToCaseAsync_SkipsNotification_WhenAnonymous()
    {
        var session = new ChatSession { ChatSessionId = 1, UserId = null, Status = ChatSessionStatus.InProgress };
        _sessionRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(session);
        _messageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ChatMessage>
        {
            new() { ChatSessionId = 1, ChatMessageId = 1, Role = "user", Content = "hello" }
        });
        var savedCase = new Case { CaseId = 99 };
        _caseRepo.Setup(r => r.AddAsync(It.IsAny<Case>()))
            .Callback<Case>(c => c.CaseId = 99)
            .Returns(Task.CompletedTask);
        _caseRepoTyped.Setup(r => r.GetWithDocumentsAsync(99)).ReturnsAsync(savedCase);
        _rightsService.Setup(r => r.ExplainRightsAsync(It.IsAny<Case>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RightsExplanationDto("explanation", new List<CitedSectionDto>(), "disclaimer"));

        await _service.CommitToCaseAsync(
            chatSessionId: 1, categoryId: 1, districtId: 1, title: "t",
            notificationEmail: null, isAnonymous: true, userId: null, documentType: "LabourComplaint");

        _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
    }
}
```

Check `IRightsExplanationService.ExplainRightsAsync`'s and `RightsExplanationDto`'s exact signatures in `src/MuktoAin.Application/Services/IRightsExplanationService.cs`/`src/MuktoAin.Application/DTOs/` before running this — adjust the mock setup's parameter types/DTO constructor args to match exactly if they differ from what's shown (this plan was written by reading `ChatService.cs`'s call site, not that interface file directly).

- [ ] **Step 7: Run to verify it passes**

Run: `dotnet test --filter ChatServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Run full test suite**

Run: `dotnet test`
Expected: PASS, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/MuktoAin.Application/Services/CaseService.cs src/MuktoAin.Application/Services/ChatService.cs tests/MuktoAin.UnitTests/Services/CaseServiceTests.cs tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs tests/MuktoAin.UnitTests/Services/ChatServiceTests.cs
git commit -m "feat(notifications): notify citizen on case submission"
```

---

## Task 6: Wire `DocumentDecided`, retire `Case.HasUnreadActivity`

**Files:**
- Modify: `src/MuktoAin.Application/Services/LawyerReviewService.cs`
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs`
- Modify: `src/MuktoAin.Domain/Entities/Case.cs`
- Test: `tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs`

**Interfaces:**
- Consumes: `IRepository<Notification>` (direct, same pattern as Task 5) and `NotificationType.DocumentDecided`.

- [ ] **Step 1: Write the failing test**

`LawyerReviewServiceTests.cs` builds one shared `_service` field in its constructor from 10 existing mock fields (`_docRepo, _reviewRepo, _profileRepo, _caseRepo, _categoryRepo, _districtRepo, _refRepo, _sectionRepo, _actRepo, _encryptionService`) plus `_caseService`. Add a shared `_notificationRepo` field and thread it in as the constructor's 12th argument (after `_caseService`):

```csharp
private readonly Mock<IRepository<Notification>> _notificationRepo = new();
```

```csharp
_service = new LawyerReviewService(
    _docRepo.Object, _reviewRepo.Object, _profileRepo.Object, _caseRepo.Object,
    _categoryRepo.Object, _districtRepo.Object, _refRepo.Object, _sectionRepo.Object,
    _actRepo.Object, _encryptionService.Object, _caseService, _notificationRepo.Object);
```

Then add the two new tests:

```csharp
[Fact]
public async Task SubmitReviewAsync_NotifiesTheCaseOwner()
{
    var doc = new GeneratedDocument { DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview };
    _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
    var owner = new Case { CaseId = 10, UserId = 55, IsAnonymous = false };
    _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(owner);
    Notification? captured = null;
    _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
        .Callback<Notification>(n => captured = n)
        .Returns(Task.CompletedTask);

    await _service.SubmitReviewAsync(new SubmitReviewDto(1, LawyerProfileId: 3,
        Decision: ReviewDecision.Approved, Comments: "ok", EditedContent: null));

    Assert.NotNull(captured);
    Assert.Equal(55, captured!.UserId);
    Assert.Equal(NotificationType.DocumentDecided, captured.Type);
    Assert.Equal(10, captured.RelatedCaseId);
    Assert.Equal(1, captured.RelatedDocumentId);
}

[Fact]
public async Task SubmitReviewAsync_SkipsNotification_WhenCaseIsAnonymous()
{
    var doc = new GeneratedDocument { DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview };
    _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
    var anon = new Case { CaseId = 10, UserId = null, IsAnonymous = true };
    _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(anon);

    await _service.SubmitReviewAsync(new SubmitReviewDto(1, LawyerProfileId: 3,
        Decision: ReviewDecision.Approved, Comments: "ok", EditedContent: null));

    _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter LawyerReviewServiceTests`
Expected: FAIL to compile — `LawyerReviewService`'s real constructor doesn't accept a 12th argument yet.

- [ ] **Step 3: Add the dependency and replace the `HasUnreadActivity` block**

In `src/MuktoAin.Application/Services/LawyerReviewService.cs`, add a field and constructor parameter:

```csharp
private readonly IRepository<Notification> _notificationRepo;

public LawyerReviewService(
    IRepository<GeneratedDocument> docRepo,
    IRepository<LawyerReview> reviewRepo,
    IRepository<LawyerProfile> profileRepo,
    ICaseRepository caseRepo,
    IRepository<CaseCategory> categoryRepo,
    IRepository<District> districtRepo,
    IRepository<CaseActReference> refRepo,
    IRepository<ActSection> sectionRepo,
    IRepository<Act> actRepo,
    IEncryptionService encryptionService,
    CaseService caseService,
    IRepository<Notification> notificationRepo)
{
    _docRepo = docRepo;
    _reviewRepo = reviewRepo;
    _profileRepo = profileRepo;
    _caseRepo = caseRepo;
    _categoryRepo = categoryRepo;
    _districtRepo = districtRepo;
    _refRepo = refRepo;
    _sectionRepo = sectionRepo;
    _actRepo = actRepo;
    _encryptionService = encryptionService;
    _caseService = caseService;
    _notificationRepo = notificationRepo;
}
```

Then replace this existing block near the end of `SubmitReviewAsync`:

```csharp
// Flag unread activity for the citizen (unread dot on My Cases)
var c2 = await _caseRepo.GetByIdAsync(d.CaseId);
if (c2 != null)
{
    c2.HasUnreadActivity = true;
    await _caseRepo.SaveChangesAsync();
}
```

with:

```csharp
var c2 = await _caseRepo.GetByIdAsync(d.CaseId);
if (c2 is { UserId: not null, IsAnonymous: false })
{
    await _notificationRepo.AddAsync(new Notification
    {
        UserId = c2.UserId.Value,
        Type = NotificationType.DocumentDecided,
        RelatedCaseId = c2.CaseId,
        RelatedDocumentId = d.DocumentId,
        CreatedAt = DateTime.UtcNow
    });
    await _notificationRepo.SaveChangesAsync();
}
```

- [ ] **Step 4: Remove `Case.HasUnreadActivity` and its remaining read site**

In `src/MuktoAin.Domain/Entities/Case.cs`, delete the `public bool HasUnreadActivity { get; set; }` property.

In `src/MuktoAin.Web/Controllers/CaseController.cs`'s `Result` action, delete this now-dead block (it read the flag this task just stopped writing):

```csharp
// Unread dot clear-on-view
if (caseEntity.HasUnreadActivity)
{
    caseEntity.HasUnreadActivity = false;
    await _caseRepo.SaveChangesAsync();
}
```

`Case/Track.cshtml` renders `c.HasUnread` as a decorative dot per row (`<span class="unread-dot" title="আইনজীবী মতামত দিয়েছেন / New activity">`) — this is exactly the `DocumentDecided` signal, so replace its source with a real notification lookup instead of deleting it. In `CaseController.cs`:

Add a constructor parameter (matching this controller's existing style of injecting `IRepository<T>` directly for simple lookups, e.g. its existing `_reviewRepo`, `_lawyerProfileRepo`):

```csharp
private readonly IRepository<Notification> _notificationRepo;
```

and thread it through the constructor the same way as the other `IRepository<T>` parameters already there.

In `Track`, before the `foreach (var (caseId, sessionCode) in GetTrackedCases())` loop, compute the set of cases with an unread `DocumentDecided` notification for the current user:

```csharp
var unreadCaseIds = currentUserId.HasValue
    ? (await _notificationRepo.GetAllAsync())
        .Where(n => n.UserId == currentUserId.Value
                    && n.Type == NotificationType.DocumentDecided
                    && !n.IsRead
                    && n.RelatedCaseId.HasValue)
        .Select(n => n.RelatedCaseId!.Value)
        .ToHashSet()
    : new HashSet<int>();
```

Then change `ToListItemAsync`'s signature and body from:

```csharp
private async Task<CaseListItemViewModel> ToListItemAsync(CaseDetailDto detail, string code)
{
    var entity = await _caseRepo.GetByIdAsync(detail.CaseId);
    return new CaseListItemViewModel
    {
        CaseId = detail.CaseId,
        TrackingCode = code,
        Title = detail.Title,
        CategoryName = detail.CategoryName,
        Status = detail.Status,
        CreatedAt = detail.CreatedAt,
        HasUnread = entity?.HasUnreadActivity ?? false
    };
}
```

to:

```csharp
private static CaseListItemViewModel ToListItem(CaseDetailDto detail, string code, HashSet<int> unreadCaseIds)
{
    return new CaseListItemViewModel
    {
        CaseId = detail.CaseId,
        TrackingCode = code,
        Title = detail.Title,
        CategoryName = detail.CategoryName,
        Status = detail.Status,
        CreatedAt = detail.CreatedAt,
        HasUnread = unreadCaseIds.Contains(detail.CaseId)
    };
}
```

(now synchronous and no longer needs `_caseRepo.GetByIdAsync` at all — one fewer DB round-trip per row, not just an equivalent swap) and update both call sites inside `Track` (`vm.Cases.Add(await ToListItemAsync(detail, string.Empty));` and the one inside the `GetTrackedCases()` loop) to `vm.Cases.Add(ToListItem(detail, string.Empty, unreadCaseIds));` / `vm.Cases.Add(ToListItem(detail, sessionCode, unreadCaseIds));`.

- [ ] **Step 5: Build and run the full test suite**

Run: `dotnet build MuktoAin.slnx && dotnet test`
Expected: Build succeeds; all tests pass (fix any other test still referencing `Case.HasUnreadActivity`).

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Application/Services/LawyerReviewService.cs src/MuktoAin.Web/Controllers/CaseController.cs src/MuktoAin.Domain/Entities/Case.cs tests/MuktoAin.UnitTests/Services/LawyerReviewServiceTests.cs
git commit -m "feat(notifications): notify citizen on document decision, retire HasUnreadActivity"
```

---

## Task 7: Wire `LawyerVerified`

**Files:**
- Modify: `src/MuktoAin.Application/Services/LawyerVerificationService.cs`
- Modify: `tests/MuktoAin.UnitTests/Services/LawyerVerificationServiceTests.cs` (already exists — single-argument construction, see below)

**Interfaces:**
- Consumes: `IRepository<Notification>`, `NotificationType.LawyerVerified`.

- [ ] **Step 1: Update the fixture and add the failing test**

`LawyerVerificationServiceTests.cs` builds one shared `_service` field in its constructor from `_profileRepo`. Add a shared `_notificationRepo` field and thread it in:

```csharp
private readonly Mock<IRepository<Notification>> _notificationRepo = new();
```

Change the constructor's body from `_service = new LawyerVerificationService(_profileRepo.Object);` to:

```csharp
_service = new LawyerVerificationService(_profileRepo.Object, _notificationRepo.Object);
```

Add `using MuktoAin.Domain.Entities;` to the file's usings if not already present (it already imports `MuktoAin.Domain.Entities` via `LawyerProfile`, so no change needed there in practice — `Notification` lives in the same namespace).

Then add the new test:

```csharp
[Fact]
public async Task VerifyAsync_NotifiesTheLawyer()
{
    var profile = new LawyerProfile { LawyerProfileId = 3, UserId = 21, VerificationStatus = VerificationStatus.Pending };
    _profileRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(profile);
    Notification? captured = null;
    _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
        .Callback<Notification>(n => captured = n)
        .Returns(Task.CompletedTask);

    await _service.VerifyAsync(lawyerProfileId: 3, adminUserId: 1, approve: true);

    Assert.NotNull(captured);
    Assert.Equal(21, captured!.UserId);
    Assert.Equal(NotificationType.LawyerVerified, captured.Type);
    Assert.Equal(3, captured.RelatedLawyerProfileId);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter LawyerVerificationServiceTests`
Expected: FAIL to compile — every test in the file fails to compile, since the shared `_service` construction in the constructor now needs a 2nd argument (expected — same reasoning as Task 9's fixture change).

- [ ] **Step 3: Add the dependency and the call**

```csharp
private readonly IRepository<LawyerProfile> _profileRepo;
private readonly IRepository<Notification> _notificationRepo;

public LawyerVerificationService(
    IRepository<LawyerProfile> profileRepo, IRepository<Notification> notificationRepo)
{
    _profileRepo = profileRepo;
    _notificationRepo = notificationRepo;
}
```

At the end of `VerifyAsync`, after `await _profileRepo.SaveChangesAsync();`:

```csharp
await _notificationRepo.AddAsync(new Notification
{
    UserId = profile.UserId,
    Type = NotificationType.LawyerVerified,
    RelatedLawyerProfileId = profile.LawyerProfileId,
    CreatedAt = DateTime.UtcNow
});
await _notificationRepo.SaveChangesAsync();
```

Add `using MuktoAin.Domain.Entities;` if not already present.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter LawyerVerificationServiceTests`
Expected: PASS — the whole file, including the 4 pre-existing tests plus the new one (confirmed via `grep -rn "new LawyerVerificationService("` that this file is the only construction site in the test suite, so no other file needs updating).

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Application/Services/LawyerVerificationService.cs tests/MuktoAin.UnitTests/Services/LawyerVerificationServiceTests.cs
git commit -m "feat(notifications): notify lawyer on verification decision"
```

---

## Task 8: Wire `PaymentReceived`

**Files:**
- Modify: `src/MuktoAin.Application/Services/PaymentService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs`

**Interfaces:**
- Consumes: `IRepository<Notification>`, `NotificationType.PaymentReceived`.

- [ ] **Step 1: Write the failing test**

Add to `tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs`:

```csharp
[Fact]
public async Task CreateHonorariumOrderAsync_NotifiesTheLawyer_WhenOneIsAssigned()
{
    var doc = new GeneratedDocument { DocumentId = 1, CaseId = 5, AssignedLawyerProfileId = 42 };
    var c = new Case { CaseId = 5, Documents = new List<GeneratedDocument> { doc } };
    _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(c);
    var profile = new LawyerProfile { LawyerProfileId = 42, UserId = 88 };
    _lawyerRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(profile);
    Notification? captured = null;
    _notificationRepo.Setup(n => n.AddAsync(It.IsAny<Notification>()))
        .Callback<Notification>(n => captured = n)
        .Returns(Task.CompletedTask);

    await _service.CreateHonorariumOrderAsync(caseId: 5, userId: 7, amount: 1000m);

    Assert.NotNull(captured);
    Assert.Equal(88, captured!.UserId);
    Assert.Equal(NotificationType.PaymentReceived, captured.Type);
}

[Fact]
public async Task CreateHonorariumOrderAsync_SkipsNotification_WhenNoLawyerAssigned()
{
    var doc = new GeneratedDocument { DocumentId = 1, CaseId = 5, AssignedLawyerProfileId = null };
    var c = new Case { CaseId = 5, Documents = new List<GeneratedDocument> { doc } };
    _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(c);

    await _service.CreateHonorariumOrderAsync(caseId: 5, userId: 7, amount: 1000m);

    _notificationRepo.Verify(n => n.AddAsync(It.IsAny<Notification>()), Times.Never);
}
```

Add `private readonly Mock<IRepository<Notification>> _notificationRepo = new();` to the test class's field list and pass `_notificationRepo.Object` into `new PaymentService(...)` in the constructor (both here and in the pre-existing `CreateHonorariumOrderAsync_ResolvesLawyerFromCasesClaimedDocument` test).

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter PaymentServiceTests`
Expected: FAIL to compile.

- [ ] **Step 3: Add the dependency and the call**

```csharp
private readonly IRepository<Notification> _notificationRepo;

public PaymentService(
    IRepository<PaymentOrder> orderRepo,
    IRepository<PayoutRequest> payoutRepo,
    IRepository<LawyerProfile> lawyerRepo,
    ICaseRepository caseRepo,
    UserManager<User> userManager,
    IRepository<Notification> notificationRepo)
{
    _orderRepo = orderRepo;
    _payoutRepo = payoutRepo;
    _lawyerRepo = lawyerRepo;
    _caseRepo = caseRepo;
    _userManager = userManager;
    _notificationRepo = notificationRepo;
}
```

Inside `CreateHonorariumOrderAsync`, inside the existing `if (order.LawyerProfileId.HasValue)` block, after `await _caseRepo.SaveChangesAsync();`:

```csharp
var lawyerProfile = await _lawyerRepo.GetByIdAsync(order.LawyerProfileId.Value);
if (lawyerProfile != null)
{
    await _notificationRepo.AddAsync(new Notification
    {
        UserId = lawyerProfile.UserId,
        Type = NotificationType.PaymentReceived,
        RelatedCaseId = caseId,
        RelatedLawyerProfileId = lawyerProfile.LawyerProfileId,
        CreatedAt = DateTime.UtcNow
    });
    await _notificationRepo.SaveChangesAsync();
}
```

Add `using MuktoAin.Domain.Entities;` if not already present.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter PaymentServiceTests`
Expected: PASS (3 tests total in that file, including the pre-existing one).

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Application/Services/PaymentService.cs tests/MuktoAin.UnitTests/Services/PaymentServiceTests.cs
git commit -m "feat(notifications): notify lawyer when an honorarium payment lands"
```

---

## Task 9: Wire `NewLawyerApplication`

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AccountController.cs`
- Test: `tests/MuktoAin.UnitTests/Controllers/AccountControllerTests.cs`

**Interfaces:**
- Consumes: `NotificationService.NotifyAllAdminsAsync(NotificationType type, ...)` (Task 4) — this call site uses the full service (unlike Tasks 5–8's direct-repo calls) since `AccountController` has no existing repository dependency to piggyback on and `NotifyAllAdminsAsync`'s admin-enumeration logic belongs in one place.

- [ ] **Step 1: Update the shared test fixture and add the failing test**

`AccountControllerTests.cs` builds one shared `_controller` field inside its constructor from shared mock fields (`_userManager`, `_signInManager`, `_lawyerProfileRepo`) — no per-test `_logger` field; it's passed inline as `Mock.Of<ILogger<AccountController>>()`. Add a shared `_notificationRepo` field the same way `_lawyerProfileRepo` is already declared, build a real `NotificationService` from it (concrete class, not mocked), and thread it into the existing `_controller` construction:

```csharp
private readonly Mock<IRepository<Notification>> _notificationRepo;
```

In the constructor, add right after `_lawyerProfileRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);`:

```csharp
_notificationRepo = new Mock<IRepository<Notification>>();
_notificationRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
var notificationService = new NotificationService(
    _notificationRepo.Object, _userManager.Object, Mock.Of<ILogger<NotificationService>>());
```

Then change the existing `_controller = new AccountController(...)` call from 4 arguments to 5:

```csharp
_controller = new AccountController(
    _signInManager.Object,
    _userManager.Object,
    _lawyerProfileRepo.Object,
    Mock.Of<ILogger<AccountController>>(),
    notificationService)
```

Add `using MuktoAin.Application.Services;` to this file's usings if not already present.

Now add the new test:

```csharp
[Fact]
public async Task Register_LawyerRole_NotifiesAllAdmins()
{
    var admin = new User { Id = 1, Role = UserRole.Admin, Email = "admin@example.com" };
    _userManager.Setup(m => m.Users).Returns(new List<User> { admin }.AsQueryable());
    _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
        .ReturnsAsync(IdentityResult.Success);

    var model = new RegisterViewModel
    {
        FullName = "New Lawyer", Email = "lawyer@example.com", Password = "Passw0rd!",
        Role = "Lawyer", BarRegistrationNumber = "BAR-123"
    };

    await _controller.Register(model);

    _notificationRepo.Verify(n => n.AddAsync(It.Is<Notification>(x =>
        x.UserId == 1 && x.Type == NotificationType.NewLawyerApplication)), Times.Once);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter AccountControllerTests`
Expected: FAIL to compile — `AccountController`'s constructor doesn't accept a 5th argument yet, so every existing test in this file fails to compile too (expected — this is why the fixture change and the new test land in the same step).

- [ ] **Step 3: Add the dependency and the call**

```csharp
private readonly NotificationService _notificationService;

public AccountController(
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IRepository<LawyerProfile> lawyerProfileRepo,
    ILogger<AccountController> logger,
    NotificationService notificationService)
{
    _signInManager = signInManager;
    _userManager = userManager;
    _lawyerProfileRepo = lawyerProfileRepo;
    _logger = logger;
    _notificationService = notificationService;
}
```

Inside `Register`'s existing `if (isLawyer)` block, after `await _lawyerProfileRepo.SaveChangesAsync();`:

```csharp
await _notificationService.NotifyAllAdminsAsync(NotificationType.NewLawyerApplication,
    lawyerProfileId: profile.LawyerProfileId);
```

Add `using MuktoAin.Domain.Enums;` if not already present (it already is, via `UserRole`/`VerificationStatus` usage in that file).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter AccountControllerTests`
Expected: PASS — all pre-existing tests in the file plus the new one, since the fixture change in Step 1 already updated the one shared `_controller` construction they all use.

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Web/Controllers/AccountController.cs tests/MuktoAin.UnitTests/Controllers/AccountControllerTests.cs
git commit -m "feat(notifications): notify all admins on new lawyer application"
```

---

## Task 10: `NotificationController`

**Files:**
- Create: `src/MuktoAin.Web/Controllers/NotificationController.cs`
- Test: `tests/MuktoAin.UnitTests/Controllers/NotificationControllerTests.cs`

**Interfaces:**
- Consumes: `NotificationService` (Task 4), `NotificationTextFormatter.Format` (Task 3).
- Produces: `GET /Notification/Unread`, `GET /Notification/Index`, `POST /Notification/MarkRead`, `POST /Notification/MarkAllRead` — Task 11's view and Task 12's JS call these routes verbatim.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class NotificationControllerTests
{
    private static NotificationController NewController(NotificationService service, int userId)
    {
        var controller = new NotificationController(service);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
        return controller;
    }

    [Fact]
    public async Task MarkRead_ReturnsForbid_ForSomeoneElsesNotification()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 99 }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = await controller.MarkRead(1);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Unread_ReturnsOnlyCallersOwnNotifications()
    {
        var repo = new Mock<IRepository<Notification>>();
        repo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Notification>
        {
            new() { NotificationId = 1, UserId = 5, CreatedAt = DateTime.UtcNow },
            new() { NotificationId = 2, UserId = 99, CreatedAt = DateTime.UtcNow }
        });
        var service = new NotificationService(repo.Object, NewUserManagerStub(), Mock.Of<ILogger<NotificationService>>());
        var controller = NewController(service, userId: 5);

        var result = Assert.IsType<JsonResult>(await controller.Unread());
        var count = (int)result.Value!.GetType().GetProperty("count")!.GetValue(result.Value)!;
        Assert.Equal(1, count);
    }

    private static Microsoft.AspNetCore.Identity.UserManager<User> NewUserManagerStub()
    {
        var store = new Mock<Microsoft.AspNetCore.Identity.IUserStore<User>>();
        return new Microsoft.AspNetCore.Identity.UserManager<User>(store.Object,
            Mock.Of<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Identity.IdentityOptions>>(),
            new Microsoft.AspNetCore.Identity.PasswordHasher<User>(),
            Array.Empty<Microsoft.AspNetCore.Identity.IUserValidator<User>>(),
            Array.Empty<Microsoft.AspNetCore.Identity.IPasswordValidator<User>>(),
            new Microsoft.AspNetCore.Identity.UpperInvariantLookupNormalizer(),
            new Microsoft.AspNetCore.Identity.IdentityErrorDescriber(), null!,
            Mock.Of<ILogger<Microsoft.AspNetCore.Identity.UserManager<User>>>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter NotificationControllerTests`
Expected: FAIL — `NotificationController` doesn't exist.

- [ ] **Step 3: Implement the controller**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.Services;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

[Authorize]
public class NotificationController : Controller
{
    private const int PageSize = 20;

    private readonly NotificationService _service;

    public NotificationController(NotificationService service)
    {
        _service = service;
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> Unread()
    {
        var userId = CurrentUserId();
        var count = await _service.GetUnreadCountAsync(userId);
        var recent = await _service.GetRecentAsync(userId, take: 10);

        return Json(new
        {
            count,
            items = recent.Select(n =>
            {
                var (textBn, textEn, url) = NotificationTextFormatter.Format(n);
                return new { id = n.NotificationId, textBn, textEn, url, isRead = n.IsRead, createdAt = n.CreatedAt };
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var userId = CurrentUserId();
        var paged = await _service.GetPagedAsync(userId, page, PageSize);

        var vm = new NotificationListViewModel
        {
            Page = page,
            PageSize = PageSize,
            TotalCount = paged.TotalCount,
            Items = paged.Items.Select(n =>
            {
                var (textBn, textEn, url) = NotificationTextFormatter.Format(n);
                return new NotificationItemViewModel
                {
                    NotificationId = n.NotificationId,
                    TextBn = textBn,
                    TextEn = textEn,
                    Url = url,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                };
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var ok = await _service.MarkReadAsync(id, CurrentUserId());
        if (!ok) return Forbid();
        return Ok();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        await _service.MarkAllReadAsync(CurrentUserId());
        return Ok();
    }
}
```

- [ ] **Step 4: Run to verify tests pass**

Run: `dotnet test --filter NotificationControllerTests`
Expected: PASS (2 tests). `NotificationListViewModel`/`NotificationItemViewModel` don't exist yet — Task 11 creates them; this controller won't compile until then, so run Task 11's Step 1 before this step's test actually goes green if working strictly task-by-task, or create a minimal placeholder-free version of both view models now (their final shape is defined in Task 11 anyway — write it there and treat Tasks 10–11 as landing together).

- [ ] **Step 5: Commit** (after Task 11's view models exist, so this compiles)

```bash
git add src/MuktoAin.Web/Controllers/NotificationController.cs tests/MuktoAin.UnitTests/Controllers/NotificationControllerTests.cs
git commit -m "feat(notifications): add NotificationController with ownership-checked MarkRead"
```

---

## Task 11: ViewModels and the `Notification/Index` view

**Files:**
- Create: `src/MuktoAin.Web/ViewModels/NotificationViewModels.cs`
- Create: `src/MuktoAin.Web/Views/Notification/Index.cshtml`

**Interfaces:**
- Produces: `NotificationListViewModel { Page, PageSize, TotalCount, Items }`, `NotificationItemViewModel { NotificationId, TextBn, TextEn, Url, IsRead, CreatedAt }` — consumed by Task 10's `NotificationController.Index`.

- [ ] **Step 1: Write the view models**

```csharp
namespace MuktoAin.Web.ViewModels;

public class NotificationListViewModel
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<NotificationItemViewModel> Items { get; set; } = new();
}

public class NotificationItemViewModel
{
    public int NotificationId { get; set; }
    public string TextBn { get; set; } = string.Empty;
    public string TextEn { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 2: Write the view**

Following `Views/Lawyer/History.cshtml`'s pagination markup exactly (same `.pagination` class, `asp-route-page`, `chevron-left`/`chevron-right` Lucide icons):

```cshtml
@model MuktoAin.Web.ViewModels.NotificationListViewModel
@{
    ViewData["Title"] = "বিজ্ঞপ্তি / Notifications";
    var totalPages = (int)System.Math.Ceiling(Model.TotalCount / (double)Model.PageSize);
}

<div class="container" style="max-width: 720px; margin: 24px auto;">
    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom: 16px;">
        <h1 data-bn="বিজ্ঞপ্তি" data-en="Notifications">বিজ্ঞপ্তি</h1>
        @if (Model.Items.Any(i => !i.IsRead))
        {
            <form asp-controller="Notification" asp-action="MarkAllRead" method="post" id="mark-all-read-form">
                @Html.AntiForgeryToken()
                <button type="submit" class="btn btn-outline btn-sm" data-bn="সব পঠিত করুন" data-en="Mark all read">সব পঠিত করুন</button>
            </form>
        }
    </div>

    @if (!Model.Items.Any())
    {
        <p class="muted" data-bn="কোনো বিজ্ঞপ্তি নেই।" data-en="No notifications yet.">কোনো বিজ্ঞপ্তি নেই।</p>
    }
    else
    {
        <ul style="list-style:none; padding:0; display:flex; flex-direction:column; gap:8px;">
            @foreach (var item in Model.Items)
            {
                <li style="border:1px solid var(--border); border-radius:8px; padding:12px 14px; @(item.IsRead ? "" : "background: var(--surface-2);")">
                    <a asp-action="MarkAndGo" asp-route-id="@item.NotificationId" asp-route-returnUrl="@item.Url"
                       style="display:block; text-decoration:none; color:inherit;">
                        <span data-bn="@item.TextBn" data-en="@item.TextEn">@item.TextBn</span>
                        <div class="muted tiny" style="margin-top:4px;">@item.CreatedAt.ToString("d MMM yyyy, HH:mm")</div>
                    </a>
                </li>
            }
        </ul>

        @if (totalPages > 1)
        {
            <div class="pagination" style="margin-top: 12px">
                @if (Model.Page > 1)
                {
                    <a asp-action="Index" asp-route-page="@(Model.Page - 1)"><i data-lucide="chevron-left"></i></a>
                }
                @for (var p = 1; p <= totalPages; p++)
                {
                    <a class="@(p == Model.Page ? "active" : "")" asp-action="Index" asp-route-page="@p">@p</a>
                }
                @if (Model.Page < totalPages)
                {
                    <a asp-action="Index" asp-route-page="@(Model.Page + 1)"><i data-lucide="chevron-right"></i></a>
                }
            </div>
        }
    }
</div>
```

This references a `MarkAndGo` action (mark-read-then-redirect) rather than a bare link, so clicking a notification both marks it read and navigates — add it to `NotificationController`:

```csharp
[HttpGet]
public async Task<IActionResult> MarkAndGo(int id, string returnUrl)
{
    await _service.MarkReadAsync(id, CurrentUserId());
    if (!Url.IsLocalUrl(returnUrl)) return RedirectToAction(nameof(Index));
    return Redirect(returnUrl);
}
```

(`Url.IsLocalUrl` guard mirrors the existing pattern already used in `AccountController.Login`'s `returnUrl` handling — never redirect to an external URL from user-influenced input.)

- [ ] **Step 3: Build**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors — this also finally makes Task 10's controller compile.

- [ ] **Step 4: Manual check**

Run the app (`dotnet run --project src/MuktoAin.Web`), log in as any seeded demo user, navigate to `/Notification/Index`. Expected: page renders (likely empty list on a fresh dev DB — that's correct, not a bug, until Tasks 5–9's triggers fire).

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Web/ViewModels/NotificationViewModels.cs src/MuktoAin.Web/Views/Notification/Index.cshtml src/MuktoAin.Web/Controllers/NotificationController.cs tests/MuktoAin.UnitTests/Controllers/NotificationControllerTests.cs
git commit -m "feat(notifications): add notification list page"
```

---

## Task 12: Bell icon in `_Layout.cshtml` + polling JS

**Files:**
- Modify: `src/MuktoAin.Web/Views/Shared/_Layout.cshtml`
- Modify: `src/MuktoAin.Web/wwwroot/assets/js/main.js`

**Interfaces:**
- Consumes: `GET /Notification/Unread`, `POST /Notification/MarkRead` (Task 10).

- [ ] **Step 1: Add the bell icon to the nav, reusing the existing `pop-wrap`/`data-pop` mechanism**

In `_Layout.cshtml`, inside the `else { ... }` branch under `@if (!isAuthenticated) { ... } else { <div class="pop-wrap"> ... avatar ... </div> }` (the authenticated branch), immediately before that existing avatar `<div class="pop-wrap">`, add a second one:

```cshtml
<div class="pop-wrap">
    <button class="icon-btn" type="button" data-pop="notif-pop" aria-label="বিজ্ঞপ্তি" id="notif-bell">
        <i data-lucide="bell"></i>
        <span class="badge" id="notif-badge" hidden>0</span>
    </button>
    <div class="menu-pop" id="notif-pop">
        <div style="padding: 8px 12px; border-bottom: 1px solid var(--border); font-size: 13px; display:flex; justify-content:space-between; align-items:center;">
            <strong data-bn="বিজ্ঞপ্তি" data-en="Notifications">বিজ্ঞপ্তি</strong>
            <a asp-controller="Notification" asp-action="Index" class="tiny" data-bn="সব দেখুন" data-en="See all">সব দেখুন</a>
        </div>
        <div id="notif-pop-list"></div>
    </div>
</div>
```

`.icon-btn`, `.pop-wrap`, `.menu-pop`, `.badge` classes and the `[data-pop]` click-toggle JS already exist and are reused verbatim — no new CSS or dropdown-toggle logic needed. If `main.css` has no `.badge` class already (check `wwwroot/assets/css/main.css` for `\.badge\b` before assuming), add a minimal one scoped to this button:

```css
#notif-bell { position: relative; }
#notif-bell .badge {
    position: absolute; top: -2px; right: -2px;
    background: var(--danger, #c0392b); color: #fff;
    border-radius: 999px; font-size: 10px; line-height: 1;
    padding: 2px 5px; font-variant-numeric: tabular-nums;
}
```

- [ ] **Step 2: Add the polling/render JS**

In `src/MuktoAin.Web/wwwroot/assets/js/main.js`, near the existing `[data-pop]` wiring block (search for `document.querySelectorAll("[data-pop]")`), add a new self-contained block — this can go anywhere inside the same `DOMContentLoaded`-style IIFE the rest of the file's nav logic runs in (match whatever wrapping function the `[data-pop]` block above is already inside):

```javascript
/* notifications */
(function () {
    var bell = document.getElementById("notif-bell");
    if (!bell) return; // not authenticated -- bell isn't rendered

    var badge = document.getElementById("notif-badge");
    var list = document.getElementById("notif-pop-list");

    function render(data) {
        if (data.count > 0) {
            badge.textContent = String(data.count);
            badge.hidden = false;
        } else {
            badge.hidden = true;
        }
        list.innerHTML = "";
        if (!data.items || !data.items.length) {
            var empty = document.createElement("div");
            empty.className = "muted tiny";
            empty.style.padding = "12px";
            empty.textContent = document.documentElement.lang === "en" ? "No notifications yet." : "কোনো বিজ্ঞপ্তি নেই।";
            list.appendChild(empty);
            return;
        }
        data.items.forEach(function (item) {
            var a = document.createElement("a");
            a.href = item.url;
            a.style.display = "block";
            a.style.padding = "8px 12px";
            a.style.borderBottom = "1px solid var(--border)";
            a.style.fontWeight = item.isRead ? "normal" : "600";
            a.textContent = document.documentElement.lang === "en" ? item.textEn : item.textBn;
            a.addEventListener("click", function (e) {
                e.preventDefault();
                fetch("/Notification/MarkRead", {
                    method: "POST",
                    headers: { "RequestVerificationToken": antiForgeryToken() }
                }).finally(function () { window.location.href = item.url; });
            });
            list.appendChild(a);
        });
    }

    function antiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    function poll() {
        fetch("/Notification/Unread")
            .then(function (r) { return r.json(); })
            .then(render)
            .catch(function () {});
    }

    poll();
    setInterval(poll, 30000);
})();
```

Note: `MarkRead` requires `[ValidateAntiForgeryToken]`, which validates the `RequestVerificationToken` header/form value against the user's session — `antiForgeryToken()` above reads it from any `<form>`'s hidden `__RequestVerificationToken` input already present on the page (e.g. `_Layout.cshtml`'s existing logout form renders one via `asp-controller`/`method="post"`); if no such hidden input exists on every page (verify via browser devtools on a page with no other forms), add one explicitly near the bell markup: `<form id="notif-af" style="display:none">@Html.AntiForgeryToken()</form>`, and change the selector to `#notif-af input[name="__RequestVerificationToken"]`.

- [ ] **Step 3: Manual verification**

Run the app, log in, trigger one of the five events (e.g. submit a case as a logged-in citizen), confirm the bell badge shows "1" within 30 seconds (or immediately on next page load, since `poll()` also runs once on load) and clicking the item navigates to the case and clears the badge.

- [ ] **Step 4: Commit**

```bash
git add src/MuktoAin.Web/Views/Shared/_Layout.cshtml src/MuktoAin.Web/wwwroot/assets/js/main.js
git commit -m "feat(notifications): add bell icon with polling dropdown"
```

---

## Task 13: Update `plans/Dependency_plan.md`

**Files:**
- Modify: `plans/Dependency_plan.md`

Per this project's mandatory task-tracking rule (CLAUDE.md), record this work as a completed entry in the redesign-wave tracking table/list (follow the existing `[x] **[R-N]** ... — *Hrittika* (...)` format already used for entries R-18 through R-27), summarizing: new `NOTIFICATION` table, `NotificationService`, five wired triggers, bell icon + list page, and the `Case.HasUnreadActivity` retirement.

- [ ] **Step 1: Add the entry** (pick the next unused `R-N` number after the highest currently in the file)

- [ ] **Step 2: Commit**

```bash
git add plans/Dependency_plan.md
git commit -m "docs: record in-app notifications in Dependency_plan.md"
```
