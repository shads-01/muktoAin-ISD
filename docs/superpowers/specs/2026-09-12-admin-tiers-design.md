# Admin Tiers (SuperAdmin) — Design

Date: 2026-09-12
Status: Approved for implementation planning

## Problem

There is exactly one admin level today (`UserRole.Admin`). Every admin
account has identical power, including over other admin accounts — except
`UserManagementService.SetAccountStatusAsync` currently blocks *any*
suspension of *any* admin, by anyone, unconditionally
(`if (user.Role == UserRole.Admin ...) return false;`). There is also no
way to create a new admin account at all: `AccountController.Register`
only ever creates `Citizen` or `Lawyer` roles. In practice this means the
one admin account seeded by `SeedAdminUser` at startup is the only admin
that can ever exist through the application itself.

This spec adds a second admin tier (SuperAdmin) that can create other admin
accounts and manage them, and extends that same tier boundary to the
platform's financial actions.

## Scope

In scope:
- `bool IsSuperAdmin` on `User`, meaningful only when `Role == Admin`.
- SuperAdmin-only: create a new Admin (or SuperAdmin) account; suspend/
  reactivate a regular Admin; promote a regular Admin to SuperAdmin.
- SuperAdmin-only: `AdminController.RefundOrder`, `ApprovePayout`,
  `MarkOrderPaid` (the platform's real/simulated money-movement actions).
- A `[SuperAdminOnly]` authorization filter (or policy) so the boundary is
  declared once and applied consistently, not re-implemented per action.
- Everything else already in `AdminController` (Users, Lawyers verification,
  Scenarios, Categories, Corpus, AiLogs, Dashboard/Analytics/health
  endpoints) stays available to both tiers, unchanged.

Out of scope (explicitly deferred, not silently decided):
- Demoting a SuperAdmin back to Admin, or suspending a SuperAdmin at all,
  through the application. See "Guard rails" below — this is a deliberate
  one-way door, not a gap.
- More than two tiers / a numeric ranked hierarchy. Nothing in this request
  calls for it.
- Notifying admins about admin-tier changes — covered by the separate
  [[2026-09-12-notifications-design]] spec's `NewLawyerApplication`-style
  trigger, not duplicated here. (A `SuperAdmin promoted/created an admin`
  notification can be added as a sixth trigger to that system later with no
  schema change here.)
- Fixing `AdminController.EmbeddingProgress`/`GeminiKeyStatus`'s
  `[AllowAnonymous]` bug (`PROJECT_AUDIT_REPORT.md`, Backend/Admin #1) —
  real bug, tracked there, unrelated to tiering; not folded into this spec
  to keep it focused.

## Architecture & components

**Data model** — one column, no new table:

```csharp
// User.cs
public bool IsSuperAdmin { get; set; }  // meaningful only when Role == Admin
```

Added via a new numbered script (`scripts/11_add_user_issuperadmin.sql`,
`ALTER TABLE [dbo].[USER] ADD IsSuperAdmin BIT NOT NULL DEFAULT 0`) plus the
corresponding `UserConfiguration.cs` change — same mechanism the last three
schema changes in this repo used (`09_fix_chat_session_sessionkey_unique_index.sql`,
`10_fix_case_title_column_width.sql`).

**Bootstrap** — `SeedAdminUser.SeedAsync` sets `IsSuperAdmin = true` on the
one admin account it creates. Without this, the seeded admin could log in
but could never create a second admin (chicken-and-egg) — the seeder is the
only place a SuperAdmin can be created out-of-band, matching how this
project already treats seeded/manually-run SSMS state as the trusted
starting point.

**Authorization** — a small custom `IAuthorizationRequirement`/handler (or
the simpler route: an `IAsyncActionFilter` checking
`User.FindFirst("IsSuperAdmin")?.Value == "true"`) exposed as
`[SuperAdminOnly]`, applied alongside the existing
`[Authorize(Roles = "Admin")]` on:
- `AdminController.CreateAdmin` (new action, GET+POST)
- `AdminController.SuspendAdmin` (new — see below, distinct from the
  existing `Suspend` which stays for Citizen/Lawyer accounts)
- `AdminController.PromoteAdmin` (new)
- `AdminController.RefundOrder`, `ApprovePayout`, `MarkOrderPaid` (existing
  actions, filter added)

Whether `IsSuperAdmin` is carried as a claim (cheapest: added in
`UserRoleClaimsTransformation`, alongside where `Role` already becomes a
claim, so `[SuperAdminOnly]` needs no extra DB round-trip) or checked via
`UserManager` per-request is an implementation-plan-level decision, not a
design one — the claims-transformation route is recommended since that
transformer already exists and already runs on every request for the
`Role` claim.

**New/changed service methods** (`UserManagementService`):

```csharp
Task<AdminAccountResult> CreateAdminAsync(
    string fullName, string email, bool asSuperAdmin, int actingSuperAdminId);
    // returns the created user + a password-reset link, mirroring
    // AccountController.ForgotPassword's dev-mode link pattern (no SMTP).
    // Sets the new User.CreatedByAdminId = actingSuperAdminId — this column
    // already exists (design.md §2.1: "Enforces admins can only be created
    // by admins") but has never been set by any code path, since nothing
    // could create an admin before this feature. No schema change needed
    // for this part.

Task<bool> SetAdminStatusAsync(
    int targetAdminId, AccountStatus status, int actingSuperAdminId);
    // guard: target must have IsSuperAdmin == false (see Guard rails)

Task<bool> PromoteToSuperAdminAsync(int targetAdminId, int actingSuperAdminId);
    // guard: target must currently be IsSuperAdmin == false
```

The existing `SetAccountStatusAsync(userId, status, actingAdminId)` keeps
its current unconditional "never suspend an Admin" behavior for the
existing `Suspend` action (Citizen/Lawyer management) — it is simply never
called with an admin's `userId` from that action's view going forward,
since admin suspension now has its own action guarded by
`[SuperAdminOnly]` and its own guard rail below. No existing call site
needs to change behavior.

**Guard rails (the actual design decision here):**

A SuperAdmin can never suspend, promote/demote, or otherwise change the
status of another account that is already `IsSuperAdmin == true` —
including their own. This is checked inside `SetAdminStatusAsync` and
`PromoteToSuperAdminAsync` (`if (target.IsSuperAdmin) return false;`),
mirroring the shape of the existing self-suspend guard in
`SetAccountStatusAsync`. Consequences, stated explicitly rather than left
implicit:
- Once an account becomes a SuperAdmin, it can only ever be un-suspended,
  suspended, or demoted by direct SSMS/DB action — never through the app.
  This is the same trust boundary the project already draws around the
  seeded admin and manually-authored schema; it is not a new kind of gap.
- Two SuperAdmins can never lock each other out or engage in a
  suspend-war. This also means there is no "last SuperAdmin" edge case to
  guard against — the invariant that SuperAdmins are mutually untouchable
  makes that scenario structurally impossible rather than something to
  detect and block at runtime.
- A regular Admin still cannot suspend/promote anyone (unchanged) — only
  `[SuperAdminOnly]` actions reach `SetAdminStatusAsync`/
  `PromoteToSuperAdminAsync` at all.

**UI** — `Admin/Users.cshtml` gains a "Manage Admins" section (visible only
to `IsSuperAdmin`) listing admin accounts with tier badge (SuperAdmin /
Admin), a "Create Admin" button/form (Full Name, Email, tier radio —
default Admin), and, per non-SuperAdmin admin row, Suspend/Reactivate and
"Promote to SuperAdmin" actions — each behind the same `confirm()` pattern
already used for `Suspend`/`RefundOrder` elsewhere on this page (the audit
found this pattern already correctly applied across Admin views; extending
it, not introducing it fresh). SuperAdmin rows show no action buttons at
all, visually reinforcing the guard rail rather than showing a button that
would just fail server-side.

## Error handling

- `CreateAdminAsync`: reuses `IdentityErrorMapper` for
  `UserManager.CreateAsync` failures (duplicate email, etc.) — same mapping
  `AccountController.Register` already uses, not reinvented.
- `SetAdminStatusAsync`/`PromoteToSuperAdminAsync` return `false` (not an
  exception) on every guard-rail violation — the controller turns that into
  the same `TempData["Error"]`/`TempData["ErrorEn"]` pattern
  `AdminController.Suspend` already uses, so a blocked action reads as a
  normal, explained no-op rather than a 500 or a silent nothing.
- `[SuperAdminOnly]` on a non-SuperAdmin caller returns 403
  (`Home/AccessDenied`), same as `[Authorize(Roles="Admin")]` already does
  for non-admins — no new error page needed.

## Testing

Follows the existing Moq/xUnit style in `tests/MuktoAin.UnitTests/`:

- `UserManagementServiceTests`: `CreateAdminAsync` creates the account with
  the requested tier and returns a working reset link;
  `SetAdminStatusAsync` returns `false` and makes no change when the target
  `IsSuperAdmin == true` (including when target == actor); same shape for
  `PromoteToSuperAdminAsync`; both succeed against a regular Admin target.
- `AdminControllerTests`: `[SuperAdminOnly]` actions return 403 for a
  non-SuperAdmin admin caller (extends the existing pattern already used to
  test `[Authorize(Roles="Admin")]` rejecting non-admins);
  `RefundOrder`/`ApprovePayout`/`MarkOrderPaid` specifically re-verified
  under the new filter since they're existing actions gaining a new guard.
- `SeedAdminUserTests` (if one exists, else add): seeded admin has
  `IsSuperAdmin == true`.
