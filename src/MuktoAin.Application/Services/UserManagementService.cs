using Microsoft.AspNetCore.Identity;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Services;

// S-3.6 / FR-18: admin user management -- list users, suspend/activate accounts.
// Two methods wrapping UserManager (per Shads_plan Step 3.8): Identity handles
// account creation via registration; this covers only the admin-list and
// suspend/activate gap. Ceiling: list + status toggle.
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

        return true;
    }
}
