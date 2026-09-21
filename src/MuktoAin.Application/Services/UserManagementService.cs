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

        await audit.LogAdminActionAsync(
            actingAdminId,
            status == AccountStatus.Suspended ? "SuspendUser" : "UnsuspendUser",
            targetUserId: userId,
            details: $"Status changed to {status}");

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
        return $"Tmp!1A{Guid.NewGuid():N}"[..20];
    }
}
