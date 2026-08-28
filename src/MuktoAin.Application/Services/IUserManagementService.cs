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
}
