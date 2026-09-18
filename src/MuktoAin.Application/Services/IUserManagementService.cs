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
