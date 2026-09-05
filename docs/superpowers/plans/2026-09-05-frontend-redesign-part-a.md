# Frontend Redesign Implementation Plan — PART A (Backend + Chat-First Home + Case Lifecycle)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **EXECUTOR NOTE:** This plan is written for **Gemini 3.8 Flash (Antigravity)** with ZERO creative freedom. Follow every step exactly as written — the code blocks are complete and final. Do NOT redesign, rename, restructure, "improve", or deviate. All signatures were verified against the actual codebase on 2026-09-05. If something doesn't compile, re-read the step before changing anything. Steps marked **[OPENCODE VERIFY]** are reserved for the OpenCode model — Antigravity skips them.

**Goal:** Build the chat-first citizen experience backend (chat sessions, quota, case commit) and rewire Home into a ChatGPT-style interface, plus the new case lifecycle (edit/resubmit/withdraw/send-to-lawyer), without breaking any existing working flow.

**Architecture:** Additive services in `MuktoAin.Application` + one new `ChatController`. Chat turns run on an unsaved in-memory `Case` (CaseId=0) so the existing RAG pipeline (`AiOrchestrationService.ProcessCaseAsync`) is reused verbatim with zero DB writes until commit. At commit, the transcript is persisted as `CHAT_MESSAGE` rows attached to the new case. New tables (`CHAT_SESSION`, `CHAT_MESSAGE`, `ANSWER_CACHE`, `PAYMENT_ORDER`, `PAYOUT_REQUEST`) land via a new SSMS script `scripts/08_redesign_tables.sql` + EF configurations — additive only; existing tables gain nullable/defaulted columns only.

**Tech Stack:** ASP.NET Core MVC (.NET 8), EF Core (manual-schema mapping, no migrations), existing Parchment Sepia CSS (`wwwroot/assets/css/main.css`), vanilla JS/Fetch.

**Spec:** `docs/superpowers/specs/2026-09-05-frontend-redesign-design.md` (authoritative design) + `UI.md` v2 (page inventory) + `.agent/spec/requirements.md` (FR-1..24).

## Global Constraints (apply to EVERY task)

1. **Never break existing flows:** `/Case/Submit`, `/Case/Result`, `/Case/Track`, `/Search`, `/Category`, `/Account/*`, `/Admin/Dashboard`, `/Admin/Analytics`, `/Lawyer/Queue`, `/Lawyer/Review` must keep compiling and serving. Additions only, except where a task says "replace".
2. **Clean Architecture:** Domain = zero new package deps; Application = services/DTOs; Infrastructure = EF configs; Web = controllers/views/VMs. Follow the file paths each task specifies.
3. **No migrations.** Schema goes into `scripts/08_redesign_tables.sql` (executed manually in SSMS by the human). EF maps onto it via `IEntityTypeConfiguration` classes, same pattern as existing `src/MuktoAin.Infrastructure/Data/Configurations/*.cs`.
4. **Bangla-first copy** with `data-bn`/`data-en` attributes (the existing i18n engine in `wwwroot/assets/js/main.js` swaps on `localStorage mkt-lang`).
5. **Icons: Lucide only** (`<i data-lucide="..."></i>`). No emojis as icons. No new CSS/JS frameworks.
6. **Existing CSS classes are the design system** — `.chat-shell .chat-scroll .chat-inner .chat-welcome .bubble .user .ai .answer-card .quick-replies .draft-card .identity-bar .composer-wrap .composer .composer-mode .composer-row .send-btn .mini-footer .citation-chip .chip .chip-row .badge .badge-* .timeline .t-step .t-dot .paper-sheet .paper-watermark .doc-stamp .doc-meta .item-ico .item-row .list-rows .empty-state .fab .breadcrumbs .page-head .kicker .page-title .page-sub .section-h .card .btn .btn-primary .btn-gold .btn-outline .btn-ghost .btn-quiet .btn-sm .btn-block .input .mono .muted .tiny .serif .alert .alert-warn .alert-danger .modal-backdrop .modal .modal-handle .modal-head .divider .row .spread .grid .grid-3 .grid-4 .wrap .chip-sm .acc .details` — all exist in `main.css`. Use them; the ONLY new classes this plan introduces are `.recent-chats`, `.quota-note`, `.answer-cached`, `.unread-dot` (CSS given in Task A5/A7).
7. **Antiforgery:** every form POST uses the `form` tag helper (auto-token) and every POST action has `[ValidateAntiForgeryToken]`. AJAX JSON POSTs to `/Chat/*` are exempt (API-style).
8. **Commit after every green step.** Exact commit commands given.
9. **Build command:** `dotnet build` at repo root. NEVER skip it; a step is only done when build succeeds.
10. Do NOT edit `plans/Dependency_plan.md` — OpenCode handles progress tracking at verify gates.

---

### Task A1: Domain entities for chat, quota cache, payments

**Files:**
- Create: `src/MuktoAin.Domain/Enums/ChatSessionStatus.cs`
- Create: `src/MuktoAin.Domain/Enums/PaymentPurpose.cs`
- Create: `src/MuktoAin.Domain/Enums/PaymentStatus.cs`
- Create: `src/MuktoAin.Domain/Entities/ChatSession.cs`
- Create: `src/MuktoAin.Domain/Entities/ChatMessage.cs`
- Create: `src/MuktoAin.Domain/Entities/AnswerCache.cs`
- Create: `src/MuktoAin.Domain/Entities/PaymentOrder.cs`
- Create: `src/MuktoAin.Domain/Entities/PayoutRequest.cs`
- Modify: `src/MuktoAin.Domain/Entities/Case.cs` (add 3 properties)
- Modify: `src/MuktoAin.Domain/Entities/GeneratedDocument.cs` (add 3 properties)

**Interfaces (Produces for later tasks):**
- Entities `ChatSession`, `ChatMessage`, `AnswerCache`, `PaymentOrder`, `PayoutRequest` with the exact property names below.
- Enums `ChatSessionStatus { InProgress = 0, Committed = 1 }`, `PaymentPurpose { TopUp = 0, Honorarium = 1 }`, `PaymentStatus { Pending = 0, Paid = 1, Failed = 2, Refunded = 3 }`.
- `Case.NotificationEmail` (string?), `Case.HasUnreadActivity` (bool), `Case.HonorariumPaid` (bool).
- `GeneratedDocument.VersionNo` (int, default 1), `GeneratedDocument.ClaimedAt` (DateTime?), `GeneratedDocument.CitizenEdited` (bool).

- [ ] **Step 1: Create the three enums**

`src/MuktoAin.Domain/Enums/ChatSessionStatus.cs`:
```csharp
namespace MuktoAin.Domain.Enums;

public enum ChatSessionStatus
{
    InProgress = 0,
    Committed = 1
}
```

`src/MuktoAin.Domain/Enums/PaymentPurpose.cs`:
```csharp
namespace MuktoAin.Domain.Enums;

public enum PaymentPurpose
{
    TopUp = 0,
    Honorarium = 1
}
```

`src/MuktoAin.Domain/Enums/PaymentStatus.cs`:
```csharp
namespace MuktoAin.Domain.Enums;

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Refunded = 3
}
```

- [ ] **Step 2: Create ChatSession entity**

`src/MuktoAin.Domain/Entities/ChatSession.cs`:
```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// A citizen chat conversation on the home page. InProgress sessions are
// resumable from the recent-chats strip; Committed sessions have become
// cases and their transcript stays attached to the case forever.
public class ChatSession
{
    public int ChatSessionId { get; set; }

    // NULL for guest sessions — guests are matched by SessionKey instead
    public int? UserId { get; set; }
    public User? User { get; set; }

    // Random 22-char key kept in the guest's browser session (ASP.NET session
    // value "mkt-chatkey"). Unique constraint in DB.
    public string? SessionKey { get; set; }

    public string Title { get; set; } = string.Empty;

    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.InProgress;

    public int? CommittedCaseId { get; set; }
    public Case? CommittedCase { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
```

- [ ] **Step 3: Create ChatMessage entity**

`src/MuktoAin.Domain/Entities/ChatMessage.cs`:
```csharp
namespace MuktoAin.Domain.Entities;

public class ChatMessage
{
    public int ChatMessageId { get; set; }
    public int ChatSessionId { get; set; }
    public ChatSession ChatSession { get; set; } = null!;

    // "user" or "assistant"
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    // JSON array of { "sectionId": n, "actTitle": "...", "sectionNumber": "..." }
    public string? CitedJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Create AnswerCache entity**

`src/MuktoAin.Domain/Entities/AnswerCache.cs`:
```csharp
namespace MuktoAin.Domain.Entities;

// Normalized-query hash -> cached AI answer (quota ladder tier 0).
// Repeat questions are served without spending Gemini quota.
public class AnswerCache
{
    public int AnswerCacheId { get; set; }

    // SHA-256 hex of the normalized question
    public string QueryHash { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    public string? CitedJson { get; set; }

    public int HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 5: Create PaymentOrder entity**

`src/MuktoAin.Domain/Entities/PaymentOrder.cs`:
```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// Sandbox payment (FR-24). Honorarium orders carry the lawyer split on the
// order row itself (gross / commission / net — single-table ledger).
public class PaymentOrder
{
    public int PaymentOrderId { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    // The case the honorarium belongs to (null for TopUp)
    public int? CaseId { get; set; }
    public Case? Case { get; set; }

    // The lawyer receiving the net (null for TopUp)
    public int? LawyerProfileId { get; set; }
    public LawyerProfile? LawyerProfile { get; set; }

    public PaymentPurpose Purpose { get; set; }
    public PaymentStatus Status { get; set; }

    // All amounts in BDT
    public decimal Amount { get; set; }
    public decimal Commission { get; set; }
    public decimal NetToLawyer { get; set; }

    public string? GatewayRef { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}
```

- [ ] **Step 6: Create PayoutRequest entity**

`src/MuktoAin.Domain/Entities/PayoutRequest.cs`:
```csharp
namespace MuktoAin.Domain.Entities;

// Lawyer payout request ("পরিশোধ চান") — approved by admin (sandbox marks paid).
public class PayoutRequest
{
    public int PayoutRequestId { get; set; }

    public int LawyerProfileId { get; set; }
    public LawyerProfile LawyerProfile { get; set; } = null!;

    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }

    public DateTime RequestedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
```

- [ ] **Step 7: Extend Case entity — add 3 properties**

In `src/MuktoAin.Domain/Entities/Case.cs`, insert immediately after the `AnonymousTrackingCode` property (change nothing else):

```csharp
    // Redesign 2026-09: optional notification email for anonymous cases
    // ("no account created" — used ONLY for status-change emails, FR-22)
    public string? NotificationEmail { get; set; }

    // Redesign 2026-09: set when a lawyer acts (claim/decision); cleared
    // when the citizen opens the case page (unread dot on My Cases)
    public bool HasUnreadActivity { get; set; }

    // Redesign 2026-09 (FR-24): honorarium paid marker for approved case
    public bool HonorariumPaid { get; set; }
```

- [ ] **Step 8: Extend GeneratedDocument entity — add 3 properties**

In `src/MuktoAin.Domain/Entities/GeneratedDocument.cs`, insert immediately after the `AssignedLawyerProfile` property (change nothing else):

```csharp
    // Redesign 2026-09 (FR-21): draft version chain. 1 = immutable AI original;
    // citizen saves increment. Latest citizen edit lives in ContentFinal.
    public int VersionNo { get; set; } = 1;

    // Redesign 2026-09 (FR-23): when this document was claimed from the pool
    public DateTime? ClaimedAt { get; set; }

    // Redesign 2026-09 (FR-21): current citizen edit differs from the AI original
    public bool CitizenEdited { get; set; }
```

- [ ] **Step 9: Build**

Run: `dotnet build`
Expected: `Build succeeded` (0 errors; warnings OK).

- [ ] **Step 10: Commit**

```bash
git add src/MuktoAin.Domain
git commit -m "feat(domain): chat/payment/cache entities + Case/GeneratedDocument redesign columns"
```

---

### Task A2: SSMS schema script + EF mappings

**Files:**
- Create: `scripts/08_redesign_tables.sql`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/ChatSessionConfiguration.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/ChatMessageConfiguration.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/AnswerCacheConfiguration.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/PaymentOrderConfiguration.cs`
- Create: `src/MuktoAin.Infrastructure/Data/Configurations/PayoutRequestConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/CaseConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/GeneratedDocumentConfiguration.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/AppDbContext.cs` (add 5 DbSets)

**Interfaces (Produces):** DbSets `ChatSessions`, `ChatMessages`, `AnswerCaches`, `PaymentOrders`, `PayoutRequests`; mapped columns for the 6 new CASE/GENERATED_DOCUMENT properties. Tasks A3+ query these.

- [ ] **Step 1: Write scripts/08_redesign_tables.sql (full content)**

```sql
/* ============================================================
   MuktoAin — Frontend Redesign additive schema (2026-09-05)
   Chat sessions/messages, answer cache, sandbox payments,
   CASE / GENERATED_DOCUMENT extension columns.
   IDEMPOTENT: safe to re-run; every CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[CHAT_SESSION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHAT_SESSION] (
        ChatSessionId    INT IDENTITY(1,1) NOT NULL,
        UserId           INT                NULL,
        SessionKey       NVARCHAR(64)       NULL,
        Title            NVARCHAR(250)      NOT NULL DEFAULT (N''),
        Status           INT                NOT NULL DEFAULT (0),
        CommittedCaseId  INT                NULL,
        CreatedAt        DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CHAT_SESSION PRIMARY KEY (ChatSessionId),
        CONSTRAINT FK_CHAT_SESSION_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_CHAT_SESSION_Case FOREIGN KEY (CommittedCaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT UQ_CHAT_SESSION_SessionKey UNIQUE (SessionKey)
    );
    CREATE INDEX IX_CHAT_SESSION_UserId ON [dbo].[CHAT_SESSION] (UserId);
    CREATE INDEX IX_CHAT_SESSION_Status ON [dbo].[CHAT_SESSION] (Status);
END
GO

IF OBJECT_ID(N'[dbo].[CHAT_MESSAGE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHAT_MESSAGE] (
        ChatMessageId   INT IDENTITY(1,1) NOT NULL,
        ChatSessionId   INT                NOT NULL,
        Role            NVARCHAR(16)       NOT NULL DEFAULT (N'user'),
        Content         NVARCHAR(MAX)      NOT NULL,
        CitedJson       NVARCHAR(MAX)      NULL,
        CreatedAt       DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CHAT_MESSAGE PRIMARY KEY (ChatMessageId),
        CONSTRAINT FK_CHAT_MESSAGE_Session FOREIGN KEY (ChatSessionId)
            REFERENCES [dbo].[CHAT_SESSION] (ChatSessionId) ON DELETE CASCADE
    );
    CREATE INDEX IX_CHAT_MESSAGE_Session ON [dbo].[CHAT_MESSAGE] (ChatSessionId);
END
GO

IF OBJECT_ID(N'[dbo].[ANSWER_CACHE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ANSWER_CACHE] (
        AnswerCacheId  INT IDENTITY(1,1) NOT NULL,
        QueryHash      CHAR(64)           NOT NULL,
        Question       NVARCHAR(500)      NOT NULL,
        Answer         NVARCHAR(MAX)      NOT NULL,
        CitedJson      NVARCHAR(MAX)      NULL,
        HitCount       INT                NOT NULL DEFAULT (0),
        CreatedAt      DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ANSWER_CACHE PRIMARY KEY (AnswerCacheId),
        CONSTRAINT UQ_ANSWER_CACHE_QueryHash UNIQUE (QueryHash)
    );
END
GO

IF OBJECT_ID(N'[dbo].[PAYMENT_ORDER]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAYMENT_ORDER] (
        PaymentOrderId  INT IDENTITY(1,1) NOT NULL,
        UserId          INT                NULL,
        CaseId          INT                NULL,
        LawyerProfileId INT                NULL,
        Purpose         INT                NOT NULL DEFAULT (0),
        Status          INT                NOT NULL DEFAULT (0),
        Amount          DECIMAL(10,2)     NOT NULL DEFAULT (0),
        Commission      DECIMAL(10,2)     NOT NULL DEFAULT (0),
        NetToLawyer     DECIMAL(10,2)     NOT NULL DEFAULT (0),
        GatewayRef      NVARCHAR(200)     NULL,
        CreatedAt       DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        PaidAt          DATETIME2         NULL,
        RefundedAt      DATETIME2         NULL,
        CONSTRAINT PK_PAYMENT_ORDER PRIMARY KEY (PaymentOrderId),
        CONSTRAINT FK_PAYMENT_ORDER_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_PAYMENT_ORDER_Case FOREIGN KEY (CaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_PAYMENT_ORDER_LawyerProfile FOREIGN KEY (LawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
    CREATE INDEX IX_PAYMENT_ORDER_Status ON [dbo].[PAYMENT_ORDER] (Status);
    CREATE INDEX IX_PAYMENT_ORDER_LawyerProfileId ON [dbo].[PAYMENT_ORDER] (LawyerProfileId);
END
GO

IF OBJECT_ID(N'[dbo].[PAYOUT_REQUEST]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAYOUT_REQUEST] (
        PayoutRequestId INT IDENTITY(1,1) NOT NULL,
        LawyerProfileId INT                NOT NULL,
        Amount          DECIMAL(10,2)     NOT NULL DEFAULT (0),
        IsPaid          BIT               NOT NULL DEFAULT (0),
        RequestedAt     DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        PaidAt          DATETIME2         NULL,
        CONSTRAINT PK_PAYOUT_REQUEST PRIMARY KEY (PayoutRequestId),
        CONSTRAINT FK_PAYOUT_REQUEST_LawyerProfile FOREIGN KEY (LawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
END
GO

/* ---------- CASE extension columns (additive, idempotent) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'NotificationEmail')
    ALTER TABLE [dbo].[CASE] ADD NotificationEmail NVARCHAR(256) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'HasUnreadActivity')
    ALTER TABLE [dbo].[CASE] ADD HasUnreadActivity BIT NOT NULL DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'HonorariumPaid')
    ALTER TABLE [dbo].[CASE] ADD HonorariumPaid BIT NOT NULL DEFAULT (0);
GO

/* ---------- GENERATED_DOCUMENT extension columns ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'VersionNo')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD VersionNo INT NOT NULL DEFAULT (1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'ClaimedAt')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD ClaimedAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'CitizenEdited')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD CitizenEdited BIT NOT NULL DEFAULT (0);
GO
```

- [ ] **Step 2: Create the five EF configurations**

`src/MuktoAin.Infrastructure/Data/Configurations/ChatSessionConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class ChatSessionConfiguration : IEntityTypeConfiguration<ChatSession>
{
    public void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.ToTable("CHAT_SESSION", "dbo");
        builder.HasKey(s => s.ChatSessionId);
        builder.HasIndex(s => s.SessionKey).IsUnique();
    }
}
```

`src/MuktoAin.Infrastructure/Data/Configurations/ChatMessageConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("CHAT_MESSAGE", "dbo");
        builder.HasKey(m => m.ChatMessageId);
    }
}
```

`src/MuktoAin.Infrastructure/Data/Configurations/AnswerCacheConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class AnswerCacheConfiguration : IEntityTypeConfiguration<AnswerCache>
{
    public void Configure(EntityTypeBuilder<AnswerCache> builder)
    {
        builder.ToTable("ANSWER_CACHE", "dbo");
        builder.HasKey(a => a.AnswerCacheId);
        builder.HasIndex(a => a.QueryHash).IsUnique();
    }
}
```

`src/MuktoAin.Infrastructure/Data/Configurations/PaymentOrderConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.ToTable("PAYMENT_ORDER", "dbo");
        builder.HasKey(o => o.PaymentOrderId);
    }
}
```

`src/MuktoAin.Infrastructure/Data/Configurations/PayoutRequestConfiguration.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class PayoutRequestConfiguration : IEntityTypeConfiguration<PayoutRequest>
{
    public void Configure(EntityTypeBuilder<PayoutRequest> builder)
    {
        builder.ToTable("PAYOUT_REQUEST", "dbo");
        builder.HasKey(p => p.PayoutRequestId);
    }
}
```

- [ ] **Step 3: Extend the two existing configurations**

Replace the ENTIRE content of `src/MuktoAin.Infrastructure/Data/Configurations/CaseConfiguration.cs` with:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("CASE", "dbo");
        builder.HasKey(c => c.CaseId);
        // Redesign columns (scripts/08_redesign_tables.sql) — additive
        builder.Property(c => c.NotificationEmail).HasMaxLength(256);
        builder.Property(c => c.HasUnreadActivity).HasDefaultValue(false);
        builder.Property(c => c.HonorariumPaid).HasDefaultValue(false);
    }
}
```

Replace the ENTIRE content of `src/MuktoAin.Infrastructure/Data/Configurations/GeneratedDocumentConfiguration.cs` with:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class GeneratedDocumentConfiguration : IEntityTypeConfiguration<GeneratedDocument>
{
    public void Configure(EntityTypeBuilder<GeneratedDocument> builder)
    {
        builder.ToTable("GENERATED_DOCUMENT", "dbo");
        builder.HasKey(d => d.DocumentId);
        // Redesign columns (scripts/08_redesign_tables.sql) — additive
        builder.Property(d => d.VersionNo).HasDefaultValue(1);
        builder.Property(d => d.CitizenEdited).HasDefaultValue(false);
    }
}
```

- [ ] **Step 4: Add the 5 DbSets to AppDbContext**

In `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`, immediately after the line `public DbSet<AiLog> AiLogs => Set<AiLog>();` add:
```csharp
    // Redesign 2026-09 (scripts/08_redesign_tables.sql)
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AnswerCache> AnswerCaches => Set<AnswerCache>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PayoutRequest> PayoutRequests => Set<PayoutRequest>();
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add scripts/08_redesign_tables.sql src/MuktoAin.Infrastructure
git commit -m "feat(infra): redesign schema script + EF configs (chat/cache/payments)"
```

### Task A3: ChatService + AiBudgetService (Application layer)

**Files:**
- Create: `src/MuktoAin.Application/DTOs/ChatDto.cs`
- Create: `src/MuktoAin.Application/Services/AiBudgetService.cs`
- Create: `src/MuktoAin.Application/Services/ChatService.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (2 DI lines)

**Interfaces:**
- Consumes: `IRightsExplanationService.ExplainRightsAsync(Case, CancellationToken)` (namespace `MuktoAin.Application.Services`); `IRepository<T>`; entities from A1; `IScenarioMappingRepository.GetAllAsync()`; `IKeywordSectionSearch` (Domain/Interfaces/Services — see Step 5 for signature adaptation); `DocumentService.GenerateDocumentAsync(int caseId, RightsExplanationDto)`; `ICaseRepository.GetWithDocumentsAsync(int)`; `IEncryptionService.Encrypt/Decrypt` (namespace `MuktoAin.Domain.Interfaces`).
- Produces (used by A4/A6): `ChatService` public API exactly as written below (esp. const `ChatService.ChatTurnMarker = "[MKT-CHAT-TURN]"`, `AskAsync`, `CommitToCaseAsync`, `GetOrCreateSessionAsync`, `GetRecentAsync`, `GetMessagesAsync`, `AppendMessageAsync`, `GetSessionAsync`); `AiBudgetService` (`GetRemainingToday`, `TryReserveTurnAsync`, `RecordTurnUsed`, `DailyLimitFor`); DTOs `ChatTurnDto`, `ChatMessageDto`, `RecentChatDto`, `ChatCommitResultDto`, `QuotaSnapshotDto`.

- [ ] **Step 1: Create the chat DTOs**

`src/MuktoAin.Application/DTOs/ChatDto.cs`:
```csharp
namespace MuktoAin.Application.DTOs;

// One assistant answer rendered in the chat thread
public record ChatTurnDto(
    string Answer,
    IReadOnlyList<CitedSectionDto> CitedSections,
    string Disclaimer,
    bool FromCache,
    bool RetrievalOnly,
    string Tier // "full" | "capped" | "retrieval-only" | "wall"
);

public record ChatMessageDto(
    int ChatMessageId,
    string Role,
    string Content,
    string? CitedJson
);

public record RecentChatDto(
    int ChatSessionId,
    string Title,
    DateTime UpdatedAt,
    int MessageCount
);

public record ChatCommitResultDto(
    int CaseId,
    string? AnonymousTrackingCode,
    int DocumentId,
    string DocumentContent
);

public record QuotaSnapshotDto(
    int RemainingToday,
    int DailyLimit,
    bool IsLoggedIn
);
```

- [ ] **Step 2: Create AiBudgetService**

`src/MuktoAin.Application/Services/AiBudgetService.cs`:
```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// FR-20 quota meter. Chat turns are identified in AI_LOG by the ChatTurnMarker
// prefix on the prompt (see ChatService). Guests ~10/day per browser session,
// signed-in ~30/day. Resets at midnight Pacific (matches Gemini RPD reset).
// Draft-generation prompts do NOT carry the marker, so they never count here.
public class AiBudgetService
{
    private const int GuestDailyLimit = 10;
    private const int SignedInDailyLimit = 30;

    private readonly IRepository<AiLog> _logRepo;

    public AiBudgetService(IRepository<AiLog> logRepo)
    {
        _logRepo = logRepo;
    }

    private static bool IsPacificDaylight =>
        DateTime.UtcNow.Month > 3 && DateTime.UtcNow.Month < 11;

    // UTC instant of the most recent midnight Pacific (approximation is fine
    // for a quota meter; Google's exact reset instant is not contractual).
    private static DateTime PacificMidnightUtc()
    {
        var ptTodayMidnight = DateTime.UtcNow.AddHours(IsPacificDaylight ? -7 : -8).Date;
        var asUtc = ptTodayMidnight.AddHours(IsPacificDaylight ? 7 : 8);
        return asUtc > DateTime.UtcNow ? asUtc.AddDays(-1) : asUtc;
    }

    public int DailyLimitFor(bool isLoggedIn) =>
        isLoggedIn ? SignedInDailyLimit : GuestDailyLimit;

    public async Task<QuotaSnapshotDto> GetRemainingToday(int? userId, string? sessionKey)
    {
        var since = PacificMidnightUtc();
        var logs = await _logRepo.GetAllAsync();
        var used = logs.Count(l =>
            l.CreatedAt >= since
            && l.RequestType == AiRequestType.RightsExplanation
            && l.PromptText.StartsWith(ChatService.ChatTurnMarker, StringComparison.Ordinal));
        var limit = DailyLimitFor(userId.HasValue);
        return new QuotaSnapshotDto(Math.Max(0, limit - used), limit, userId.HasValue);
    }

    public async Task<bool> TryReserveTurnAsync(int? userId, string? sessionKey)
    {
        var snapshot = await GetRemainingToday(userId, sessionKey);
        return snapshot.RemainingToday > 0;
    }

    public Task<QuotaSnapshotDto> RecordTurnUsed(int? userId, string? sessionKey)
    {
        // The turn was already logged to AI_LOG by the orchestration pipeline;
        // metering reads the log, so this is a read-back only.
        return GetRemainingToday(userId, sessionKey);
    }
}
```

- [ ] **Step 3: Create ChatService (full file)**

`src/MuktoAin.Application/Services/ChatService.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

// Chat-first home (FR-19). Sessions stay InProgress until the citizen presses
// [Generate Draft]; CommitToCaseAsync then creates the case + document and
// flips the session to Committed. Chat turns run on an UNSAVED Case (CaseId=0)
// so the shared pipeline's case-scoped DB writes stay inert until commit.
public class ChatService
{
    // Prefixes every chat-turn prompt so AiBudgetService can meter chat turns
    // separately from case-critical RightsExplanation calls.
    public const string ChatTurnMarker = "[MKT-CHAT-TURN]";

    private const int MaxRecentChats = 8;

    private readonly IRepository<ChatSession> _sessionRepo;
    private readonly IRepository<ChatMessage> _messageRepo;
    private readonly IRepository<Case> _caseRepo;
    private readonly ICaseRepository _caseRepoTyped;
    private readonly IRepository<AnswerCache> _cacheRepo;
    private readonly IRightsExplanationService _rightsService;
    private readonly DocumentService _documentService;
    private readonly IEncryptionService _encryptionService;
    private readonly IScenarioMappingRepository _scenarioRepo;
    private readonly IKeywordSectionSearch _keywordSearch;

    public ChatService(
        IRepository<ChatSession> sessionRepo,
        IRepository<ChatMessage> messageRepo,
        IRepository<Case> caseRepo,
        ICaseRepository caseRepoTyped,
        IRepository<AnswerCache> cacheRepo,
        IRightsExplanationService rightsService,
        DocumentService documentService,
        IEncryptionService encryptionService,
        IScenarioMappingRepository scenarioRepo,
        IKeywordSectionSearch keywordSearch)
    {
        _sessionRepo = sessionRepo;
        _messageRepo = messageRepo;
        _caseRepo = caseRepo;
        _caseRepoTyped = caseRepoTyped;
        _cacheRepo = cacheRepo;
        _rightsService = rightsService;
        _documentService = documentService;
        _encryptionService = encryptionService;
        _scenarioRepo = scenarioRepo;
        _keywordSearch = keywordSearch;
    }

    // ---------- session management ----------

    public async Task<ChatSession> GetOrCreateSessionAsync(int? userId, string? sessionKey, string? firstMessage)
    {
        var all = await _sessionRepo.GetAllAsync();
        ChatSession? existing = null;
        if (userId.HasValue)
        {
            existing = all.Where(s => s.UserId == userId
                                 && s.Status == ChatSessionStatus.InProgress)
                          .OrderByDescending(s => s.UpdatedAt)
                          .FirstOrDefault();
        }
        else if (!string.IsNullOrEmpty(sessionKey))
        {
            existing = all.Where(s => s.SessionKey == sessionKey
                                 && s.Status == ChatSessionStatus.InProgress)
                          .OrderByDescending(s => s.UpdatedAt)
                          .FirstOrDefault();
        }
        if (existing != null) return existing;

        var session = new ChatSession
        {
            UserId = userId,
            SessionKey = userId.HasValue ? null : (sessionKey ?? Guid.NewGuid().ToString("N")[..22]),
            Title = BuildTitle(firstMessage),
            Status = ChatSessionStatus.InProgress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _sessionRepo.AddAsync(session);
        await _sessionRepo.SaveChangesAsync();
        return session;
    }

    private static string BuildTitle(string? firstMessage)
    {
        if (string.IsNullOrWhiteSpace(firstMessage)) return "নতুন আলোচনা";
        return firstMessage.Length > 60 ? firstMessage[..60] + "…" : firstMessage;
    }

    public async Task<IReadOnlyList<RecentChatDto>> GetRecentAsync(int? userId, string? sessionKey)
    {
        var all = await _sessionRepo.GetAllAsync();
        IEnumerable<ChatSession> inProgress = all.Where(s => s.Status == ChatSessionStatus.InProgress);

        if (userId.HasValue)
            inProgress = inProgress.Where(s => s.UserId == userId);
        else if (!string.IsNullOrEmpty(sessionKey))
            inProgress = inProgress.Where(s => s.SessionKey == sessionKey);
        else
            return new List<RecentChatDto>();

        var recent = inProgress.OrderByDescending(s => s.UpdatedAt).Take(MaxRecentChats).ToList();
        var messages = await _messageRepo.GetAllAsync();
        var ids = recent.Select(s => s.ChatSessionId).ToHashSet();
        var counts = messages.Where(m => ids.Contains(m.ChatSessionId))
                             .GroupBy(m => m.ChatSessionId)
                             .ToDictionary(g => g.Key, g => g.Count());

        return recent.Select(s => new RecentChatDto(
            s.ChatSessionId,
            s.Title,
            s.UpdatedAt,
            counts.TryGetValue(s.ChatSessionId, out var c) ? c : 0)).ToList();
    }

    public async Task<ChatSession?> GetSessionAsync(int chatSessionId)
        => await _sessionRepo.GetByIdAsync(chatSessionId);

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(int chatSessionId)
    {
        var messages = await _messageRepo.GetAllAsync();
        return messages
            .Where(m => m.ChatSessionId == chatSessionId)
            .OrderBy(m => m.ChatMessageId)
            .Select(m => new ChatMessageDto(m.ChatMessageId, m.Role, m.Content, m.CitedJson))
            .ToList();
    }

    public async Task AppendMessageAsync(int chatSessionId, string role, string content, string? citedJson)
    {
        await _messageRepo.AddAsync(new ChatMessage
        {
            ChatSessionId = chatSessionId,
            Role = role,
            Content = content,
            CitedJson = citedJson,
            CreatedAt = DateTime.UtcNow
        });
        await _messageRepo.SaveChangesAsync();

        var session = await _sessionRepo.GetByIdAsync(chatSessionId);
        if (session != null)
        {
            session.UpdatedAt = DateTime.UtcNow;
            if (session.Title == "নতুন আলোচনা" && role == "user")
                session.Title = BuildTitle(content);
            await _sessionRepo.SaveChangesAsync();
        }
    }

    // ---------- asking (quota ladder) ----------

    public async Task<ChatTurnDto> AskAsync(
        int chatSessionId,
        string question,
        string language,
        bool allowCapped,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question required", nameof(question));

        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        // Tier 0 — answer cache
        var hash = HashQuestion(NormalizeQuestion(question));
        var cached = (await _cacheRepo.GetAllAsync()).FirstOrDefault(a => a.QueryHash == hash);
        if (cached != null)
        {
            cached.HitCount++;
            await _cacheRepo.SaveChangesAsync();
            return new ChatTurnDto(cached.Answer, ParseCitedJson(cached.CitedJson),
                DisclaimersFor(language), FromCache: true, RetrievalOnly: false, Tier: "full");
        }

        // Unsaved Case — CaseId = 0 keeps the pipeline's case-scoped writes inert.
        var chatCase = new Case
        {
            CaseId = 0,
            UserId = session.UserId,
            CategoryId = 1,
            DistrictId = 1,
            Title = "Chat",
            Description = ChatTurnMarker + "\n" + question,
            Language = language == "en" ? "en" : "bn",
            Status = CaseStatus.Submitted,
            IsAnonymous = session.UserId == null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        RightsExplanationDto explanation;
        try
        {
            explanation = await _rightsService.ExplainRightsAsync(chatCase, ct);
        }
        catch
        {
            // Tier 2 — retrieval-only answer (AI down / quota exhausted mid-flight)
            return await BuildRetrievalOnlyAnswerAsync(question, language);
        }

        await _cacheRepo.AddAsync(new AnswerCache
        {
            QueryHash = hash,
            Question = question.Length > 480 ? question[..480] : question,
            Answer = explanation.Explanation,
            CitedJson = BuildCitedJson(explanation.CitedSections),
            HitCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        await _cacheRepo.SaveChangesAsync();

        return new ChatTurnDto(
            explanation.Explanation,
            explanation.CitedSections,
            explanation.Disclaimer,
            FromCache: false,
            RetrievalOnly: false,
            Tier: allowCapped ? "capped" : "full");
    }

    private async Task<ChatTurnDto> BuildRetrievalOnlyAnswerAsync(string question, string language)
    {
        var mappings = await _scenarioRepo.GetAllAsync();
        var q = question.Trim();
        var hits = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.ScenarioKeyword)
                        && q.Contains(m.ScenarioKeyword, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        var sections = new List<CitedSectionDto>();
        foreach (var m in hits)
        {
            var found = await _keywordSearch.SearchSectionsAsync(m.ScenarioKeyword, 2);
            foreach (var r in found)
            {
                if (sections.All(s => s.SectionId != r.SectionId)) sections.Add(r);
            }
            if (sections.Count >= 5) break;
        }
        if (sections.Count == 0)
        {
            var words = string.Join(" ", q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
            if (!string.IsNullOrWhiteSpace(words))
                sections.AddRange(await _keywordSearch.SearchSectionsAsync(words, 5));
        }

        var header = language == "en"
            ? "AI is unavailable right now. Relevant statutory sections found by keyword search:"
            : "AI এই মুহূর্তে উপলব্ধ নয়। কীওয়ার্ড অনুসন্ধানে প্রাপ্ত প্রাসঙ্গিক ধারাসমূহ:";
        var body = new StringBuilder(header).Append('\n');
        foreach (var s in sections)
        {
            body.Append("• ").Append(s.ActTitle);
            if (!string.IsNullOrWhiteSpace(s.SectionNumber)) body.Append(" — ধারা ").Append(s.SectionNumber);
            body.Append('\n');
            body.Append(s.SectionText.Length > 300 ? s.SectionText[..300] + "…" : s.SectionText);
            body.Append("\n\n");
        }
        if (sections.Count == 0)
        {
            body.Clear().Append(language == "en"
                ? "No relevant sections found. Please try different keywords."
                : "প্রাসঙ্গিক কোনো ধারা পাওয়া যায়নি। ভিন্ন শব্দ দিয়ে চেষ্টা করুন।");
        }

        return new ChatTurnDto(body.ToString(), sections, DisclaimersFor(language),
            FromCache: false, RetrievalOnly: true, Tier: "retrieval-only");
    }

    // ---------- commit ([Generate Draft]) ----------

    public async Task<ChatCommitResultDto> CommitToCaseAsync(
        int chatSessionId,
        int categoryId,
        byte districtId,
        string title,
        string? notificationEmail,
        bool isAnonymous,
        int? userId,
        string documentType,
        CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(chatSessionId)
                      ?? throw new ArgumentException("Session not found", nameof(chatSessionId));

        var messages = await GetMessagesAsync(chatSessionId);
        if (messages.Count == 0)
            throw new InvalidOperationException("Cannot commit an empty conversation");

        // Unified description = transcript (the form path writes its answers
        // into a chat session the same way — one transcript per case, always)
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            sb.Append(m.Role == "user" ? "নাগরিক: " : "সহায়ক: ").Append(m.Content).Append("\n\n");
        }
        var unifiedDescription = sb.ToString().Trim();

        string? trackingCode = isAnonymous || userId == null
            ? Guid.NewGuid().ToString("N")
            : null;

        var caseEntity = new Case
        {
            UserId = isAnonymous ? null : userId,
            CategoryId = categoryId,
            DistrictId = districtId,
            Title = _encryptionService.Encrypt(title),
            Description = _encryptionService.Encrypt(unifiedDescription),
            Language = "bn",
            Status = CaseStatus.Submitted,
            IsAnonymous = isAnonymous,
            AnonymousTrackingCode = trackingCode,
            NotificationEmail = string.IsNullOrWhiteSpace(notificationEmail) ? null : notificationEmail.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _caseRepo.AddAsync(caseEntity);
        await _caseRepo.SaveChangesAsync();

        // Case-critical generation (NOT metered as a chat turn — marker absent)
        var loaded = await _caseRepoTyped.GetWithDocumentsAsync(caseEntity.CaseId) ?? caseEntity;
        loaded.Title = title;
        loaded.Description = unifiedDescription;

        var explanation = await _rightsService.ExplainRightsAsync(loaded, ct);
        var doc = await _documentService.GenerateDocumentAsync(caseEntity.CaseId, explanation);

        session.Status = ChatSessionStatus.Committed;
        session.CommittedCaseId = caseEntity.CaseId;
        session.UpdatedAt = DateTime.UtcNow;
        await _sessionRepo.SaveChangesAsync();

        return new ChatCommitResultDto(caseEntity.CaseId, trackingCode, doc.DocumentId, doc.ContentDraft);
    }

    // ---------- helpers ----------

    public static string NormalizeQuestion(string question)
    {
        var lowered = question.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        var s = sb.ToString();
        s = s.Replace('\u09E6', '0').Replace('\u09E7', '1').Replace('\u09E8', '2')
             .Replace('\u09E9', '3').Replace('\u09EA', '4').Replace('\u09EB', '5')
             .Replace('\u09EC', '6').Replace('\u09ED', '7').Replace('\u09EE', '8')
             .Replace('\u09EF', '9');
        return s;
    }

    private static string HashQuestion(string normalized)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

    internal static string BuildCitedJson(IReadOnlyList<CitedSectionDto> sections)
    {
        var parts = sections.Select(s =>
            "{\"sectionId\":" + s.SectionId +
            ",\"actTitle\":\"" + EscapeJson(s.ActTitle) +
            "\",\"sectionNumber\":\"" + EscapeJson(s.SectionNumber) + "\"}");
        return "[" + string.Join(",", parts) + "]";
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string DisclaimersFor(string language)
        => MuktoAin.Domain.Constants.Disclaimers.ForLanguage(language == "en" ? "en" : "bn");

    internal static IReadOnlyList<CitedSectionDto> ParseCitedJson(string? citedJson)
    {
        if (string.IsNullOrWhiteSpace(citedJson)) return new List<CitedSectionDto>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(citedJson);
            var result = new List<CitedSectionDto>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                result.Add(new CitedSectionDto(
                    el.GetProperty("sectionId").GetInt32(),
                    el.GetProperty("actTitle").GetString() ?? string.Empty,
                    el.GetProperty("sectionNumber").GetString() ?? string.Empty,
                    SectionText: string.Empty,
                    RelevanceScore: 0,
                    RetrievalMethod: "Cache",
                    ActNumber: string.Empty,
                    ActYear: 0));
            }
            return result;
        }
        catch
        {
            return new List<CitedSectionDto>();
        }
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/MuktoAin.Web/Program.cs`, immediately after `builder.Services.AddScoped<LawyerVerificationService>();` add:
```csharp
// Frontend redesign 2026-09: chat-first home + AI budget
builder.Services.AddScoped<AiBudgetService>();
builder.Services.AddScoped<ChatService>();
```

- [ ] **Step 5: Verify IKeywordSectionSearch signature**

Open `src/MuktoAin.Domain/Interfaces/Services/IKeywordSectionSearch.cs`. The code in `BuildRetrievalOnlyAnswerAsync` calls `SearchSectionsAsync(string query, int maxResults)` expecting items with `SectionId`, `ActTitle`, `SectionNumber`, `SectionText`. If the real interface instead returns `IEnumerable<RetrievedSection>` (Domain/Models, fields `SectionId, ActTitle, SectionNumber, SectionText, RelevanceScore, Method, ActNumber, ActYear`), adapt ONLY that method: map each `RetrievedSection` into a `CitedSectionDto` exactly the way `RightsExplanationService.ExplainRightsAsync` does. Do NOT change the interface.

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/MuktoAin.Application/DTOs/ChatDto.cs src/MuktoAin.Application/Services/AiBudgetService.cs src/MuktoAin.Application/Services/ChatService.cs src/MuktoAin.Web/Program.cs
git commit -m "feat(app): ChatService (sessions/ask/ladder/commit) + AiBudgetService quota meter"
```

---
### Task A4: ChatController (AJAX endpoints)

**Files:**
- Create: `src/MuktoAin.Web/Controllers/ChatController.cs`

**Interfaces:**
- Consumes: `ChatService` + `AiBudgetService` (A3) with the exact public methods listed there; `ChatService.BuildCitedJson` is `internal` in the same assembly? NO — it is in `MuktoAin.Application`, so the controller serializes citations itself (code below includes its own `SerializeCited` — do not call ChatService.BuildCitedJson from Web).
- Produces: `POST /Chat/New`, `POST /Chat/Ask`, `GET /Chat/Messages?id=`, `GET /Chat/Recent`, `GET /Chat/Quota`, `POST /Chat/Commit` — JSON shapes consumed by chat.js (A5). Guest sessions keyed by ASP.NET session value `mkt-chatkey`.

- [ ] **Step 1: Create ChatController**

`src/MuktoAin.Web/Controllers/ChatController.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;

namespace MuktoAin.Web.Controllers;

// AJAX endpoints for the chat-first home page. Guests are keyed by the
// "mkt-chatkey" ASP.NET-session value (created on first contact).
[ApiController]
[Route("[controller]/[action]")]
public class ChatController : Controller
{
    private const string ChatKeySessionName = "mkt-chatkey";

    private readonly ChatService _chatService;
    private readonly AiBudgetService _budgetService;

    public ChatController(ChatService chatService, AiBudgetService budgetService)
    {
        _chatService = chatService;
        _budgetService = budgetService;
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private string? SessionKey()
    {
        var key = HttpContext.Session.GetString(ChatKeySessionName);
        if (string.IsNullOrEmpty(key))
        {
            key = Guid.NewGuid().ToString("N")[..22];
            HttpContext.Session.SetString(ChatKeySessionName, key);
        }
        return key;
    }

    // Start (or resume) a session. Body: { "firstMessage": "..." } (optional)
    [HttpPost]
    public async Task<IActionResult> New([FromBody] ChatNewRequest? body)
    {
        var session = await _chatService.GetOrCreateSessionAsync(
            CurrentUserId(), SessionKey(), body?.FirstMessage);
        return Json(new { chatSessionId = session.ChatSessionId, title = session.Title });
    }

    // Ask a question. Body: { chatSessionId, question, language? }
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatAskRequest? body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Question) || body.ChatSessionId <= 0)
            return BadRequest(new { error = "question and chatSessionId required" });

        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();

        if (!await _budgetService.TryReserveTurnAsync(userId, key))
        {
            var wall = await _budgetService.GetRemainingToday(userId, key);
            return Json(new
            {
                tier = "wall",
                remainingToday = wall.RemainingToday,
                dailyLimit = wall.DailyLimit
            });
        }

        var language = string.IsNullOrWhiteSpace(body.Language) ? "bn" : body.Language;
        var turn = await _chatService.AskAsync(body.ChatSessionId, body.Question, language, allowCapped: true);
        var quota = await _budgetService.RecordTurnUsed(userId, key);

        await _chatService.AppendMessageAsync(body.ChatSessionId, "user", body.Question, null);
        await _chatService.AppendMessageAsync(
            body.ChatSessionId, "assistant", turn.Answer, SerializeCited(turn.CitedSections));

        return Json(new
        {
            tier = turn.Tier,
            answer = turn.Answer,
            disclaimer = turn.Disclaimer,
            fromCache = turn.FromCache,
            retrievalOnly = turn.RetrievalOnly,
            citedSections = turn.CitedSections.Select(s => new
            {
                sectionId = s.SectionId,
                actTitle = s.ActTitle,
                sectionNumber = s.SectionNumber,
                sectionText = s.SectionText,
                relevance = Math.Round(s.RelevanceScore * 100) + "%"
            }),
            remainingToday = quota.RemainingToday,
            dailyLimit = quota.DailyLimit
        });
    }

    // Load a session's messages. Query: ?id=
    [HttpGet]
    public async Task<IActionResult> Messages(int id)
    {
        var session = await _chatService.GetSessionAsync(id);
        if (session == null) return NotFound();

        var userId = CurrentUserId();
        var key = SessionKey();
        var allowed = session.UserId == userId
                      || (session.UserId == null && session.SessionKey == key);
        if (!allowed) return Forbid();

        var messages = await _chatService.GetMessagesAsync(id);
        return Json(new
        {
            chatSessionId = id,
            title = session.Title,
            messages = messages.Select(m => new
            {
                role = m.Role, content = m.Content, citedJson = m.CitedJson
            })
        });
    }

    // Recent in-progress chats for the strip.
    [HttpGet]
    public async Task<IActionResult> Recent()
    {
        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();
        var recent = await _chatService.GetRecentAsync(userId, key);
        return Json(new
        {
            chats = recent.Select(r => new
            {
                chatSessionId = r.ChatSessionId,
                title = r.Title,
                updatedAt = r.UpdatedAt,
                messageCount = r.MessageCount
            })
        });
    }

    // Daily quota snapshot for the composer counter.
    [HttpGet]
    public async Task<IActionResult> Quota()
    {
        var userId = CurrentUserId();
        var key = userId.HasValue ? null : SessionKey();
        var snap = await _budgetService.GetRemainingToday(userId, key);
        return Json(new
        {
            remainingToday = snap.RemainingToday,
            dailyLimit = snap.DailyLimit,
            isLoggedIn = snap.IsLoggedIn
        });
    }

    // Generate Draft commit. Body: all modal fields.
    [HttpPost]
    public async Task<IActionResult> Commit([FromBody] ChatCommitRequest? body)
    {
        if (body == null || body.ChatSessionId <= 0 || body.CategoryId <= 0 || body.DistrictId <= 0)
            return BadRequest(new { error = "chatSessionId, categoryId, districtId required" });
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "title required" });

        try
        {
            var result = await _chatService.CommitToCaseAsync(
                body.ChatSessionId,
                body.CategoryId,
                body.DistrictId,
                body.Title,
                body.NotificationEmail,
                body.IsAnonymous,
                CurrentUserId(),
                body.DocumentType,
                HttpContext.RequestAborted);

            if (result.AnonymousTrackingCode != null)
                TempData["TrackingCode"] = result.AnonymousTrackingCode;

            return Json(new
            {
                caseId = result.CaseId,
                trackingCode = result.AnonymousTrackingCode,
                documentId = result.DocumentId,
                documentContent = result.DocumentContent,
                redirectUrl = Url.Action("Result", "Case",
                    new { id = result.CaseId, code = result.AnonymousTrackingCode })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private static string SerializeCited(IReadOnlyList<CitedSectionDto> sections)
    {
        var parts = sections.Select(s =>
            "{\"sectionId\":" + s.SectionId +
            ",\"actTitle\":\"" + s.ActTitle.Replace("\"", "\\\"") +
            "\",\"sectionNumber\":\"" + s.SectionNumber.Replace("\"", "\\\"") + "\"}");
        return "[" + string.Join(",", parts) + "]";
    }
}

// ---- request bodies ----
public class ChatNewRequest
{
    public string? FirstMessage { get; set; }
}

public class ChatAskRequest
{
    public int ChatSessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class ChatCommitRequest
{
    public int ChatSessionId { get; set; }
    public int CategoryId { get; set; }
    public byte DistrictId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? NotificationEmail { get; set; }
    public bool IsAnonymous { get; set; }
    public string DocumentType { get; set; } = "LabourComplaint";
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/MuktoAin.Web/Controllers/ChatController.cs
git commit -m "feat(web): ChatController — ask/new/messages/recent/quota/commit endpoints"
```

---
### Task A5: Chat-first Home page + chat.js

**Files:**
- Modify (full replace): `src/MuktoAin.Web/Views/Home/Index.cshtml`
- Create: `src/MuktoAin.Web/wwwroot/assets/js/chat.js`
- Modify: `src/MuktoAin.Web/wwwroot/assets/css/main.css` (append 5 rules)
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs` (add 1 action)

**Interfaces:**
- Consumes: ChatController endpoints (A4); existing CSS classes (Global Constraint 6); `main.js` behaviors: `showToast(msg)`, `data-open-modal="#id"` / `data-close-modal` (adds/removes `.open` on `.modal-backdrop`), `data-copy`, `data-counter`, `data-chip-group`, `data-prefill` (chips fill composer), language swap via `data-bn`/`data-en`.
- Produces: chat home at `/`; reads `?prefill=` deep-link param; `GET /Case/SubmitOptions` (district JSON for the draft modal).

- [ ] **Step 1: Replace Views/Home/Index.cshtml (full content)**

```html
@{
    ViewData["Title"] = "মুক্ত আইন — AI-সহায়ক আইনি তথ্য ও পরামর্শ";
    var prefill = Context.Request.Query["prefill"].ToString();
}

<main class="chat-shell" id="main" data-prefill="@prefill">
    <div class="chat-scroll">
        <div class="chat-inner">

            <!-- Recent in-progress chats (resumable) -->
            <div class="recent-chats" id="recent-chats" hidden>
                <span class="tiny muted" data-bn="সাম্প্রতিক আলোচনা" data-en="Recent chats">সাম্প্রতিক আলোচনা</span>
                <div class="chip-row" id="recent-chats-row"></div>
            </div>

            <!-- Welcome / empty state -->
            <div class="chat-welcome" id="chat-welcome">
                <h1 data-bn="আপনার সমস্যা বলুন — আইনটা আমরা খুঁজে দেব" data-en="Tell us your problem — we'll find the law">আপনার সমস্যা বলুন — আইনটা আমরা খুঁজে দেব</h1>
                <p data-bn="বাংলা, English বা Banglish-এ লিখুন। আমি প্রাসঙ্গিক আইন খুঁজে আপনার অধিকার সহজ ভাষায় ব্যাখ্যা করব, আর দরকার হলে আইনজীবী-যাচাইকৃত দলিল তৈরি করব।"
                   data-en="Write in Bangla, English, or Banglish. I'll find the relevant laws, explain your rights in plain language, and draft a lawyer-verified document when needed.">
                    বাংলা, English বা Banglish-এ লিখুন। আমি প্রাসঙ্গিক আইন খুঁজে আপনার অধিকার সহজ ভাষায় ব্যাখ্যা করব, আর দরকার হলে আইনজীবী-যাচাইকৃত দলিল তৈরি করব।
                </p>
            </div>
            <div class="chip-row" style="justify-content:center">
                <button class="chip" type="button" data-prefill="আমি একটি সাধারণ ডায়েরি (GD) করতে চাই।"><i data-lucide="file-text"></i> সাধারণ ডায়েরি (GD)</button>
                <button class="chip" type="button" data-prefill="আমি কোনো সরকারি অফিসের কাছে তথ্য চাই।"><i data-lucide="megaphone"></i> তথ্য অধিকার (RTI)</button>
                <button class="chip" type="button" data-prefill="আমার বেতন দিচ্ছে না।"><i data-lucide="factory"></i> শ্রম অভিযোগ</button>
                <button class="chip" type="button" data-prefill="কেনা পণ্যটি ত্রুটিপূর্ণ।"><i data-lucide="shopping-cart"></i> ভোক্তা অভিযোগ</button>
            </div>
            <p class="tiny" style="text-align:center">
                <span data-bn="উদাহরণ:" data-en="Examples:">উদাহরণ:</span>
                “গার্মেন্টসে ৩ মাস বেতন পাইনি” · “জমির দলিল নিয়ে বিরোধ”
            </p>

            <!-- Thread (messages injected by chat.js) -->
            <div id="chat-thread"></div>
        </div>
    </div>

    <!-- Composer -->
    <div class="composer-wrap">
        <div class="composer">
            <div class="composer-mode chip-row" id="composer-mode" data-chip-group="">
                <button class="chip active" type="button" data-mode="rights"><i data-lucide="message-circle"></i> <span data-bn="আইনি অধিকার জানুন" data-en="Know my rights">আইনি অধিকার জানুন</span></button>
                <button class="chip" type="button" data-mode="search"><i data-lucide="search"></i> <span data-bn="ধারা খুঁজুন" data-en="Find sections">ধারা খুঁজুন</span></button>
            </div>
            <div class="composer-row">
                <textarea id="chat-input" rows="1" maxlength="5000"
                          placeholder="আপনার সমস্যা লিখুন... (বাংলা / English / Banglish)"
                          aria-label="আপনার বার্তা লিখুন"></textarea>
                <button class="send-btn" id="chat-send" type="button" aria-label="পাঠান"><i data-lucide="arrow-up"></i></button>
            </div>
            <div class="quota-note tiny muted" id="quota-note"></div>
        </div>
    </div>

    <footer class="mini-footer">
        ⚠ <span data-bn="মুক্ত আইন সাধারণ আইনি তথ্য দেয়" data-en="MuktoAin provides general legal information">মুক্ত আইন সাধারণ আইনি তথ্য দেয়</span> ·
        <a asp-controller="Home" asp-action="About"><span data-bn="পরিচিতি ও ডেটাসেট কৃতজ্ঞতা" data-en="About & dataset attribution">পরিচিতি ও ডেটাসেট কৃতজ্ঞতা</span></a>
    </footer>
</main>

<!-- Citation modal (filled by chat.js) -->
<div class="modal-backdrop" id="citation-modal" role="dialog" aria-modal="true" aria-labelledby="cite-title">
    <div class="modal">
        <div class="modal-handle"></div>
        <div class="modal-head">
            <div>
                <span class="kicker" data-bn="আইনের ধারা" data-en="Statutory section">আইনের ধারা</span>
                <h3 id="cite-title">—</h3>
            </div>
            <button class="icon-btn" type="button" data-close-modal aria-label="বন্ধ করুন"><i data-lucide="x"></i></button>
        </div>
        <p class="serif" id="cite-text" style="line-height:2; color:var(--ink)"></p>
        <div class="row wrap" style="margin-top:16px">
            <button class="btn btn-outline btn-sm" type="button" data-close-modal data-bn="বন্ধ করুন" data-en="Close">বন্ধ করুন</button>
            <a class="btn btn-ghost btn-sm" asp-controller="Search" asp-action="Index" data-bn="আইনে আরও খুঁজুন →" data-en="Search more laws →">আইনে আরও খুঁজুন →</a>
        </div>
    </div>
</div>

<!-- Generate Draft confirm modal -->
<div class="modal-backdrop" id="draft-modal" role="dialog" aria-modal="true" aria-labelledby="draft-modal-title">
    <div class="modal">
        <div class="modal-handle"></div>
        <div class="modal-head">
            <div>
                <span class="kicker"><i data-lucide="file-check-2"></i> FR-5 · FR-19</span>
                <h3 id="draft-modal-title" data-bn="আপনার মামলার খসড়া প্রস্তুত করুন" data-en="Prepare your case draft">আপনার মামলার খসড়া প্রস্তুত করুন</h3>
            </div>
            <button class="icon-btn" type="button" data-close-modal aria-label="বন্ধ করুন"><i data-lucide="x"></i></button>
        </div>
        <div style="display:flex; flex-direction:column; gap:14px">
            <div>
                <label class="form-label" for="draft-doc-type" data-bn="দলিলের ধরন" data-en="Document type">দলিলের ধরন</label>
                <select id="draft-doc-type" class="input">
                    <option value="LabourComplaint" selected>শ্রম অভিযোগ পত্র / Labour complaint</option>
                    <option value="GeneralDiary">সাধারণ ডায়েরি (GD)</option>
                    <option value="RtiRequest">তথ্য অধিকার আবেদন (RTI)</option>
                    <option value="ConsumerComplaint">ভোক্তা অভিযোগ</option>
                </select>
            </div>
            <div>
                <label class="form-label" for="draft-category" data-bn="বিভাগ" data-en="Category">বিভাগ</label>
                <select id="draft-category" class="input">
                    <option value="1">শ্রম অধিকার ও অভিযোগ</option>
                    <option value="2">সাধারণ ডায়েরি (GD)</option>
                    <option value="3">তথ্য অধিকার</option>
                    <option value="4">ভোক্তা অধিকার</option>
                </select>
            </div>
            <div>
                <label class="form-label" for="draft-district" data-bn="জেলা" data-en="District">জেলা</label>
                <select id="draft-district" class="input"></select>
            </div>
            <div>
                <label class="form-label" for="draft-title-input" data-bn="মামলার শিরোনাম" data-en="Case title">মামলার শিরোনাম</label>
                <input id="draft-title-input" class="input" type="text" maxlength="250" placeholder="যেমন: ৩ মাসের বকেয়া বেতন" />
            </div>
            <div>
                <label class="form-label" for="draft-email" data-bn="ইমেইল (ঐচ্ছিক — শুধু আপডেট জানাতে)" data-en="Email (optional — notifications only)">ইমেইল (ঐচ্ছিক — শুধু আপডেট জানাতে)</label>
                <input id="draft-email" class="input" type="email" placeholder="you@example.com" />
                <small class="muted tiny" data-bn="কোনো অ্যাকাউন্ট তৈরি হবে না।" data-en="No account is created.">কোনো অ্যাকাউন্ট তৈরি হবে না।</small>
            </div>
            <label class="row" style="gap:10px; align-items:flex-start">
                <input type="checkbox" id="draft-anonymous" checked style="margin-top:4px" />
                <span class="tiny" data-bn="বেনামে জমা দিন — একটি ট্র্যাকিং কোড পাবেন যা দিয়ে মামলা দেখতে পারবেন" data-en="Submit anonymously — you'll get a tracking code to view the case">বেনামে জমা দিন — একটি ট্র্যাকিং কোড পাবেন যা দিয়ে মামলা দেখতে পারবেন</span>
            </label>
            <div class="alert alert-warn tiny">
                <i data-lucide="lock"></i>
                <span data-bn="ডাউনলোডের আগে একজন যাচাইকৃত আইনজীবীর অনুমোদন প্রয়োজন।" data-en="A verified lawyer's approval is required before download.">ডাউনলোডের আগে একজন যাচাইকৃত আইনজীবীর অনুমোদন প্রয়োজন।</span>
            </div>
            <button class="btn btn-gold btn-block" id="draft-submit" type="button">
                <i data-lucide="sparkles"></i> <span data-bn="নথি তৈরি করুন" data-en="Generate document">নথি তৈরি করুন</span>
            </button>
        </div>
    </div>
</div>

<script src="~/assets/js/chat.js" asp-append-version="true" defer></script>
```

**Note on `data-prefill`:** `main.js` already wires `[data-prefill]` chips to fill the composer textarea on OTHER pages via `document.querySelector(".composer textarea")`. On THIS page the composer lives in `.composer-row` — chat.js (below) implements its own prefill binding, so the main.js one simply won't match anything harmful. Keep the attribute name `data-prefill` anyway.

- [ ] **Step 2: Create wwwroot/assets/js/chat.js (full content)**

```javascript
/* MuktoAin chat home (FR-19/20) — vanilla JS, no frameworks.
   Talks to /Chat/* endpoints. Relies on main.js for: showToast, modal
   .open toggling (data-open-modal/data-close-modal), data-copy, icons. */
(function () {
    "use strict";

    var state = { chatSessionId: 0, asking: false };

    var thread, input, sendBtn, quotaNote, welcome;

    function el(id) { return document.getElementById(id); }
    function bn(n) { try { return Number(n).toLocaleString("bn-BD"); } catch (e) { return String(n); } }
    function scrollBottom() {
        var sc = document.querySelector(".chat-scroll");
        if (sc) sc.scrollTop = sc.scrollHeight;
    }
    function renderIcons() { if (window.lucide) window.lucide.createIcons(); }

    // ---------- rendering ----------

    function userBubble(text) {
        var d = document.createElement("div");
        d.className = "bubble user";
        d.textContent = text;
        thread.appendChild(d);
        scrollBottom();
    }

    function answerCard(data) {
        var wrap = document.createElement("div");
        wrap.className = "answer-card";

        var head = document.createElement("div");
        head.className = "answer-head";
        head.innerHTML = '<h3><i data-lucide="scale"></i> <span data-bn="আপনার অধিকার" data-en="Your rights">আপনার অধিকার</span></h3>';
        wrap.appendChild(head);

        var p = document.createElement("p");
        p.style.fontFamily = "var(--font-doc)";
        p.style.fontSize = "16px";
        p.style.whiteSpace = "pre-wrap";
        p.textContent = data.answer;
        wrap.appendChild(p);

        if (data.citedSections && data.citedSections.length) {
            var chips = document.createElement("div");
            chips.className = "chip-row";
            chips.style.marginTop = "12px";
            data.citedSections.forEach(function (s) {
                var b = document.createElement("button");
                b.className = "citation-chip";
                b.type = "button";
                b.textContent = (s.actTitle || "") +
                    (s.sectionNumber ? " · ধারা " + s.sectionNumber : "");
                b.addEventListener("click", function () { openCitation(s); });
                chips.appendChild(b);
            });
            wrap.appendChild(chips);
        }

        if (data.fromCache) {
            var c = document.createElement("small");
            c.className = "answer-cached tiny";
            c.style.display = "block";
            c.style.marginTop = "6px";
            c.setAttribute("data-bn", "এই প্রশ্নের উত্তর আগে দেওয়া হয়েছিল (ক্যাশ)।");
            c.setAttribute("data-en", "This question was answered before (cached).");
            c.textContent = "এই প্রশ্নের উত্তর আগে দেওয়া হয়েছিল (ক্যাশ)।";
            wrap.appendChild(c);
        }
        if (data.retrievalOnly) {
            var ro = document.createElement("small");
            ro.className = "muted tiny";
            ro.style.display = "block";
            ro.style.marginTop = "6px";
            ro.textContent = "⚙ AI ছাড়া কীওয়ার্ড-অনুসন্ধানের ফলাফল / retrieved without AI";
            wrap.appendChild(ro);
        }

        var disc = document.createElement("small");
        disc.className = "ai-disclaimer";
        disc.textContent = "⚠ " + (data.disclaimer || "সাধারণ আইনি তথ্য, আনুষ্ঠানিক আইনি পরামর্শ নয়।");
        wrap.appendChild(disc);

        thread.appendChild(wrap);
        quickReplies();
        draftSuggestion();
        renderIcons();
        scrollBottom();
    }

    function quickReplies() {
        var qr = document.createElement("div");
        qr.className = "quick-replies";
        [["নথি বানাতে চাই", "draft"], ["আরও প্রশ্ন আছে", "more"], ["না, ধন্যবাদ", "done"]].forEach(function (pair) {
            var b = document.createElement("button");
            b.className = "btn btn-outline btn-sm";
            b.type = "button";
            b.textContent = pair[0];
            b.addEventListener("click", function () {
                if (pair[1] === "draft") openDraftModal();
                else if (pair[1] === "more") input.focus();
                else showToast("ধন্যবাদ! যেকোনো সময় আবার আসুন।");
            });
            qr.appendChild(b);
        });
        thread.appendChild(qr);
    }

    function draftSuggestion() {
        var d = document.createElement("div");
        d.className = "draft-card";
        var head = document.createElement("div");
        head.className = "row";
        head.innerHTML =
            '<div class="item-ico"><i data-lucide="file-text"></i></div>' +
            '<div><b data-bn="এই সমস্যার জন্য দলিল তৈরি করতে পারি" data-en="I can draft a document for this problem">এই সমস্যার জন্য দলিল তৈরি করতে পারি</b><br>' +
            '<span class="muted tiny" data-bn="আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন" data-en="Lawyer-verified draft — you can edit it">আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন</span></div>';
        d.appendChild(head);
        var btn = document.createElement("button");
        btn.className = "btn btn-gold btn-block";
        btn.type = "button";
        btn.style.marginTop = "10px";
        btn.innerHTML = '<i data-lucide="sparkles"></i> <span data-bn="নথি তৈরি করুন" data-en="Generate document">নথি তৈরি করুন</span>';
        btn.addEventListener("click", openDraftModal);
        d.appendChild(btn);
        thread.appendChild(d);
    }

    function typing() {
        var t = document.createElement("div");
        t.className = "bubble ai typing";
        t.setAttribute("aria-hidden", "true");
        t.innerHTML = "<i></i><i></i><i></i>";
        thread.appendChild(t);
        scrollBottom();
        return t;
    }

    function quotaWallCard() {
        var d = document.createElement("div");
        d.className = "identity-bar";
        d.innerHTML =
            '<div class="item-ico" style="background:var(--primary-soft); color:var(--primary)"><i data-lucide="alarm-clock"></i></div>' +
            '<div style="flex:1"><b data-bn="আজকের AI সীমা শেষ" data-en="Daily AI limit reached">আজকের AI সীমা শেষ</b><br>' +
            '<small class="muted" data-bn="মাঝরাতে (প্রশান্ত মহাসাগরীয়) রিসেট হবে।" data-en="Resets at midnight Pacific.">মাঝরাতে (প্রশান্ত মহাসাগরীয়) রিসেট হবে।</small></div>';
        var actions = document.createElement("div");
        actions.className = "row wrap";
        [["/Account/Register", "user-plus", "নিবন্ধন করুন (৩× সীমা)"],
         ["/Search", "search", "আইন খুঁজুন (বিনামূল্যে)"],
         ["/Case/Submit", "edit-3", "ফর্মে জমা দিন"]].forEach(function (l) {
            var a = document.createElement("a");
            a.className = "btn btn-outline btn-sm";
            a.href = l[0];
            a.innerHTML = '<i data-lucide="' + l[1] + '"></i> ' + l[2];
            actions.appendChild(a);
        });
        d.appendChild(actions);
        thread.appendChild(d);
        renderIcons();
        scrollBottom();
    }

    function openCitation(s) {
        var title = el("cite-title");
        var text = el("cite-text");
        if (title) title.textContent = (s.actTitle || "") +
            (s.sectionNumber ? " — ধারা " + s.sectionNumber : "");
        if (text) text.textContent = s.sectionText || "";
        var modal = el("citation-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    // ---------- ask ----------

    function ask(question) {
        if (state.asking || !question || !question.trim()) return;
        state.asking = true;
        sendBtn.disabled = true;
        if (welcome) welcome.style.display = "none";
        userBubble(question);
        var dots = typing();

        fetch("/Chat/Ask", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                chatSessionId: state.chatSessionId,
                question: question,
                language: (localStorage.getItem("mkt-lang") || "bn") === "en" ? "en" : "bn"
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            dots.remove();
            if (data.tier === "wall") quotaWallCard();
            else answerCard(data);
            updateQuota(data.remainingToday, data.dailyLimit);
        })
        .catch(function () {
            dots.remove();
            userBubble("⚠ সংযোগ সমস্যা — আবার চেষ্টা করুন / Connection error");
        })
        .finally(function () {
            state.asking = false;
            sendBtn.disabled = false;
        });
    }

    function updateQuota(remaining, limit) {
        if (!quotaNote || typeof remaining !== "number") return;
        quotaNote.textContent = "আজ বাকি: " + bn(remaining) + " / " + bn(limit);
        quotaNote.setAttribute("data-bn", "আজ বাকি: " + bn(remaining) + " / " + bn(limit));
        quotaNote.setAttribute("data-en", "Remaining today: " + remaining + " / " + limit);
    }

    // ---------- draft modal ----------

    function openDraftModal() {
        var ti = el("draft-title-input");
        if (ti && !ti.value) {
            var users = thread.querySelectorAll(".bubble.user");
            if (users.length) ti.value = users[users.length - 1].textContent.slice(0, 250);
        }
        var modal = el("draft-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    function submitDraft() {
        var btn = el("draft-submit");
        btn.disabled = true;
        btn.textContent = "…";
        fetch("/Chat/Commit", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                chatSessionId: state.chatSessionId,
                categoryId: parseInt(el("draft-category").value, 10),
                districtId: parseInt(el("draft-district").value, 10),
                title: el("draft-title-input").value,
                notificationEmail: el("draft-email").value || null,
                isAnonymous: el("draft-anonymous").checked,
                documentType: el("draft-doc-type").value
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (data.error) {
                showToast("সমস্যা: " + data.error);
                btn.disabled = false;
                btn.textContent = "নথি তৈরি করুন";
                return;
            }
            window.location.href = data.redirectUrl;
        })
        .catch(function () {
            showToast("সংযোগ সমস্যা — আবার চেষ্টা করুন");
            btn.disabled = false;
            btn.textContent = "নথি তৈরি করুন";
        });
    }

    // ---------- recent chats / resume ----------

    function loadRecent() {
        fetch("/Chat/Recent")
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (!data.chats || !data.chats.length) return;
            var box = el("recent-chats"), row = el("recent-chats-row");
            if (!box || !row) return;
            data.chats.forEach(function (c) {
                var b = document.createElement("button");
                b.className = "chip chip-sm";
                b.type = "button";
                b.textContent = (c.title || "আলোচনা").slice(0, 32) + " (" + bn(c.messageCount) + ")";
                b.addEventListener("click", function () { resume(c.chatSessionId); });
                row.appendChild(b);
            });
            box.hidden = false;
        })
        .catch(function () {});
    }

    function resume(chatSessionId) {
        fetch("/Chat/Messages?id=" + chatSessionId)
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (!data.messages) return;
            thread.innerHTML = "";
            if (welcome) welcome.style.display = "none";
            state.chatSessionId = data.chatSessionId;
            data.messages.forEach(function (m) {
                if (m.role === "user") {
                    userBubble(m.content);
                } else {
                    var cited = [];
                    if (m.citedJson) {
                        try {
                            cited = JSON.parse(m.citedJson).map(function (c) {
                                return { actTitle: c.actTitle, sectionNumber: c.sectionNumber,
                                         sectionText: "", sectionId: c.sectionId };
                            });
                        } catch (e) { cited = []; }
                    }
                    answerCard({ answer: m.content, citedSections: cited, disclaimer: "",
                                 fromCache: false, retrievalOnly: false });
                }
            });
        })
        .catch(function () { showToast("আলোচনা খোলা যায়নি"); });
    }

    // ---------- init ----------

    function ensureSession() {
        fetch("/Chat/New", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: "{}"
        })
        .then(function (r) { return r.json(); })
        .then(function (d) { state.chatSessionId = d.chatSessionId; })
        .catch(function () {});
    }

    function loadDistricts() {
        fetch("/Case/SubmitOptions")
        .then(function (r) { return r.json(); })
        .then(function (data) {
            var sel = el("draft-district");
            if (!sel || !data) return;
            sel.innerHTML = "";
            (data.districts || []).forEach(function (d) {
                var o = document.createElement("option");
                o.value = d.id;
                o.textContent = d.name;
                sel.appendChild(o);
            });
        })
        .catch(function () {});
    }

    document.addEventListener("DOMContentLoaded", function () {
        thread = el("chat-thread");
        input = el("chat-input");
        sendBtn = el("chat-send");
        quotaNote = el("quota-note");
        welcome = el("chat-welcome");
        if (!thread || !input || !sendBtn) return;

        // category chips prefill the composer
        document.querySelectorAll("[data-prefill]").forEach(function (chip) {
            if (chip.tagName === "BUTTON") {
                chip.addEventListener("click", function () {
                    input.value = chip.getAttribute("data-prefill");
                    input.focus();
                });
            }
        });

        // mode chips (search mode routes the question through the same Ask
        // endpoint; the marker-free prompt already favors section retrieval)
        document.querySelectorAll("#composer-mode .chip").forEach(function (chip) {
            chip.addEventListener("click", function () {
                if ((chip.dataset.mode || "") === "search") {
                    showToast("ধারা খুঁজুন মোড: প্রশ্ন লিখুন — সরাসরি ধারা দেখানো হবে");
                }
            });
        });

        sendBtn.addEventListener("click", function () {
            ask(input.value);
            input.value = "";
        });
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                ask(input.value);
                input.value = "";
            }
        });

        var ds = el("draft-submit");
        if (ds) ds.addEventListener("click", submitDraft);

        // deep-link prefill (?prefill= from categories/search)
        var shell = document.querySelector(".chat-shell");
        var pf = shell ? (shell.dataset.prefill || "") : "";
        if (pf) { input.value = decodeURIComponent(pf); input.focus(); }

        ensureSession();
        loadRecent();
        loadDistricts();

        fetch("/Chat/Quota")
        .then(function (r) { return r.json(); })
        .then(function (d) { updateQuota(d.remainingToday, d.dailyLimit); })
        .catch(function () {});
    });
})();
```

- [ ] **Step 3: Add SubmitOptions action to CaseController**

In `src/MuktoAin.Web/Controllers/CaseController.cs`, add immediately after the existing `Submit()` GET action:
```csharp
    // District list as JSON for the chat draft modal (A5).
    [HttpGet]
    public async Task<IActionResult> SubmitOptions()
    {
        var districts = await _districtRepo.GetAllAsync();
        return Json(new
        {
            districts = districts.OrderBy(d => d.Name)
                .Select(d => new { id = d.DistrictId, name = d.Name })
        });
    }
```

- [ ] **Step 4: Append CSS**

Append to the END of `src/MuktoAin.Web/wwwroot/assets/css/main.css`:
```css
/* ---------- Frontend redesign additions (2026-09-05) ---------- */
.recent-chats { margin-bottom: 12px; }
.recent-chats .tiny { display: block; margin-bottom: 6px; }
.quota-note { text-align: right; padding: 4px 2px 0; }
.answer-cached { color: var(--ink-3); }
.unread-dot {
  width: 10px; height: 10px; border-radius: 50%;
  background: var(--gold); display: inline-block; flex: none;
  box-shadow: 0 0 0 3px var(--gold-soft);
}
/* chat home replaces the landing page — hide the site footer there */
body:has(.chat-shell) .site-footer { display: none; }
```
(`:has` is supported by all evergreen browsers; on ancient browsers the footer simply shows — acceptable graceful degradation. `.unread-dot` is placed here so A7 can use it without a second CSS task.)

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Web/Views/Home/Index.cshtml src/MuktoAin.Web/wwwroot/assets/js/chat.js src/MuktoAin.Web/wwwroot/assets/css/main.css src/MuktoAin.Web/Controllers/CaseController.cs
git commit -m "feat(web): chat-first home — thread, quota counter, ladder UI, draft modal, recent chats"
```

---
### Task A6: Case page evolution — real timeline, embedded editable paper, send/withdraw

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs` (ctor + Result rewrite + 3 new POST actions; delete `MapToResultViewModel`)
- Modify (full replace): `src/MuktoAin.Web/Views/Case/Result.cshtml`
- Modify: `src/MuktoAin.Web/ViewModels/CaseViewModels.cs` (replace `CaseResultViewModel`; add `HasUnread` to `CaseListItemViewModel`)

**Interfaces:**
- Consumes: `CaseService.GetCaseDetailAsync/GetUserCasesAsync/TransitionStatusAsync`; `ICaseRepository.GetWithDocumentsAsync`; `DocumentService.GenerateDocumentAsync`; `IRepository<GeneratedDocument/LawyerReview/LawyerProfile/AiLog/CaseActReference>`; `UserManager<User>` (resolve via `HttpContext.RequestServices`); `DocumentStatus/CaseStatus` enums.
- Produces: POST `/Case/SaveDraft` (params `id, editedContent, code?`), POST `/Case/SendToLawyer` (`id, code?`), POST `/Case/Withdraw` (`id, code?`); evolved `CaseResultViewModel` (fields below) consumed by the new view.

- [ ] **Step 1: Extend the ViewModels**

In `src/MuktoAin.Web/ViewModels/CaseViewModels.cs`:

**1a.** REPLACE the whole `CaseResultViewModel` class with:
```csharp
public class CaseResultViewModel
{
    public int CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Submitted";
    public string CategoryName { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string TrackingCode { get; set; } = string.Empty;

    // Rights explanation
    public string RightsExplanation { get; set; } = string.Empty;
    public List<CitedSectionViewModel> CitedSections { get; set; } = new();

    // Document draft (embedded paper, version chain)
    public int? DocumentId { get; set; }
    public string? DocumentContent { get; set; }
    public string? ContentFinal { get; set; }
    public string? DocumentStatus { get; set; }
    public bool CanDownloadPdf { get; set; }
    public int VersionNo { get; set; } = 1;
    public bool CitizenEdited { get; set; }
    public bool CanEdit { get; set; }

    // Timeline (real, status-driven)
    public string TimelineCurrent { get; set; } = "DraftReady";

    // Lawyer block
    public string? LawyerName { get; set; }
    public string? LawyerBarNumber { get; set; }
    public string? LawyerDecision { get; set; }
    public string? LawyerComments { get; set; }
    public string? RejectionReason { get; set; }
}
```

**1b.** In `CaseListItemViewModel`, add:
```csharp
    public bool HasUnread { get; set; }
```

- [ ] **Step 2: Rework CaseController**

**2a.** Replace the private-fields + constructor block (everything from `private readonly CaseService _caseService;` through the constructor's closing brace) with:
```csharp
    private readonly CaseService _caseService;
    private readonly IRightsExplanationService _rightsExplanationService;
    private readonly DocumentService _documentService;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly IRepository<LawyerReview> _reviewRepo;
    private readonly IRepository<LawyerProfile> _lawyerProfileRepo;

    public CaseController(
        CaseService caseService,
        IRightsExplanationService rightsExplanationService,
        DocumentService documentService,
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo,
        IRepository<GeneratedDocument> docRepo,
        IRepository<LawyerReview> reviewRepo,
        IRepository<LawyerProfile> lawyerProfileRepo)
    {
        _caseService = caseService;
        _rightsExplanationService = rightsExplanationService;
        _documentService = documentService;
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
        _docRepo = docRepo;
        _reviewRepo = reviewRepo;
        _lawyerProfileRepo = lawyerProfileRepo;
    }
```
Add `using Microsoft.AspNetCore.Identity;` at the top of the file if not present.

**2b.** REPLACE the whole existing `Result` action with:
```csharp
    [HttpGet]
    public async Task<IActionResult> Result(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var currentUserId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        var detail = await _caseService.GetCaseDetailAsync(id, currentUserId, role, trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        if (caseEntity == null) return NotFound();

        var doc = caseEntity.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null)
        {
            // Legacy form-path case with no draft yet — generate once
            // (then served from AI_LOG/GENERATED_DOCUMENT on future views).
            caseEntity.Title = detail.Title;
            caseEntity.Description = detail.Description;
            var explanation = await _rightsExplanationService.ExplainRightsAsync(caseEntity);
            var docDto = await _documentService.GenerateDocumentAsync(id, explanation);
            doc = await _docRepo.GetByIdAsync(docDto.DocumentId);
            if (doc == null) return NotFound();
        }

        // Unread dot clear-on-view
        if (caseEntity.HasUnreadActivity)
        {
            caseEntity.HasUnreadActivity = false;
            await _caseRepo.SaveChangesAsync();
        }

        var review = doc.Reviews.OrderBy(r => r.ReviewId).LastOrDefault();
        LawyerProfile? lawyer = null;
        if (doc.AssignedLawyerProfileId.HasValue)
            lawyer = await _lawyerProfileRepo.GetByIdAsync(doc.AssignedLawyerProfileId.Value);
        if (lawyer == null && review != null)
            lawyer = await _lawyerProfileRepo.GetByIdAsync(review.LawyerProfileId);

        string? lawyerName = null;
        if (lawyer != null)
        {
            var userManager = HttpContext.RequestServices
                .GetRequiredService<UserManager<MuktoAin.Domain.Entities.User>>();
            var lawyerUser = await userManager.FindByIdAsync(lawyer.UserId.ToString());
            lawyerName = lawyerUser?.FullName;
        }

        var vm = new CaseResultViewModel
        {
            CaseId = id,
            Title = detail.Title,
            Status = detail.Status,
            CategoryName = detail.CategoryName,
            DistrictName = detail.DistrictName,
            CreatedAt = detail.CreatedAt,
            TrackingCode = caseEntity.AnonymousTrackingCode ?? string.Empty,
            RightsExplanation = string.Empty,
            DocumentId = doc.DocumentId,
            DocumentContent = doc.ContentDraft,
            ContentFinal = doc.ContentFinal,
            DocumentStatus = doc.Status.ToString(),
            CanDownloadPdf = doc.Status == DocumentStatus.Approved
                          || doc.Status == DocumentStatus.EditedApproved,
            VersionNo = doc.VersionNo,
            CitizenEdited = doc.CitizenEdited,
            CanEdit = doc.Status == DocumentStatus.Draft || doc.Status == DocumentStatus.Rejected,
            TimelineCurrent = MapTimelineState(caseEntity.Status, doc.Status),
            LawyerName = lawyerName,
            LawyerBarNumber = lawyer?.BarRegistrationNumber,
            LawyerDecision = review?.Decision.ToString(),
            LawyerComments = review?.Comments,
            RejectionReason = doc.Status == DocumentStatus.Rejected ? review?.Comments : null
        };

        // Rights explanation from the cached AI_LOG entry — no regeneration
        // (fixes the generate-on-every-GET churn by design).
        var logRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.AiLog>>();
        var logs = await logRepo.GetAllAsync();
        var rightsLog = logs
            .Where(l => l.CaseId == id && l.RequestType == AiRequestType.RightsExplanation)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefault();
        vm.RightsExplanation = rightsLog?.ResponseText
            ?? "আইনি অধিকার বিশ্লেষণ প্রস্তুত হচ্ছে...";

        // Cited sections from CASE_ACT_REFERENCE (persisted at generation time)
        var refRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.CaseActReference>>();
        var actRefs = await refRepo.GetAllAsync();
        vm.CitedSections = actRefs
            .Where(r => r.CaseId == id)
            .Select(r => new CitedSectionViewModel
            {
                ActTitle = r.Section?.Act?.Title ?? string.Empty,
                SectionNumber = string.IsNullOrWhiteSpace(r.Section?.SectionNumber)
                    ? string.Empty
                    : $"ধারা {r.Section.SectionNumber}",
                SectionText = r.Section?.SectionText ?? string.Empty,
                RelevanceScore = $"{Math.Round(r.RelevanceScore * 100)}%"
            })
            .ToList();

        return View(vm);
    }

    private static string MapTimelineState(CaseStatus caseStatus, DocumentStatus docStatus) =>
        (caseStatus, docStatus) switch
        {
            (_, DocumentStatus.Rejected) => "Rejected",
            (CaseStatus.Finalized, _) => "Approved",
            (CaseStatus.UnderReview, _) => "UnderReview",
            _ => "DraftReady"
        };
```

**2c.** Add these three POST actions inside the class:
```csharp
    // FR-21: citizen edits the draft — saves ContentFinal, bumps version.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(int id, string editedContent, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null) return NotFound();
        if (doc.Status != DocumentStatus.Draft && doc.Status != DocumentStatus.Rejected)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(editedContent) && editedContent != doc.ContentFinal)
        {
            doc.ContentFinal = editedContent;
            doc.VersionNo++;
            doc.CitizenEdited = editedContent.Trim() != doc.ContentDraft.Trim();
            await _docRepo.SaveChangesAsync();
        }

        TempData["Success"] = $"খসড়া সংরক্ষিত হয়েছে (সংস্করণ {doc.VersionNo})।";
        TempData["SuccessEn"] = $"Draft saved (version {doc.VersionNo}).";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }

    // FR-13: citizen sends the draft to the lawyer pool (oldest-first, claim-based).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToLawyer(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null) return NotFound();
        if (doc.Status != DocumentStatus.Draft && doc.Status != DocumentStatus.Rejected)
            return Forbid();

        doc.Status = DocumentStatus.UnderReview;
        await _docRepo.SaveChangesAsync();
        await _caseService.TransitionStatusAsync(id, CaseStatus.UnderReview);

        TempData["Success"] = "আপনার খসড়া আইনজীবী পুলে পাঠানো হয়েছে।";
        TempData["SuccessEn"] = "Your draft was sent to the lawyer pool.";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }

    // FR-21: citizen withdraws a case (AI advised unsalvageable, or citizen
    // chooses to stop). Case stays viewable forever.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc != null && doc.Status == DocumentStatus.UnderReview)
            return Forbid(); // cannot withdraw while a lawyer holds it

        var ok = await _caseService.TransitionStatusAsync(id, CaseStatus.Finalized);
        if (!ok) return Forbid();

        TempData["Success"] = "মামলাটি প্রত্যাহৃত হয়েছে — যেকোনো সময় দেখতে পারবেন।";
        TempData["SuccessEn"] = "Case withdrawn — you can still view it anytime.";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }
```

**2d.** DELETE the now-unused private method `MapToResultViewModel` (its logic was inlined above).

- [ ] **Step 3: Full-replace Views/Case/Result.cshtml**

```html
@model CaseResultViewModel
@using MuktoAin.Web.Controllers
@{
    ViewData["Title"] = "মামলা #" + Model.CaseId + " — মুক্ত আইন";
    var displayCode = string.IsNullOrEmpty(Model.TrackingCode) ? "" : "MKT-" + Model.TrackingCode.Substring(0, Math.Min(6, Model.TrackingCode.Length)).ToUpper();
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <a asp-controller="Case" asp-action="Track" data-bn="আমার মামলা" data-en="My Cases">আমার মামলা</a>
        <span class="sep">/</span>
        <span>#@Model.CaseId</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="folder-open"></i> CASE-@Model.CaseId · @Model.CategoryName</span>
        <h1 class="page-title">@Model.Title</h1>
        <p class="page-sub">
            @Model.DistrictName · @Model.CreatedAt.ToString("d MMM yyyy")
            <span class="badge badge-@(Model.TimelineCurrent.ToLower())">@StatusText.Bn(Model.TimelineCurrent) / @StatusText.En(Model.TimelineCurrent)</span>
            @if (!string.IsNullOrEmpty(displayCode))
            {
                <span class="chip chip-sm mono">@displayCode
                    <button type="button" class="icon-btn" data-copy="@Model.TrackingCode" aria-label="কোড কপি করুন"><i data-lucide="copy"></i></button>
                </span>
            }
        </p>
    </div>

    <!-- Real status timeline (driven by actual status) -->
    <div class="timeline" aria-label="Case timeline">
        @{
            var steps = new[] {
                ("DraftReady", "খসড়া প্রস্তুত", "Draft ready"),
                ("UnderReview", "আইনজীবী পর্যালোচনায়", "Under review"),
                ("Approved", "অনুমোদিত", "Approved"),
                ("Withdrawn", "প্রত্যাহৃত", "Withdrawn")
            };
            if (Model.TimelineCurrent == "Rejected")
            {
                steps = new[] {
                    ("DraftReady", "খসড়া প্রস্তুত", "Draft ready"),
                    ("UnderReview", "আইনজীবী পর্যালোচনায়", "Under review"),
                    ("Rejected", "প্রত্যাখ্যাত", "Rejected"),
                    ("Withdrawn", "প্রত্যাহৃত", "Withdrawn")
                };
            }
            var curIndex = Array.FindIndex(steps, s => s.Item1 == Model.TimelineCurrent);
            if (Model.TimelineCurrent == "Withdrawn") { curIndex = 3; }
        }
        @for (var i = 0; i < steps.Length; i++)
        {
            var isRejectedStep = Model.TimelineCurrent == "Rejected" && steps[i].Item1 == "Rejected";
            var cls = isRejectedStep ? "current rejected"
                : i < curIndex ? "done"
                : i == curIndex ? "current" : "";
            <div class="t-step @cls">
                <span class="t-dot"></span>
                <span>@steps[i].Item2 <small class="muted">/ @steps[i].Item3</small></span>
            </div>
        }
    </div>

    @if (Model.TimelineCurrent == "Rejected" && Model.RejectionReason != null)
    {
        <div class="alert alert-danger" style="margin: 16px 0">
            <i data-lucide="x-circle"></i>
            <div>
                <b data-bn="আইনজীবীর মতামত:" data-en="Lawyer's reason:">আইনজীবীর মতামত:</b> @Model.RejectionReason
            </div>
        </div>
        <div class="row wrap" style="gap: 10px; margin-bottom: 16px">
            <a class="btn btn-primary btn-sm" asp-controller="Home" asp-action="Index"
               asp-route-prefill="আমার মামলাটি প্রত্যাখ্যাত হয়েছিল। কারণ: @Model.RejectionReason। এটি কি ঠিক করা সম্ভব?">
                <i data-lucide="message-circle"></i> <span data-bn="চ্যাটে ফিরে যান (কারণসহ)" data-en="Return to chat (with reason)">চ্যাটে ফিরে যান (কারণসহ)</span>
            </a>
        </div>
    }

    <!-- Lawyer block (visible after claim/decision) -->
    @if (Model.LawyerName != null)
    {
        <div class="card" style="margin: 16px 0">
            <div class="row" style="gap: 12px; align-items: center">
                <div class="item-ico"><i data-lucide="user-check"></i></div>
                <div style="flex: 1">
                    <b>অ্যাডভোকেট @Model.LawyerName</b>
                    @if (Model.LawyerBarNumber != null)
                    {
                        <small class="muted"> · Bar #@Model.LawyerBarNumber</small>
                    }
                    @if (Model.LawyerDecision != null)
                    {
                        <br />
                        <span class="badge badge-@(Model.LawyerDecision == "Rejected" ? "rejected" : "final")">@Model.LawyerDecision</span>
                    }
                    @if (!string.IsNullOrEmpty(Model.LawyerComments) && Model.DocumentStatus != "Rejected")
                    {
                        <p class="muted tiny" style="margin: 6px 0 0">@Model.LawyerComments</p>
                    }
                </div>
            </div>
        </div>
    }

    <!-- Rights explanation -->
    <div class="card" style="margin: 16px 0">
        <h2 class="section-h"><i data-lucide="scale"></i> <span data-bn="আপনার অধিকার" data-en="Your rights">আপনার অধিকার</span></h2>
        <p class="serif" style="white-space: pre-wrap; line-height: 2">@Model.RightsExplanation</p>
    </div>

    <!-- Cited sections -->
    @if (Model.CitedSections.Any())
    {
        <div class="card" style="margin: 16px 0">
            <h2 class="section-h"><i data-lucide="book-open"></i> <span data-bn="উদ্ধৃত ধারাসমূহ" data-en="Cited sections">উদ্ধৃত ধারাসমূহ</span></h2>
            @foreach (var s in Model.CitedSections)
            {
                <details class="acc">
                    <summary>
                        <span class="badge badge-neutral">@s.ActTitle @s.SectionNumber</span>
                        <small class="muted">@s.RelevanceScore</small>
                    </summary>
                    <p class="serif" style="line-height: 2; white-space: pre-wrap">@s.SectionText</p>
                </details>
            }
        </div>
    }

    <!-- Embedded paper draft -->
    @if (Model.DocumentId.HasValue)
    {
        <div class="card" style="margin: 16px 0">
            <div class="row spread wrap" style="margin-bottom: 12px; align-items: center; gap: 10px">
                <h2 class="section-h" style="margin: 0">
                    <i data-lucide="file-text"></i>
                    <span data-bn="তৈরি হওয়া দলিল" data-en="Generated document">তৈরি হওয়া দলিল</span>
                    <span class="badge badge-neutral">v@(Model.VersionNo)</span>
                    @if (Model.CitizenEdited)
                    {
                        <span class="badge badge-review" data-bn="আপনি সম্পাদনা করেছেন" data-en="Citizen edited">আপনি সম্পাদনা করেছেন</span>
                    }
                </h2>
                <div class="row" style="gap: 8px">
                    @if (Model.CanEdit)
                    {
                        <button class="btn btn-outline btn-sm" type="button" data-open-modal="#edit-modal">
                            <i data-lucide="edit-3"></i> <span data-bn="সম্পাদনা" data-en="Edit">সম্পাদনা</span>
                        </button>
                    }
                    @if (Model.TimelineCurrent is "DraftReady" or "Rejected")
                    {
                        <form asp-action="SendToLawyer" asp-route-id="@Model.CaseId" method="post"
                              asp-route-code="@Model.TrackingCode" style="display:inline">
                            @Html.AntiForgeryToken()
                            <button class="btn btn-primary btn-sm" type="submit"
                                    data-confirm="আইনজীবীর কাছে পাঠাবেন? / Send to the lawyer pool?">
                                <i data-lucide="send"></i> <span data-bn="আইনজীবীর কাছে পাঠান" data-en="Send to lawyer">আইনজীবীর কাছে পাঠান</span>
                            </button>
                        </form>
                    }
                </div>
            </div>

            <div class="paper-sheet @(Model.CanDownloadPdf ? "" : "paper-watermark")">
                <div class="doc-meta">গণপ্রজাতন্ত্রী বাংলাদেশ · @StatusText.Bn(Model.DocumentStatus) · DOC-@Model.DocumentId</div>
                <pre style="white-space: pre-wrap; font-family: var(--font-doc); font-size: 15px; line-height: 2; margin: 0">@(Model.ContentFinal ?? Model.DocumentContent)</pre>
                <div class="doc-stamp">
                    ⚠ এই দলিলটি AI সহায়তায় প্রস্তুত — যাচাইকৃত আইনজীবীর অনুমোদন ছাড়া চূড়ান্ত নয়।
                    MuktoAin provides general legal information, not formal legal advice.
                </div>
            </div>

            @if (Model.CanDownloadPdf)
            {
                <a class="btn btn-gold btn-block" style="margin-top: 12px"
                   asp-controller="Document" asp-action="Download" asp-route-id="@Model.DocumentId">
                    <i data-lucide="download"></i> <span data-bn="চূড়ান্ত PDF ডাউনলোড করুন" data-en="Download final PDF">চূড়ান্ত PDF ডাউনলোড করুন</span>
                </a>
            }
            else
            {
                <div class="alert alert-warn tiny" style="margin-top: 12px">
                    <i data-lucide="lock"></i>
                    <span data-bn="PDF ডাউনলোড আইনজীবী অনুমোদনের পরে আনলক হবে।" data-en="PDF download unlocks after lawyer approval.">PDF ডাউনলোড আইনজীবী অনুমোদনের পরে আনলক হবে।</span>
                </div>
            }
        </div>
    }

    <!-- Withdraw (only when not in lawyer's hands and not already final) -->
    @if (Model.TimelineCurrent is "DraftReady" or "Rejected")
    {
        <form asp-action="Withdraw" asp-route-id="@Model.CaseId" method="post" asp-route-code="@Model.TrackingCode"
              data-confirm="মামলাটি প্রত্যাহার করবেন? আর পাঠানো যাবে না / Withdraw this case? This cannot be undone.">
            @Html.AntiForgeryToken()
            <button class="btn btn-quiet btn-sm" type="submit">
                <i data-lucide="archive"></i> <span data-bn="মামলা প্রত্যাহার" data-en="Withdraw case">মামলা প্রত্যাহার</span>
            </button>
        </form>
    }
</main>

<!-- Edit modal (citizen draft editing — FR-21) -->
<div class="modal-backdrop" id="edit-modal" role="dialog" aria-modal="true" aria-labelledby="edit-modal-title">
    <div class="modal">
        <div class="modal-handle"></div>
        <div class="modal-head">
            <div>
                <span class="kicker"><i data-lucide="edit-3"></i> FR-21</span>
                <h3 id="edit-modal-title" data-bn="খসড়া সম্পাদনা করুন" data-en="Edit draft">খসড়া সম্পাদনা করুন</h3>
            </div>
            <button class="icon-btn" type="button" data-close-modal aria-label="বন্ধ করুন"><i data-lucide="x"></i></button>
        </div>
        <form asp-action="SaveDraft" asp-route-id="@Model.CaseId" method="post" asp-route-code="@Model.TrackingCode">
            @Html.AntiForgeryToken()
            <textarea name="editedContent" class="input" rows="14"
                      style="width:100%; font-family: var(--font-doc)"
                      data-counter="#edit-counter">@(Model.ContentFinal ?? Model.DocumentContent)</textarea>
            <small class="muted tiny" id="edit-counter"></small>
            <button class="btn btn-primary btn-block" style="margin-top: 12px" type="submit">
                <i data-lucide="save"></i> <span data-bn="সংরক্ষণ করুন" data-en="Save">সংরক্ষণ করুন</span>
            </button>
        </form>
    </div>
</div>
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Web/Controllers/CaseController.cs src/MuktoAin.Web/Views/Case/Result.cshtml src/MuktoAin.Web/ViewModels/CaseViewModels.cs
git commit -m "feat(web): case page — real timeline, embedded editable paper, send-to-lawyer, withdraw"
```

---
### Task A7: My Cases (Track) upgrade + Submit prefill fix

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs` (Track rewrite, Submit GET params)
- Modify (full replace): `src/MuktoAin.Web/Views/Case/Track.cshtml`
- Modify: `src/MuktoAin.Web/ViewModels/CaseViewModels.cs` (extend `CaseTrackViewModel`)

**Interfaces:**
- Consumes: A6 VM changes (`CaseListItemViewModel.HasUnread`); `CaseService.GetCaseDetailAsync(caseId, userId, role, trackingCode)`; existing session helpers `GetTrackedCases()` / `RememberTrackedCase()`; `ICaseRepository.GetByIdAsync`.
- Produces: `GET /Case/Track?status=&code=` (status filter + guest code lookup redirect); `GET /Case/Submit?cat=&q=` pre-fill.

- [ ] **Step 1: Extend CaseTrackViewModel**

In `src/MuktoAin.Web/ViewModels/CaseViewModels.cs`, REPLACE the whole `CaseTrackViewModel` class with:
```csharp
public class CaseTrackViewModel
{
    public List<CaseListItemViewModel> Cases { get; set; } = new();
    public string ActiveStatusFilter { get; set; } = "All";
    public string LookupCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Rewrite the Track action + Submit GET**

**2a.** In `CaseController.cs`, REPLACE the whole existing `Track` action with:
```csharp
    [HttpGet]
    public async Task<IActionResult> Track(string? status, string? code)
    {
        var vm = new CaseTrackViewModel();

        // Guest tracking-code lookup (FR-8): valid code redirects straight to the case
        if (!string.IsNullOrWhiteSpace(code))
        {
            var all = await _caseRepo.GetAllAsync();
            var match = all.FirstOrDefault(c =>
                c.AnonymousTrackingCode == code.Trim());
            if (match != null)
            {
                return RedirectToAction(nameof(Result), new { id = match.CaseId, code = code.Trim() });
            }
            TempData["Error"] = "কোডটি মেলেনি — আবার চেষ্টা করুন।";
            TempData["ErrorEn"] = "Code did not match — try again.";
        }

        var currentUserId = GetCurrentUserId();

        if (currentUserId.HasValue)
        {
            var userCases = await _caseService.GetUserCasesAsync(currentUserId.Value);
            foreach (var detail in userCases)
            {
                vm.Cases.Add(await ToListItemAsync(detail, string.Empty));
            }
        }

        foreach (var (caseId, sessionCode) in GetTrackedCases())
        {
            if (vm.Cases.Any(c => c.CaseId == caseId)) continue;
            var detail = await _caseService.GetCaseDetailAsync(
                caseId, userId: null, UserRole.Citizen, sessionCode);
            if (detail == null) continue;
            vm.Cases.Add(await ToListItemAsync(detail, sessionCode));
        }

        // Server-side status filter (real param — fixes decorative chips)
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            vm.Cases = vm.Cases.Where(c => MatchesFilter(c.Status, status)).ToList();
        }

        vm.ActiveStatusFilter = status ?? "All";
        vm.LookupCode = code ?? string.Empty;
        return View(vm);
    }

    private static bool MatchesFilter(string caseStatus, string filter) =>
        filter switch
        {
            "Approved" => caseStatus == nameof(CaseStatus.Finalized),
            _ => caseStatus == filter // UnderReview / Submitted map directly
        };

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

**2b.** REPLACE the existing `Submit()` GET action with:
```csharp
    [HttpGet]
    public async Task<IActionResult> Submit(int? cat, string? q)
    {
        var vm = await BuildSubmitViewModelAsync();
        if (cat.HasValue) vm.CategoryId = cat.Value;
        if (!string.IsNullOrWhiteSpace(q)) vm.Description = q;
        return View(vm);
    }
```

- [ ] **Step 3: Full-replace Views/Case/Track.cshtml**

```html
@model CaseTrackViewModel
@using MuktoAin.Web.Controllers
@{
    ViewData["Title"] = "আমার মামলা — মুক্ত আইন";
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <span data-bn="আমার মামলা" data-en="My Cases">আমার মামলা</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="folder-open"></i> FR-8</span>
        <h1 class="page-title" data-bn="আমার মামলাসমূহ" data-en="My Cases">আমার মামলাসমূহ</h1>
        <p class="page-sub" data-bn="আপনার জমা দেওয়া মামলাগুলোর সর্বশেষ অবস্থা।" data-en="Track the status of your submitted cases.">আপনার জমা দেওয়া মামলাগুলোর সর্বশেষ অবস্থা।</p>
    </div>

    <!-- Guest tracking-code lookup -->
    <div class="card" style="margin-bottom: 16px">
        <form asp-action="Track" method="get" class="row" style="gap: 10px; align-items: center">
            <div class="item-ico"><i data-lucide="key-round"></i></div>
            <input class="input mono" style="flex: 1; min-width: 180px" type="text" name="code" value="@Model.LookupCode"
                   placeholder="MKT-XXXXXXXX (ট্র্যাকিং কোড)" aria-label="ট্র্যাকিং কোড" />
            <button class="btn btn-primary" type="submit">
                <i data-lucide="search"></i> <span data-bn="ট্র্যাক করুন" data-en="Track">ট্র্যাক করুন</span>
            </button>
        </form>
        <p class="muted tiny" style="margin: 8px 0 0" data-bn="অ্যাকাউন্ট ছাড়া জমা দেওয়া মামলা এই কোড দিয়ে খুলুন।" data-en="Open anonymously-submitted cases with this code.">অ্যাকাউন্ট ছাড়া জমা দেওয়া মামলা এই কোড দিয়ে খুলুন।</p>
    </div>

    <!-- Server-side filter chips -->
    <div class="chip-row" style="margin-bottom: 16px">
        @foreach (var f in new[] { "All", "Submitted", "UnderReview", "Approved", "Rejected" })
        {
            <a class="chip chip-sm @(Model.ActiveStatusFilter == f ? "active" : "")"
               asp-action="Track" asp-route-status="@f">@StatusText.Bn(f)</a>
        }
    </div>

    @if (!Model.Cases.Any())
    {
        <div class="empty-state">
            <div class="item-ico"><i data-lucide="folder-open"></i></div>
            <h3 data-bn="এখনো কোনো মামলা নেই" data-en="No cases yet">এখনো কোনো মামলা নেই</h3>
            <p class="muted" data-bn="চ্যাটে সমস্যা বলুন অথবা ফর্মে জমা দিন।" data-en="Describe your problem in chat or use the form.">চ্যাটে সমস্যা বলুন অথবা ফর্মে জমা দিন।</p>
            <div class="row" style="justify-content: center; gap: 10px">
                <a class="btn btn-primary btn-sm" asp-controller="Home" asp-action="Index"><i data-lucide="message-circle"></i> চ্যাট</a>
                <a class="btn btn-outline btn-sm" asp-controller="Case" asp-action="Submit"><i data-lucide="edit-3"></i> ফর্ম</a>
            </div>
        </div>
    }
    else
    {
        <div class="list-rows">
            @foreach (var c in Model.Cases)
            {
                <a class="item-row" asp-controller="Case" asp-action="Result"
                   asp-route-id="@c.CaseId" asp-route-code="@c.TrackingCode">
                    @if (c.HasUnread)
                    {
                        <span class="unread-dot" title="আইনজীবী মতামত দিয়েছেন / New activity" aria-label="Unread activity"></span>
                    }
                    <div class="item-ico"><i data-lucide="folder"></i></div>
                    <div style="flex: 1; min-width: 0">
                        <b>@c.Title</b><br />
                        <small class="muted">@c.CategoryName · @c.CreatedAt.ToString("d MMM yyyy")</small>
                    </div>
                    <span class="badge badge-@c.Status.ToLower()">@StatusText.Bn(c.Status)</span>
                </a>
            }
        </div>
    }

    <!-- Mobile FAB -->
    <a class="fab" asp-controller="Home" asp-action="Index" aria-label="নতুন মামলা"><i data-lucide="plus"></i></a>
</main>
```
(`.fab` already exists in main.css — verified at line ~724. If the selector is `.fab` with a `<button>`, an `<a>` works identically as it's styled on the class.)

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Web/Controllers/CaseController.cs src/MuktoAin.Web/Views/Case/Track.cshtml src/MuktoAin.Web/ViewModels/CaseViewModels.cs
git commit -m "feat(web): My Cases — guest code lookup, server filters, unread dots; Submit prefill"
```

---

### Task A8: Form path → chat transcript unification + Submit view polish

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/CaseController.cs` (Submit POST routes through ChatService)
- Modify (full replace): `src/MuktoAin.Web/Views/Case/Submit.cshtml`

**Interfaces:**
- Consumes: `ChatService.GetOrCreateSessionAsync/AppendMessageAsync/CommitToCaseAsync` (A3); existing `CaseSubmitViewModel` (fields `CategoryId, DistrictId, Title, Description, Language, IsAnonymous, Categories, Districts`); `CaseSubmissionDto(int CategoryId, byte DistrictId, string Title, string Description, string Language, bool IsAnonymous)` + `CaseSubmissionResultDto(int CaseId, string? AnonymousTrackingCode)` (Application/DTOs — verified).
- Produces: form submissions create a case WITH a persisted transcript (same lifecycle as chat path) and an initial document draft, so `/Case/Result` never needs the legacy no-doc fallback in A6.

- [ ] **Step 1: Rewire Submit POST**

In `CaseController.cs`, REPLACE the whole existing `Submit(CaseSubmitViewModel vm)` POST action with:
```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CaseSubmitViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(vm);
            return View(vm);
        }

        var lang = !string.IsNullOrWhiteSpace(vm.Language) && vm.Language != "bn"
            ? vm.Language
            : (Request.Cookies["mkt-lang"] ?? vm.Language ?? "bn");

        var currentUserId = GetCurrentUserId();

        // Form answers become the case's transcript (unification rule: every
        // case = chat transcript + draft chain, regardless of entry path).
        var chatService = HttpContext.RequestServices
            .GetRequiredService<MuktoAin.Application.Services.ChatService>();
        var session = await chatService.GetOrCreateSessionAsync(currentUserId, null, vm.Title);
        await chatService.AppendMessageAsync(session.ChatSessionId, "user",
            $"বিষয়: {vm.Title}\nবিভাগ: {vm.Categories.FirstOrDefault(c => c.Value == vm.CategoryId.ToString())?.Text ?? vm.CategoryId.ToString()}\nবিবরণ: {vm.Description}", null);

        var categoryEntity = await _categoryRepo.GetByIdAsync(vm.CategoryId);
        var documentType = categoryEntity?.Name switch
        {
            var n when n.Contains("শ্রম") || n.Contains("Labour", StringComparison.OrdinalIgnoreCase) => "LabourComplaint",
            var n when n.Contains("ডায়েরি") || n.Contains("Diary", StringComparison.OrdinalIgnoreCase) => "GeneralDiary",
            var n when n.Contains("তথ্য") || n.Contains("Information", StringComparison.OrdinalIgnoreCase) => "RtiRequest",
            var n when n.Contains("ভোক্তা") || n.Contains("Consumer", StringComparison.OrdinalIgnoreCase) => "ConsumerComplaint",
            _ => "LabourComplaint"
        };

        MuktoAin.Application.DTOs.ChatCommitResultDto result;
        try
        {
            result = await chatService.CommitToCaseAsync(
                session.ChatSessionId,
                vm.CategoryId,
                vm.DistrictId,
                vm.Title,
                notificationEmail: null,
                vm.IsAnonymous,
                currentUserId,
                documentType);
        }
        catch (Exception)
        {
            // Fall back to the legacy direct-submission path (no transcript)
            var dto = new CaseSubmissionDto(vm.CategoryId, vm.DistrictId, vm.Title, vm.Description, lang, vm.IsAnonymous);
            var legacy = await _caseService.SubmitCaseAsync(dto, currentUserId);
            RememberTrackedCase(legacy.CaseId, legacy.AnonymousTrackingCode);
            TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
            TempData["SuccessEn"] = "Case submitted successfully!";
            if (legacy.AnonymousTrackingCode != null)
                TempData["TrackingCode"] = legacy.AnonymousTrackingCode;
            return RedirectToAction(nameof(Result),
                new { id = legacy.CaseId, code = legacy.AnonymousTrackingCode });
        }

        RememberTrackedCase(result.CaseId, result.AnonymousTrackingCode);

        TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
        TempData["SuccessEn"] = "Case submitted successfully!";
        if (result.AnonymousTrackingCode != null)
            TempData["TrackingCode"] = result.AnonymousTrackingCode;

        return RedirectToAction(nameof(Result),
            new { id = result.CaseId, code = result.AnonymousTrackingCode });
    }
```
(The `CaseSubmissionDto` fallback keeps the old path alive — belt and braces per Global Constraint 1.)

- [ ] **Step 2: Full-replace Views/Case/Submit.cshtml**

```html
@model CaseSubmitViewModel
@{
    ViewData["Title"] = "মামলা জমা দিন — মুক্ত আইন";
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <span data-bn="সমস্যা জমা দিন" data-en="Submit a case">সমস্যা জমা দিন</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="edit-3"></i> FR-2 · বিকল্প পথ</span>
        <h1 class="page-title" data-bn="মামলা জমা দিন" data-en="Submit a case">মামলা জমা দিন</h1>
        <p class="page-sub" data-bn="ফর্মে গোছাতে পছন্দ করলে এখানে জমা দিন — চ্যাটের মতোই একই প্রক্রিয়া।" data-en="Prefer forms? Submit here — same pipeline as the chat.">ফর্মে গোছাতে পছন্দ করলে এখানে জমা দিন — চ্যাটের মতোই একই প্রক্রিয়া।</p>
    </div>

    <div class="grid" style="grid-template-columns: minmax(0, 1fr); gap: 20px">
        <div class="card">
            <form asp-action="Submit" method="post">
                <div asp-validation-summary="ModelOnly" class="text-danger"></div>

                <div style="margin-bottom: 14px">
                    <label class="form-label" asp-for="CategoryId" data-bn="অভিযোগের ধরন" data-en="Category">অভিযোগের ধরন</label>
                    <select class="input" asp-for="CategoryId" asp-items="Model.Categories"></select>
                </div>

                <div style="margin-bottom: 14px">
                    <label class="form-label" asp-for="DistrictId" data-bn="জেলা (৬৪)" data-en="District (64)">জেলা (৬৪)</label>
                    <select class="input" asp-for="DistrictId" asp-items="Model.Districts"></select>
                </div>

                <div style="margin-bottom: 14px">
                    <label class="form-label" asp-for="Title" data-bn="শিরোনাম" data-en="Title">শিরোনাম</label>
                    <input class="input" asp-for="Title" maxlength="250" placeholder="যেমন: ৩ মাসের বকেয়া বেতন" />
                    <span class="text-danger" asp-validation-for="Title"></span>
                </div>

                <div style="margin-bottom: 14px">
                    <label class="form-label" asp-for="Description" data-bn="বিবরণ" data-en="Description">বিবরণ</label>
                    <textarea class="input autogrow" asp-for="Description" rows="5" maxlength="5000"
                              placeholder="বাংলা / English / Banglish — যেভাবে খুশি লিখুন"
                              data-counter="#desc-counter"></textarea>
                    <small class="muted tiny" id="desc-counter"></small>
                    <span class="text-danger" asp-validation-for="Description"></span>
                </div>

                <div class="alert alert-warn tiny" style="margin-bottom: 14px">
                    <input type="checkbox" asp-for="IsAnonymous" style="margin-right: 8px" />
                    <span data-bn="বেনামে জমা দিন — একটি ট্র্যাকিং কোড পাবেন যা দিয়ে মামলা দেখতে পারবেন" data-en="Submit anonymously — you'll get a tracking code to view the case">বেনামে জমা দিন — একটি ট্র্যাকিং কোড পাবেন যা দিয়ে মামলা দেখতে পারবেন</span>
                </div>

                <div class="row wrap" style="gap: 10px">
                    <button class="btn btn-primary" type="submit">
                        <i data-lucide="send"></i> <span data-bn="জমা দিন" data-en="Submit">জমা দিন</span>
                    </button>
                    <a class="btn btn-ghost" asp-controller="Home" asp-action="Index">
                        <i data-lucide="message-circle"></i> <span data-bn="চ্যাটের মাধ্যমে জমা দিন" data-en="Submit via chat instead">চ্যাটের মাধ্যমে জমা দিন</span>
                    </a>
                </div>
            </form>
        </div>

        <aside class="card">
            <h2 class="section-h"><i data-lucide="workflow"></i> <span data-bn="এরপর কী হবে" data-en="What happens next">এরপর কী হবে</span></h2>
            <ol style="padding-left: 20px; line-height: 2.2">
                <li data-bn="AI আপনার অধিকার ব্যাখ্যা করবে (ধারাসহ)" data-en="AI explains your rights (with citations)">AI আপনার অধিকার ব্যাখ্যা করবে (ধারাসহ)</li>
                <li data-bn="খসড়া দলিল তৈরি হবে — আপনি সম্পাদনা করতে পারবেন" data-en="A draft document is generated — you can edit it">খসড়া দলিল তৈরি হবে — আপনি সম্পাদনা করতে পারবেন</li>
                <li data-bn="আইনজীবী অনুমোদনের পরেই PDF ডাউনলোড আনলক হবে" data-en="PDF download unlocks only after lawyer approval">আইনজীবী অনুমোদনের পরেই PDF ডাউনলোড আনলক হবে</li>
            </ol>
        </aside>
    </div>
</main>

@section Scripts {
    <partial name="_ValidationScriptsPartial" />
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/MuktoAin.Web/Controllers/CaseController.cs src/MuktoAin.Web/Views/Case/Submit.cshtml
git commit -m "feat(web): form path unified into chat transcript + Submit view refresh"
```

---

## PART A — Final Verification Gate **[OPENCODE VERIFY — Antigravity stops here]**

After all 8 tasks are committed, OpenCode runs this gate:

1. `dotnet build` — clean.
2. `dotnet test tests/MuktoAin.UnitTests` — all pass (existing tests may need ZERO changes; if any test referenced `MapToResultViewModel` or the old Result behavior, update that test minimally).
3. Human runs `scripts/08_redesign_tables.sql` in SSMS (idempotent).
4. `dotnet run` + curl smoke:
   - `GET /` → chat shell HTML contains `chat-thread` and `draft-modal`
   - `GET /Case/SubmitOptions` → district JSON
   - `GET /Chat/Quota` → `{ remainingToday, dailyLimit, isLoggedIn }`
   - `POST /Chat/New` with empty body → `{ chatSessionId, title }`
5. Browser pass: chat ask (with Gemini key configured) → citation chips → Generate Draft modal → case page with real timeline → edit → send to lawyer → Track list shows UnderReview + unread semantics on lawyer action (A9/B2 wire the lawyer side).
6. Verify NO regressions: `/Case/Submit` form posts still work (transcript path), `/Search`, `/Category`, `/Account/*`, `/Admin/*` unchanged.
7. Update `plans/Dependency_plan.md` redesign-wave task boxes.

**PART A depends on nothing in PART B.** Execute in either order or in parallel by two agents.
