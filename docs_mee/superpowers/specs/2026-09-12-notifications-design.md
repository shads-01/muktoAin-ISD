# In-App Notifications — Design

Date: 2026-09-12
Status: Approved for implementation planning

## Problem

Nothing in the app tells a user something happened to their case, profile,
or queue unless they manually revisit the page:

- A citizen finds out a lawyer approved/rejected their document only by
  reopening `Case/Result`.
- A lawyer finds out their bar-registration verification was approved or
  rejected only by reopening `Lawyer/Status`.
- A citizen gets no confirmation their case was actually submitted beyond
  the one-time `TempData["Success"]` flash on the redirect that created it.
- A lawyer gets no signal an honorarium payment landed in their balance
  until they open `Lawyer/Payments`.
- An admin discovers a new lawyer application only by opening `Admin/Lawyers`
  or noticing the dashboard's "Verifications Waiting" count changed.

This spec adds a generic, persistent, in-app notification system covering
these five events across all three personas.

## Scope

In scope:
- One new `Notification` entity/table, a `NotificationService`, a bell-icon
  UI in the shared layout, a full paginated notification list page.
- Exactly five trigger events (below) — each wired at its existing service
  call-site, no new business logic invented to justify a sixth.
- Bilingual (Bangla/English) notification text, following the site's
  existing `data-bn`/`data-en` + `StatusText`-style pattern.
- Retiring `Case.HasUnreadActivity` in favor of the new system (it becomes
  redundant with `DocumentDecided` notifications; keeping both would be two
  competing "you have unread stuff" signals for the same event).

Out of scope (explicitly deferred, not silently decided):
- Email delivery. SMTP is not configured in this build (confirmed in
  `AccountController.ForgotPassword`, which only surfaces the reset link
  directly in Development). `Case.NotificationEmail` remains an unused
  field for anonymous cases until a future spec wires up SMTP.
- Notifications for anonymous (tracking-code-only) citizens. `Notification`
  is keyed to `UserId`; a citizen with no account has nothing to receive
  notifications into. Anonymous citizens keep using `Case/Track` +
  tracking code as today.
- Real-time push (SignalR/WebSockets). Polling on the same cadence as the
  existing `Chat/Quota`/`Admin/HealthStatus` endpoints is sufficient for
  events that are not latency-sensitive, and avoids adding a transport this
  codebase doesn't otherwise use.
- Admin: new payout request notifications (considered, deferred — the
  existing `Admin/Transactions` pending-payouts list already surfaces this
  synchronously; can be added as a sixth trigger later with no schema
  change).
- Notification preferences/mute controls. Nobody asked for them; adding
  them now is speculative generality.

## Architecture & components

**Data model** — new `Notification` entity, added the same way this repo
already extends its schema (numbered script in `scripts/` + an
`EF Configuration` class + `DbSet`, per `08_redesign_tables.sql` /
`09_part_b_tables.sql`, which already added ChatSession/PaymentOrder/etc.
after the original 14):

```csharp
public enum NotificationType
{
    CaseSubmitted = 0,
    DocumentDecided = 1,
    LawyerVerified = 2,
    PaymentReceived = 3,
    NewLawyerApplication = 4
}

public class Notification
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }             // recipient
    public NotificationType Type { get; set; }
    public int? RelatedCaseId { get; set; }
    public int? RelatedDocumentId { get; set; }
    public int? RelatedLawyerProfileId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

No stored message string — the row carries only the type + related-entity
ids. Bangla/English text is rendered at read time by a new
`NotificationTextFormatter` (mirrors the existing `StatusText`/
`MarkdownText` helper pattern already in `Controllers/`), which maps
`(Type, related entity data)` to a `(TextBn, TextEn)` pair and a deep-link
URL. This keeps translation logic in one place instead of baking English
strings into stored rows, and means a future new UI language needs no data
migration.

**Service** — `INotificationService` / `NotificationService`
(`MuktoAin.Application.Services`, same layer as `AiBudgetService`):

```csharp
Task NotifyAsync(int userId, NotificationType type,
    int? caseId = null, int? documentId = null, int? lawyerProfileId = null);
Task NotifyAllAdminsAsync(NotificationType type,
    int? caseId = null, int? documentId = null, int? lawyerProfileId = null);
Task<IReadOnlyList<NotificationDto>> GetRecentAsync(int userId, int take = 10);
Task<int> GetUnreadCountAsync(int userId);
Task<PagedResult<NotificationDto>> GetPagedAsync(int userId, int page, int pageSize);
Task<bool> MarkReadAsync(int notificationId, int userId); // ownership-checked
Task MarkAllReadAsync(int userId);
```

`NotifyAsync` failures must never break the operation that triggered them —
wrapped in try/catch + `ILogger` at the call site (or inside the service
itself), the same defensive posture `AiLogService` already takes relative
to the AI pipeline it logs. A failed notification write is a degraded
experience, not a broken review/verification/payment.

**Call sites** (5 total, each a one-line addition to existing methods):

| Trigger | Where | Recipient |
|---|---|---|
| `CaseSubmitted` | `CaseService.SubmitCaseAsync`, `ChatService.CommitToCaseAsync` (only when `userId.HasValue` — anonymous submissions have no recipient) | the citizen |
| `DocumentDecided` | `LawyerReviewService.SubmitReviewAsync`, after the case's `HasUnreadActivity` block (which this replaces) | the case's `UserId` (skip if anonymous) |
| `LawyerVerified` | `LawyerVerificationService.VerifyAsync` | the lawyer |
| `PaymentReceived` | `PaymentService.CreateHonorariumOrderAsync`, only inside the existing `if (order.LawyerProfileId.HasValue)` branch | the lawyer (resolve `UserId` from `LawyerProfileId`) |
| `NewLawyerApplication` | `AccountController.Register`, inside the existing `if (isLawyer)` branch | every user with `Role == Admin` (`NotifyAllAdminsAsync`) |

**Delivery/UI**

- `NotificationController` (new): `GET /Notification/Unread` (JSON —
  `{ count, items: [{id, textBn, textEn, url, createdAt}] }`, polled every
  ~30s the same way `chat.js` already polls `/Chat/Quota`), `GET
  /Notification/Index` (full paginated Razor view, page size 20, matching
  `LawyerController.History`'s pagination pattern — not the unpaginated
  `Admin/Users`/`Lawyer/Queue` pattern this system's own audit flagged),
  `POST /Notification/MarkRead` (`[ValidateAntiForgeryToken]`, ownership
  checked via `MarkReadAsync(id, currentUserId)` — return `Forbid()` on
  mismatch, exactly the check this audit found missing on `Chat/Ask`;
  Notifications gets it right from the start).
- Bell icon + unread-count badge added to `_Layout.cshtml`'s nav (visible
  only when `User.Identity.IsAuthenticated`), dropdown showing the 10 most
  recent via `Unread`, "See all" linking to `Index`. Clicking an item marks
  it read and follows its deep link (`Case/Result`, `Lawyer/Status`, etc.).

**Cleanup folded in:** `Case.HasUnreadActivity` and its two read/write
sites (`CaseController.Result`'s clear-on-view, `LawyerReviewService`'s
set-on-review) are removed once `DocumentDecided` covers the same signal.
This also means one line changes in the already-published
`PROJECT_AUDIT_REPORT.md`/audit artifact is *not* required — that report
never flagged `HasUnreadActivity` itself, this is a design-time consolidation,
not a bug fix.

## Error handling

- `NotifyAsync`/`NotifyAllAdminsAsync`: caught and logged at
  `LogWarning`, never rethrown — a notification-write failure must not
  fail the case submission, review, verification, or payment it's attached
  to.
- `MarkReadAsync`: returns `false` (controller returns `Forbid()`) when the
  notification's `UserId` doesn't match the caller — this is the one new
  surface in the whole feature that touches another table by id from
  client input, so it gets the ownership check the audit's IDOR findings
  were about, by design, not as an afterthought.
- `GetPagedAsync`/`GetRecentAsync`/`GetUnreadCountAsync`: always scoped to
  the passed `userId` in the query itself (never "fetch all, filter after")
  so there's no path that accidentally returns another user's rows.
- Anonymous requests to any `NotificationController` action: the actions
  require `[Authorize]` at the controller level (unlike `ChatController`,
  which is deliberately anonymous-friendly) — there is no anonymous
  notification recipient in this design.

## Testing

Follows the existing Moq/xUnit unit-test style in
`tests/MuktoAin.UnitTests/Services/`:

- `NotificationServiceTests`: `NotifyAsync` creates the expected row per
  type; `MarkReadAsync` returns `false` and does not mutate `IsRead` when
  `userId` doesn't match the notification's owner; `GetPagedAsync` paginates
  and never returns another user's rows; `NotifyAsync` swallowing a
  repository exception doesn't propagate.
- One test per call site confirming the trigger actually fires:
  `CaseServiceTests`/`ChatServiceTests` (CaseSubmitted, skipped when
  anonymous), `LawyerReviewServiceTests` (DocumentDecided, all 3 decisions),
  `LawyerVerificationServiceTests` (LawyerVerified), `PaymentServiceTests`
  (PaymentReceived, only when `LawyerProfileId` resolves), and an
  `AccountControllerTests` case for `NewLawyerApplication` reaching every
  admin.
- `NotificationControllerTests` (new, mirrors `AccountControllerTests`'
  shape): `MarkRead` on someone else's notification id returns `Forbid`;
  `Unread`/`Index` never leak another user's rows.
