# Admin Tiers (SuperAdmin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second admin tier (SuperAdmin) that can create new admin accounts, suspend/promote regular admins, and gate the platform's financial admin actions — while a SuperAdmin can never touch another SuperAdmin's status.

**Architecture:** One new `bool IsSuperAdmin` column on the existing `USER` table (no new table). The existing `UserRoleClaimsTransformation` (which already projects `User.Role` into a role claim on every request) gains a second claim for it; a new ASP.NET Core authorization policy (`SuperAdminOnly`) reads that claim, applied via the framework's own `[Authorize(Policy = "SuperAdminOnly")]` — no bespoke attribute class needed. `UserManagementService` (already the one seam for admin-on-user actions) gains three methods with the mutual-immutability guard rail baked in.

**Tech Stack:** ASP.NET Core MVC (.NET 8) Identity + policy-based authorization, EF Core (SQL Server), xUnit + Moq, Razor Views.

**Spec:** `docs/superpowers/specs/2026-09-12-admin-tiers-design.md`

## Global Constraints

- No EF migrations — schema changes ship as a numbered idempotent script in `scripts/`, matching every existing script in that folder.
- Never add `Co-Authored-By` or any AI-attribution trailer to commit messages (project CLAUDE.md).
- Do not commit, stage, or push — leave all changes in the working tree (project CLAUDE.md §6; Shads is the sole committer). Every "Commit" step below is written for a human to run later.
- Bangla/English pairs for all new user-facing text and validation messages, following the `RegisterViewModel`/`AccountController` pattern already in this codebase.
- The guard rail is the entire point of this feature: `SetAdminStatusAsync`/`PromoteToSuperAdminAsync` must return `false` — never throw, never silently no-op without a caller-visible signal — whenever the target account already has `IsSuperAdmin == true`, including when target and actor are the same account.
- After finishing all tasks, update `plans/Dependency_plan.md` per this project's mandatory tracking rule — this is the final task below.

---

## Task 1: Domain + Infrastructure — `User.IsSuperAdmin` column

**Files:**
- Modify: `src/MuktoAin.Domain/Entities/User.cs`
- Modify: `src/MuktoAin.Infrastructure/Data/Configurations/UserConfiguration.cs`
- Create: `scripts/12_add_user_issuperadmin.sql`

**Interfaces:**
- Produces: `User.IsSuperAdmin` (`bool`, default `false`) — every later task in this plan depends on this exact property name.

- [ ] **Step 1: Add the property**

In `src/MuktoAin.Domain/Entities/User.cs`, add immediately after `public int? CreatedByAdminId { get; set; }` / `public User? CreatedByAdmin { get; set; }`:

```csharp
// SuperAdmin can create/suspend/promote other Admin accounts; a regular
// Admin cannot. Meaningful only when Role == UserRole.Admin.
public bool IsSuperAdmin { get; set; }
```

- [ ] **Step 2: Add the EF configuration**

In `src/MuktoAin.Infrastructure/Data/Configurations/UserConfiguration.cs`, add inside `Configure`, after the existing `builder.Property(u => u.PreferredLanguage)...` line:

```csharp
builder.Property(u => u.IsSuperAdmin).IsRequired().HasDefaultValue(false);
```

- [ ] **Step 3: Write the SQL script**

```sql
/* ============================================================
   MuktoAin — Admin tiers: USER.IsSuperAdmin (2026-09-12)
   IDEMPOTENT: safe to re-run; the ALTER is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USER]') AND name = N'IsSuperAdmin')
    ALTER TABLE [dbo].[USER] ADD IsSuperAdmin BIT NOT NULL DEFAULT (0);
GO
```

- [ ] **Step 4: Build**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run the script against the local database**

Run the script in SSMS (or `sqlcmd -S <server> -d MuktoAin -i scripts/12_add_user_issuperadmin.sql`).

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Domain/Entities/User.cs src/MuktoAin.Infrastructure/Data/Configurations/UserConfiguration.cs scripts/12_add_user_issuperadmin.sql
git commit -m "feat(admin-tiers): add User.IsSuperAdmin column"
```

---

## Task 2: Auth — `IsSuperAdmin` claim and the `SuperAdminOnly` policy

**Files:**
- Modify: `src/MuktoAin.Web/Auth/UserRoleClaimsTransformation.cs`
- Modify: `src/MuktoAin.Web/Program.cs`
- Test: `tests/MuktoAin.UnitTests/Auth/UserRoleClaimsTransformationTests.cs` (create if it doesn't exist; check first — this project has a `tests/MuktoAin.UnitTests/Auth/` folder already per its listed directories)

**Interfaces:**
- Produces: a claim of type `"IsSuperAdmin"` with value `"true"` present on an authenticated `ClaimsPrincipal` exactly when `User.IsSuperAdmin` is `true`; an ASP.NET Core authorization policy named `"SuperAdminOnly"` — Task 6's controller actions apply this via `[Authorize(Policy = "SuperAdminOnly")]`.

- [ ] **Step 1: Check for an existing test file, write the failing test**

Run: `ls tests/MuktoAin.UnitTests/Auth/` to see what's already there (this plan was written from the directory listing, not the file contents — read whatever's there before adding, to match its existing style/usings for `UserRoleClaimsTransformation` if a test for it already exists; if it does, add the new test to it instead of creating a second file).

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Web.Auth;
using Xunit;

namespace MuktoAin.UnitTests.Auth;

public class UserRoleClaimsTransformationSuperAdminTests
{
    private static UserManager<User> NewUserManager(User? user)
    {
        var store = new Mock<IUserStore<User>>();
        var manager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
        return manager.Object;
    }

    [Fact]
    public async Task TransformAsync_SuperAdmin_AddsIsSuperAdminClaim()
    {
        var user = new User { Id = 1, Role = UserRole.Admin, IsSuperAdmin = true, FullName = "Root" };
        var transformer = new UserRoleClaimsTransformation(NewUserManager(user));
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformer.TransformAsync(principal);

        Assert.True(result.HasClaim("IsSuperAdmin", "true"));
    }

    [Fact]
    public async Task TransformAsync_RegularAdmin_DoesNotAddIsSuperAdminClaim()
    {
        var user = new User { Id = 2, Role = UserRole.Admin, IsSuperAdmin = false, FullName = "Regular" };
        var transformer = new UserRoleClaimsTransformation(NewUserManager(user));
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "2") }, "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await transformer.TransformAsync(principal);

        Assert.False(result.HasClaim(c => c.Type == "IsSuperAdmin"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter UserRoleClaimsTransformationSuperAdminTests`
Expected: FAIL — no `IsSuperAdmin` claim is ever added yet, so `HasClaim("IsSuperAdmin", "true")` is `false` for both tests (the first assertion fails).

- [ ] **Step 3: Add the claim**

In `UserRoleClaimsTransformation.TransformAsync`, immediately after the existing `identity.AddClaim(new Claim(RoleClaimType, user.Role.ToString()));` line, add:

```csharp
if (user.IsSuperAdmin)
{
    identity.AddClaim(new Claim("IsSuperAdmin", "true"));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter UserRoleClaimsTransformationSuperAdminTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Register the policy**

In `src/MuktoAin.Web/Program.cs`, immediately after the existing `builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();` block, add:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim("IsSuperAdmin", "true"));
});
```

(No `AddAuthorization` call currently exists in `Program.cs` — `AddControllersWithViews()` registers the authorization services implicitly with only the framework defaults, so this is a new call, not an edit to an existing one.)

- [ ] **Step 6: Build**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/MuktoAin.Web/Auth/UserRoleClaimsTransformation.cs src/MuktoAin.Web/Program.cs tests/MuktoAin.UnitTests/Auth/UserRoleClaimsTransformationSuperAdminTests.cs
git commit -m "feat(admin-tiers): project IsSuperAdmin into a claim, add SuperAdminOnly policy"
```

---

## Task 3: Application — DTOs

**Files:**
- Modify: `src/MuktoAin.Application/DTOs/UserListDto.cs`
- Create: `src/MuktoAin.Application/DTOs/AdminAccountResultDto.cs`

**Interfaces:**
- Produces: `UserListDto` gains `IsSuperAdmin` (consumed by Task 6's controller mapping to `AdminUserRowViewModel`); `AdminAccountResultDto(User CreatedUser, string PasswordResetUrl)` — consumed by Task 4's `CreateAdminAsync` return type and Task 6's controller.

- [ ] **Step 1: Extend `UserListDto`**

```csharp
namespace MuktoAin.Application.DTOs;

// S-3.6: read model for the admin user-management list view (/Admin/Users).
public record UserListDto(
    int UserId,
    string FullName,
    string Email,
    string Role,
    string Status,
    bool IsSuperAdmin);
```

This changes the constructor from 5 to 6 positional arguments — `UserManagementService.GetAllUsersAsync` (Task 4) is the only place that constructs it (confirmed: `grep -rn "new UserListDto("` shows one call site), so no other file needs updating for this specific change.

- [ ] **Step 2: Add `AdminAccountResultDto`**

```csharp
using MuktoAin.Domain.Entities;

namespace MuktoAin.Application.DTOs;

public record AdminAccountResultDto(User CreatedUser, string PasswordResetUrl);
```

- [ ] **Step 3: Build**

Run: `dotnet build MuktoAin.slnx`
Expected: FAIL — `UserManagementService.GetAllUsersAsync` still constructs `UserListDto` with 5 arguments. This is expected; Task 4 fixes it. If working strictly one task at a time, this is the one point in this plan where a task boundary leaves the build red — note it here rather than pretend otherwise, and proceed straight to Task 4 before committing.

- [ ] **Step 4: Commit** (after Task 4 makes the build green again — see Task 4's own commit step, which includes these two files)

No separate commit for this task; its files are committed together with Task 4's, since Task 4 is what makes them compile.

---

## Task 4: Application — `UserManagementService` admin-tier methods

**Files:**
- Modify: `src/MuktoAin.Application/Services/IUserManagementService.cs`
- Modify: `src/MuktoAin.Application/Services/UserManagementService.cs`
- Test: `tests/MuktoAin.UnitTests/Services/UserManagementServiceTests.cs` (check if it exists first — none was found in this codebase's `find`-based sweep during planning; create it if so)

**Interfaces:**
- Consumes: `UserListDto` (Task 3, 6-arg), `AdminAccountResultDto` (Task 3), `UserManager<User>` (already injected).
- Produces: `CreateAdminAsync`, `SetAdminStatusAsync`, `PromoteToSuperAdminAsync` — Task 6's controller depends on these exact signatures.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class UserManagementServiceTests
{
    private static Mock<UserManager<User>> NewUserManagerMock()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
    }

    [Fact]
    public async Task CreateAdminAsync_CreatesAdminWithRequestedTier_AndSetsCreatedByAdminId()
    {
        var userManager = NewUserManagerMock();
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => { u.Id = 5; created = u; })
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>())).ReturnsAsync("tok");
        var service = new UserManagementService(userManager.Object);

        var result = await service.CreateAdminAsync(
            "New Admin", "newadmin@example.com", asSuperAdmin: false, actingSuperAdminId: 1);

        Assert.NotNull(created);
        Assert.Equal(UserRole.Admin, created!.Role);
        Assert.False(created.IsSuperAdmin);
        Assert.Equal(1, created.CreatedByAdminId);
        Assert.Equal(5, result.CreatedUser.Id);
        Assert.Contains("tok", result.PasswordResetUrl);
    }

    [Fact]
    public async Task CreateAdminAsync_CanCreateAnotherSuperAdmin()
    {
        var userManager = NewUserManagerMock();
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => { u.Id = 6; created = u; })
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>())).ReturnsAsync("tok");
        var service = new UserManagementService(userManager.Object);

        await service.CreateAdminAsync("New Super", "super2@example.com", asSuperAdmin: true, actingSuperAdminId: 1);

        Assert.True(created!.IsSuperAdmin);
    }

    [Fact]
    public async Task CreateAdminAsync_DuplicateEmail_ThrowsWithIdentityErrorsIntact()
    {
        var userManager = NewUserManagerMock();
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "already used" }));
        var service = new UserManagementService(userManager.Object);

        var ex = await Assert.ThrowsAsync<IdentityCreationFailedException>(() =>
            service.CreateAdminAsync("Dup", "dup@example.com", asSuperAdmin: false, actingSuperAdminId: 1));

        Assert.Single(ex.Errors);
        Assert.Equal("DuplicateEmail", ex.Errors.First().Code);
    }

    [Fact]
    public async Task SetAdminStatusAsync_ReturnsFalse_WhenTargetIsSuperAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = true, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object);

        var ok = await service.SetAdminStatusAsync(9, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.False(ok);
        Assert.Equal(AccountStatus.Active, target.AccountStatus);
        userManager.Verify(m => m.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SetAdminStatusAsync_ReturnsFalse_WhenTargetIsSelf()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 1, Role = UserRole.Admin, IsSuperAdmin = true, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("1")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object);

        var ok = await service.SetAdminStatusAsync(1, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.False(ok);
    }

    [Fact]
    public async Task SetAdminStatusAsync_Succeeds_WhenTargetIsRegularAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = false, AccountStatus = AccountStatus.Active };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        var service = new UserManagementService(userManager.Object);

        var ok = await service.SetAdminStatusAsync(9, AccountStatus.Suspended, actingSuperAdminId: 1);

        Assert.True(ok);
        Assert.Equal(AccountStatus.Suspended, target.AccountStatus);
    }

    [Fact]
    public async Task PromoteToSuperAdminAsync_ReturnsFalse_WhenAlreadySuperAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = true };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        var service = new UserManagementService(userManager.Object);

        var ok = await service.PromoteToSuperAdminAsync(9, actingSuperAdminId: 1);

        Assert.False(ok);
    }

    [Fact]
    public async Task PromoteToSuperAdminAsync_Succeeds_WhenTargetIsRegularAdmin()
    {
        var userManager = NewUserManagerMock();
        var target = new User { Id = 9, Role = UserRole.Admin, IsSuperAdmin = false };
        userManager.Setup(m => m.FindByIdAsync("9")).ReturnsAsync(target);
        userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        var service = new UserManagementService(userManager.Object);

        var ok = await service.PromoteToSuperAdminAsync(9, actingSuperAdminId: 1);

        Assert.True(ok);
        Assert.True(target.IsSuperAdmin);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter UserManagementServiceTests`
Expected: FAIL to compile — the three new methods don't exist yet.

- [ ] **Step 3: Extend the interface**

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Services;

// S-3.6 / FR-18: admin user management -- list users, suspend/activate accounts.
// Identity handles creation via registration; this covers only the admin gap.
public interface IUserManagementService
{
    Task<IEnumerable<UserListDto>> GetAllUsersAsync();

    /// <summary>
    /// Suspends or reactivates a user account. Admins cannot be suspended and an
    /// admin cannot change their own account status (design.md 2.1 guard rails).
    /// </summary>
    /// <returns>false if the user does not exist or the action is forbidden.</returns>
    Task<bool> SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId);

    /// <summary>
    /// SuperAdmin-only: create a new Admin (or SuperAdmin) account. Generates a
    /// random password and returns a password-reset link for the SuperAdmin to
    /// relay out-of-band (no SMTP in this build — mirrors AccountController's
    /// ForgotPassword dev-mode link pattern).
    /// </summary>
    /// <exception cref="IdentityCreationFailedException">
    /// Thrown when UserManager.CreateAsync fails (e.g. duplicate email) — carries
    /// the real IdentityError collection so the caller can map each error to a
    /// form field via IdentityErrorMapper, same as AccountController.Register.
    /// </exception>
    Task<AdminAccountResultDto> CreateAdminAsync(
        string fullName, string email, bool asSuperAdmin, int actingSuperAdminId);

    /// <summary>
    /// SuperAdmin-only: suspend or reactivate a regular Admin account.
    /// </summary>
    /// <returns>false if the target doesn't exist or is itself a SuperAdmin
    /// (including when target == actor) — a SuperAdmin can never change
    /// another SuperAdmin's status, by design.</returns>
    Task<bool> SetAdminStatusAsync(int targetAdminId, AccountStatus status, int actingSuperAdminId);

    /// <summary>
    /// SuperAdmin-only: promote a regular Admin to SuperAdmin.
    /// </summary>
    /// <returns>false if the target doesn't exist or is already a SuperAdmin.</returns>
    Task<bool> PromoteToSuperAdminAsync(int targetAdminId, int actingSuperAdminId);
}
```

- [ ] **Step 4: Implement in `UserManagementService`**

Per the spec ("reuses `IdentityErrorMapper` for `UserManager.CreateAsync` failures... same mapping `AccountController.Register` already uses, not reinvented"), a failed creation must carry the real `IdentityError` collection out to the controller, not a flattened string the controller can't map per-field. Add a small exception type for that, in the same file (this codebase's existing convention — see `ChatController.cs`, which defines its request DTOs in the same file as the controller that uses them, not a separate one):

```csharp
using Microsoft.AspNetCore.Identity;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Services;

// Carries UserManager.CreateAsync's real IdentityError collection out of
// CreateAdminAsync, so the caller (AdminController) can map each error to
// the right form field via IdentityErrorMapper — the same way
// AccountController.Register already does for public registration.
public class IdentityCreationFailedException(IEnumerable<IdentityError> errors) : Exception
{
    public IReadOnlyList<IdentityError> Errors { get; } = errors.ToList();
}

public class UserManagementService(UserManager<User> userManager) : IUserManagementService
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
                u.AccountStatus.ToString(),
                u.IsSuperAdmin))
            .ToList();

        return Task.FromResult<IEnumerable<UserListDto>>(users);
    }

    public async Task<bool> SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId)
    {
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

        if (status == AccountStatus.Suspended)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        return true;
    }

    public async Task<AdminAccountResultDto> CreateAdminAsync(
        string fullName, string email, bool asSuperAdmin, int actingSuperAdminId)
    {
        var user = new User
        {
            FullName = fullName,
            UserName = email,
            Email = email,
            Role = UserRole.Admin,
            IsSuperAdmin = asSuperAdmin,
            AccountStatus = AccountStatus.Active,
            PreferredLanguage = "bn",
            CreatedByAdminId = actingSuperAdminId,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true,
        };

        var randomPassword = GenerateRandomPassword();
        var result = await userManager.CreateAsync(user, randomPassword);
        if (!result.Succeeded)
        {
            throw new IdentityCreationFailedException(result.Errors);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        // Caller (AdminController) builds the full URL via Url.Action, the same
        // way AccountController.ForgotPassword does — this service returns the
        // token itself; wiring it into a full path is a web-layer concern.
        return new AdminAccountResultDto(user, token);
    }

    public async Task<bool> SetAdminStatusAsync(int targetAdminId, AccountStatus status, int actingSuperAdminId)
    {
        var target = await userManager.FindByIdAsync(targetAdminId.ToString());
        if (target is null || target.IsSuperAdmin)
        {
            return false;
        }

        target.AccountStatus = status;
        var result = await userManager.UpdateAsync(target);
        if (!result.Succeeded)
        {
            return false;
        }

        if (status == AccountStatus.Suspended)
        {
            await userManager.UpdateSecurityStampAsync(target);
        }

        return true;
    }

    public async Task<bool> PromoteToSuperAdminAsync(int targetAdminId, int actingSuperAdminId)
    {
        var target = await userManager.FindByIdAsync(targetAdminId.ToString());
        if (target is null || target.IsSuperAdmin)
        {
            return false;
        }

        target.IsSuperAdmin = true;
        var result = await userManager.UpdateAsync(target);
        return result.Succeeded;
    }

    private static string GenerateRandomPassword()
    {
        // Satisfies Program.cs's Identity password policy (digit, upper, lower,
        // non-alphanumeric, length >= 8) — the account owner resets it via the
        // link before ever using it, so this value itself is never communicated.
        return $"Tmp{Guid.NewGuid():N}!A1".Substring(0, 20);
    }
}
```

Note on `SetAdminStatusAsync`'s guard: `target.IsSuperAdmin` covers both "target is some other SuperAdmin" and "target is the acting SuperAdmin themself" in one check — a SuperAdmin's own row always has `IsSuperAdmin == true`, so self-targeting is already blocked without a separate `target.Id == actingSuperAdminId` comparison. This is deliberately simpler than `SetAccountStatusAsync`'s existing two-part guard (`Role == Admin || Id == actingAdminId`), because the invariant here is stronger: *no* SuperAdmin is ever a valid target, not just "not this one."

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter "UserManagementServiceTests"`
Expected: PASS (7 tests).

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build MuktoAin.slnx && dotnet test`
Expected: Build succeeds (this also resolves Task 3's expected-red build); all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/MuktoAin.Application/DTOs/UserListDto.cs src/MuktoAin.Application/DTOs/AdminAccountResultDto.cs src/MuktoAin.Application/Services/IUserManagementService.cs src/MuktoAin.Application/Services/UserManagementService.cs tests/MuktoAin.UnitTests/Services/UserManagementServiceTests.cs
git commit -m "feat(admin-tiers): add CreateAdminAsync/SetAdminStatusAsync/PromoteToSuperAdminAsync"
```

---

## Task 5: Bootstrap — seeded admin becomes SuperAdmin

**Files:**
- Modify: `src/MuktoAin.Infrastructure/Data/Seeding/SeedAdminUser.cs`
- Test: `tests/MuktoAin.UnitTests/` — check for an existing `SeedAdminUserTests.cs` first (none was found during planning's directory sweep; create one under `tests/MuktoAin.UnitTests/Seeding/`)

**Interfaces:**
- Produces: the seeded admin has `IsSuperAdmin == true` — this is the only way a SuperAdmin can ever come to exist without an existing SuperAdmin creating one, so getting this wrong locks the feature out entirely.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Data.Seeding;
using Xunit;

namespace MuktoAin.UnitTests.Seeding;

public class SeedAdminUserTests
{
    [Fact]
    public async Task SeedAsync_NewAdmin_IsSuperAdmin()
    {
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<ILogger<UserManager<User>>>());
        userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        User? created = null;
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, p) => created = u)
            .ReturnsAsync(IdentityResult.Success);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = "admin@muktoain.bd",
            ["SeedAdmin:Password"] = "Admin@123!",
        }).Build();

        await SeedAdminUser.SeedAsync(userManager.Object, config, Mock.Of<ILogger>());

        Assert.NotNull(created);
        Assert.True(created!.IsSuperAdmin);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SeedAdminUserTests`
Expected: FAIL — `created.IsSuperAdmin` is `false` (the seeder doesn't set it yet).

- [ ] **Step 3: Set it in the seeder**

In `src/MuktoAin.Infrastructure/Data/Seeding/SeedAdminUser.cs`, inside the `admin` object literal, add `IsSuperAdmin = true,` alongside the existing `Role = UserRole.Admin,` line.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SeedAdminUserTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MuktoAin.Infrastructure/Data/Seeding/SeedAdminUser.cs tests/MuktoAin.UnitTests/Seeding/SeedAdminUserTests.cs
git commit -m "feat(admin-tiers): seeded bootstrap admin is a SuperAdmin"
```

---

## Task 6: `AdminController` — CreateAdmin, SuspendAdmin, PromoteAdmin, and the financial-action gate

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs`
- Create: `src/MuktoAin.Web/ViewModels/CreateAdminViewModel.cs`
- Modify: `tests/MuktoAin.UnitTests/Controllers/AdminControllerTests.cs`

**Interfaces:**
- Consumes: `IUserManagementService.CreateAdminAsync/SetAdminStatusAsync/PromoteToSuperAdminAsync` (Task 4) — `AdminController` already injects `IUserManagementService _userManagement`, no new constructor dependency needed.

- [ ] **Step 1: Extend the declarative authorization test**

`AdminControllerTests.cs` today only asserts the class-level `[Authorize(Roles = "Admin")]`. Following that exact same reflection-based approach (this controller's constructor is too heavy to instantiate directly, per that file's own comment), add:

```csharp
[Theory]
[InlineData(nameof(AdminController.CreateAdmin))]
[InlineData(nameof(AdminController.SuspendAdmin))]
[InlineData(nameof(AdminController.PromoteAdmin))]
[InlineData(nameof(AdminController.RefundOrder))]
[InlineData(nameof(AdminController.ApprovePayout))]
[InlineData(nameof(AdminController.MarkOrderPaid))]
public void Action_IsGatedBySuperAdminOnlyPolicy(string actionName)
{
    var methods = typeof(AdminController).GetMethods()
        .Where(m => m.Name == actionName)
        .ToList();

    Assert.NotEmpty(methods);
    Assert.All(methods, m =>
    {
        var attr = m.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault(a => a.Policy == "SuperAdminOnly");
        Assert.NotNull(attr);
    });
}
```

(`CreateAdmin` has both a GET and a POST overload — `GetMethods().Where(...)` deliberately returns both so `Assert.All` checks each; a single `Assert.Single(...)` would fail against the intentional two-overload shape.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter Action_IsGatedBySuperAdminOnlyPolicy`
Expected: FAIL to compile — `CreateAdmin`/`SuspendAdmin`/`PromoteAdmin` don't exist yet on `AdminController`.

- [ ] **Step 3: Write the ViewModel**

```csharp
using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class CreateAdminViewModel
{
    [Required(ErrorMessage = "পূর্ণ নাম প্রয়োজন / Full Name is required")]
    [Display(Name = "পূর্ণ নাম / Full Name")]
    [StringLength(150, ErrorMessage = "সর্বোচ্চ ১৫০ অক্ষর / Maximum 150 characters")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ইমেইল প্রয়োজন / Email is required")]
    [EmailAddress(ErrorMessage = "সঠিক ইমেইল দিন / Please enter a valid email")]
    [Display(Name = "ইমেইল / Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "সুপার অ্যাডমিন / SuperAdmin")]
    public bool AsSuperAdmin { get; set; }
}
```

- [ ] **Step 4: Add the actions to `AdminController`**

```csharp
[HttpGet]
[Authorize(Policy = "SuperAdminOnly")]
public IActionResult CreateAdmin() => View(new CreateAdminViewModel());

[HttpPost]
[Authorize(Policy = "SuperAdminOnly")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CreateAdmin(CreateAdminViewModel model)
{
    if (!ModelState.IsValid)
    {
        return View(model);
    }

    var actingSuperAdminId = int.TryParse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    AdminAccountResultDto result;
    try
    {
        result = await _userManagement.CreateAdminAsync(
            model.FullName, model.Email, model.AsSuperAdmin, actingSuperAdminId);
    }
    catch (IdentityCreationFailedException ex)
    {
        // Same per-field mapping AccountController.Register already uses for
        // the identical UserManager.CreateAsync failure shape.
        foreach (var error in ex.Errors)
        {
            var (field, message) = IdentityErrorMapper.Map(error);
            ModelState.AddModelError(field ?? string.Empty, message);
        }
        return View(model);
    }

    var resetUrl = Url.Action("ResetPassword", "Account",
        new { email = model.Email, token = result.PasswordResetUrl }, Request.Scheme);

    TempData["Success"] = "নতুন অ্যাডমিন তৈরি হয়েছে — রিসেট লিংকটি নিরাপদে পাঠান।";
    TempData["SuccessEn"] = "New admin created — relay the reset link securely.";
    TempData["Info"] = resetUrl;
    return RedirectToAction(nameof(Users));
}

[HttpPost]
[Authorize(Policy = "SuperAdminOnly")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SuspendAdmin(int userId, bool suspend)
{
    var actingSuperAdminId = int.TryParse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    var ok = await _userManagement.SetAdminStatusAsync(
        userId, suspend ? AccountStatus.Suspended : AccountStatus.Active, actingSuperAdminId);

    if (!ok)
    {
        TempData["Error"] = "এই অ্যাডমিনের অবস্থা পরিবর্তন করা যাবে না (SuperAdmin সুরক্ষিত)।";
        TempData["ErrorEn"] = "This admin's status cannot be changed (SuperAdmin protected).";
    }
    else
    {
        TempData["Success"] = suspend ? "অ্যাডমিন স্থগিত হয়েছে।" : "অ্যাডমিন পুনরায় চালু হয়েছে।";
        TempData["SuccessEn"] = suspend ? "Admin suspended." : "Admin reactivated.";
    }
    return RedirectToAction(nameof(Users));
}

[HttpPost]
[Authorize(Policy = "SuperAdminOnly")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> PromoteAdmin(int userId)
{
    var actingSuperAdminId = int.TryParse(
        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
    var ok = await _userManagement.PromoteToSuperAdminAsync(userId, actingSuperAdminId);

    TempData[ok ? "Success" : "Error"] = ok
        ? "অ্যাডমিনকে সুপার অ্যাডমিনে উন্নীত করা হয়েছে।"
        : "উন্নীত করা যায়নি (ইতিমধ্যে সুপার অ্যাডমিন)।";
    TempData[ok ? "SuccessEn" : "ErrorEn"] = ok
        ? "Admin promoted to SuperAdmin."
        : "Could not promote (already a SuperAdmin).";
    return RedirectToAction(nameof(Users));
}
```

Add `[Authorize(Policy = "SuperAdminOnly")]` to the existing `RefundOrder`, `ApprovePayout`, and `MarkOrderPaid` actions (alongside their existing `[HttpPost]`/`[ValidateAntiForgeryToken]`), and add `using MuktoAin.Application.DTOs;` to the file's usings if not already present (it likely already has it via other DTO usage in this large controller — check before adding a duplicate).

- [ ] **Step 5: Run to verify tests pass**

Run: `dotnet test --filter AdminControllerTests`
Expected: PASS (7 tests: the original class-level test plus the new 6-case theory).

- [ ] **Step 6: Build**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/MuktoAin.Web/Controllers/AdminController.cs src/MuktoAin.Web/ViewModels/CreateAdminViewModel.cs tests/MuktoAin.UnitTests/Controllers/AdminControllerTests.cs
git commit -m "feat(admin-tiers): add CreateAdmin/SuspendAdmin/PromoteAdmin, gate financial actions"
```

---

## Task 7: Views — "Manage Admins" section and the Create Admin form

**Files:**
- Modify: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (the `Users` action's mapping)
- Modify: `src/MuktoAin.Web/Views/Admin/Users.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/CreateAdmin.cshtml`

**Interfaces:**
- Consumes: `UserListDto.IsSuperAdmin` (Task 3), `AdminController.CreateAdmin/SuspendAdmin/PromoteAdmin` (Task 6).

- [ ] **Step 1: Extend `AdminUserRowViewModel`**

```csharp
public class AdminUserRowViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsSuperAdmin { get; set; }
}
```

- [ ] **Step 2: Update the mapping in `AdminController.Users`**

In the existing `Users(string? role)` action, the `.Select(u => new AdminUserRowViewModel { ... })` block gains one line:

```csharp
Users = filtered.Select(u => new AdminUserRowViewModel
{
    UserId = u.UserId,
    FullName = u.FullName,
    Email = u.Email,
    Role = u.Role,
    Status = u.Status,
    IsSuperAdmin = u.IsSuperAdmin
}).ToList()
```

Also pass whether the current viewer is a SuperAdmin into the view, so it can decide whether to render the Manage-Admins controls at all (a regular Admin should see the same Users list they see today, unchanged — the new controls are additive, not something that leaks into their view and 403s on click). Add to `AdminUsersViewModel`:

```csharp
public class AdminUsersViewModel
{
    public List<AdminUserRowViewModel> Users { get; set; } = new();
    public string RoleFilter { get; set; } = "All";
    public bool ViewerIsSuperAdmin { get; set; }
}
```

and in the action, set `ViewerIsSuperAdmin = User.HasClaim("IsSuperAdmin", "true")` on the constructed `vm`.

- [ ] **Step 3: Update `Users.cshtml`**

Replace the existing `<td>` actions cell logic:

```cshtml
<td>
    @if (u.Role == "Admin")
    {
        if (u.IsSuperAdmin)
        {
            <span class="badge badge-neutral" title="SuperAdmin — Protected">SuperAdmin</span>
        }
        else if (Model.ViewerIsSuperAdmin)
        {
            <div style="display:flex; gap:6px; flex-wrap:wrap;">
                @if (u.Status == "Suspended")
                {
                    <form asp-action="SuspendAdmin" method="post" style="display:inline">
                        @Html.AntiForgeryToken()
                        <input type="hidden" name="userId" value="@u.UserId" />
                        <input type="hidden" name="suspend" value="false" />
                        <button class="btn btn-outline btn-sm" type="submit">Activate</button>
                    </form>
                }
                else
                {
                    <form asp-action="SuspendAdmin" method="post" style="display:inline"
                          data-confirm="Suspend this admin? Login will be blocked.">
                        @Html.AntiForgeryToken()
                        <input type="hidden" name="userId" value="@u.UserId" />
                        <input type="hidden" name="suspend" value="true" />
                        <button class="btn btn-danger-outline btn-sm" type="submit">Suspend</button>
                    </form>
                }
                <form asp-action="PromoteAdmin" method="post" style="display:inline"
                      data-confirm="Promote this admin to SuperAdmin? This cannot be undone through the app.">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="userId" value="@u.UserId" />
                    <button class="btn btn-outline btn-sm" type="submit">Promote</button>
                </form>
            </div>
        }
        else
        {
            <span class="badge badge-neutral" title="Protected">Protected</span>
        }
    }
    else if (u.Status == "Suspended")
    {
        <form asp-action="Suspend" method="post" style="display:inline">
            @Html.AntiForgeryToken()
            <input type="hidden" name="userId" value="@u.UserId" />
            <input type="hidden" name="suspend" value="false" />
            <button class="btn btn-outline btn-sm" type="submit">Activate</button>
        </form>
    }
    else
    {
        <form asp-action="Suspend" method="post" style="display:inline"
              data-confirm="Suspend this account? Login will be blocked.">
            @Html.AntiForgeryToken()
            <input type="hidden" name="userId" value="@u.UserId" />
            <input type="hidden" name="suspend" value="true" />
            <button class="btn btn-danger-outline btn-sm" type="submit">Suspend</button>
        </form>
    }
</td>
```

And add a "Create Admin" button near the page head, visible only to a SuperAdmin viewer — inside the existing `<div class="page-head">`, after the `<p class="page-sub">` line:

```cshtml
@if (Model.ViewerIsSuperAdmin)
{
    <a asp-action="CreateAdmin" class="btn btn-primary btn-sm">+ Create Admin</a>
}
```

- [ ] **Step 4: Write `CreateAdmin.cshtml`**

```cshtml
@model MuktoAin.Web.ViewModels.CreateAdminViewModel
@{
    ViewData["Title"] = "Create Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main" style="max-width: 560px;">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <a asp-controller="Admin" asp-action="Users">Users</a>
        <span class="sep">/</span>
        <span>Create Admin</span>
    </nav>

    <div class="page-head">
        <h1 class="page-title">Create Admin</h1>
        <p class="page-sub">SuperAdmin only. A random password is generated and a reset link is shown below — relay it to the new admin out-of-band.</p>
    </div>

    <div class="card" style="padding: 20px;">
        <form asp-action="CreateAdmin" method="post">
            @Html.AntiForgeryToken()
            @* All, not ModelOnly: IdentityErrorMapper can map a password-policy
               failure to a "Password" field, but this form has no Password
               input (the system generates one) — ModelOnly would only show
               string.Empty-keyed errors and silently drop that one. *@
            <div asp-validation-summary="All" class="text-danger"></div>

            <div class="form-group">
                <label asp-for="FullName"></label>
                <input asp-for="FullName" class="form-control" />
                <span asp-validation-for="FullName" class="text-danger"></span>
            </div>

            <div class="form-group">
                <label asp-for="Email"></label>
                <input asp-for="Email" class="form-control" />
                <span asp-validation-for="Email" class="text-danger"></span>
            </div>

            <div class="form-group" style="margin-top: 12px;">
                <label>
                    <input asp-for="AsSuperAdmin" type="checkbox" />
                    <span asp-for="AsSuperAdmin"></span>
                </label>
            </div>

            <button type="submit" class="btn btn-primary" style="margin-top: 16px;">Create Admin</button>
        </form>
    </div>
</main>
```

- [ ] **Step 5: Build and manual check**

Run: `dotnet build MuktoAin.slnx`
Expected: Build succeeded, 0 errors.

Run the app, log in as the seeded admin (`admin@muktoain.bd` / configured password — now a SuperAdmin per Task 5), visit `/Admin/Users`, confirm the "+ Create Admin" button appears and the SuperAdmin's own row shows the `SuperAdmin` badge with no action buttons. Create a second admin, confirm it appears with `Protected`-style Suspend/Promote controls, and that suspending/promoting it works.

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs src/MuktoAin.Web/Controllers/AdminController.cs src/MuktoAin.Web/Views/Admin/Users.cshtml src/MuktoAin.Web/Views/Admin/CreateAdmin.cshtml
git commit -m "feat(admin-tiers): add Manage Admins UI"
```

---

## Task 8: Update `plans/Dependency_plan.md`

**Files:**
- Modify: `plans/Dependency_plan.md`

- [ ] **Step 1: Add the entry** (next unused `R-N` after Notifications' entry, if that plan landed first, else after the highest currently in the file), summarizing: `User.IsSuperAdmin`, the `SuperAdminOnly` policy, `CreateAdmin`/`SuspendAdmin`/`PromoteAdmin`, the financial-action gate on Refund/ApprovePayout/MarkOrderPaid, and the mutual-immutability guard rail.

- [ ] **Step 2: Commit**

```bash
git add plans/Dependency_plan.md
git commit -m "docs: record admin tiers in Dependency_plan.md"
```
