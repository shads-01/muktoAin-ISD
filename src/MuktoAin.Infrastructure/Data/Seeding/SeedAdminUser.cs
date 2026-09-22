using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Infrastructure.Data.Seeding;

// S-1.2: idempotent startup seeding of the first admin account.
// "admins can only be created by admins" (design.md 2.1) -- this seeder is the
// sole exception: it bootstraps the very first admin so later admins can be
// created through the admin console (S-3.6 / FR-18).
//
// Credentials come from configuration ("SeedAdmin" section) so production does
// not run on hardcoded defaults. appsettings.Development.json.template shows the
// shape; CHANGE THE PASSWORD before any real deployment.
public static class SeedAdminUser
{
    // Bootstrap defaults used only when SeedAdmin:Email / SeedAdmin:Password
    // aren't configured. Views/Account/Login.cshtml's "Quick Demo Fill" admin
    // button references these same constants (mirroring SeedDemoUsers'
    // CitizenEmail/CitizenPassword pattern) so the demo button can never drift
    // out of sync with the actual bootstrap password again.
    public const string DefaultEmail = "admin@muktoain.bd";
    public const string DefaultPassword = "Admin@123!";

    public static async Task SeedAsync(
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration["SeedAdmin:Email"] ?? DefaultEmail;
        var password = configuration["SeedAdmin:Password"] ?? DefaultPassword;

        if (string.IsNullOrWhiteSpace(configuration["SeedAdmin:Password"]))
        {
            logger.LogWarning(
                "SeedAdmin:Password not configured -- using default bootstrap password. " +
                "Set 'SeedAdmin__Password' via environment/secret before any real deployment.");
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (!await userManager.CheckPasswordAsync(existing, password))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(existing);
                var resetResult = await userManager.ResetPasswordAsync(existing, token, password);
                if (resetResult.Succeeded)
                {
                    logger.LogInformation("Synchronized password for admin user {Email}.", email);
                }
                else
                {
                    var errors = string.Join("; ", resetResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    logger.LogWarning("Failed to synchronize password for admin user {Email}: {Errors}", email, errors);
                }
            }

            // Admins seeded before scripts/12_add_user_issuperadmin.sql got the
            // column default (0), leaving no SuperAdmin to refund, approve payouts,
            // or promote anyone. The bootstrap admin is always the SuperAdmin.
            if (existing.Role == UserRole.Admin && !existing.IsSuperAdmin)
            {
                existing.IsSuperAdmin = true;
                var promoteResult = await userManager.UpdateAsync(existing);
                if (promoteResult.Succeeded)
                {
                    logger.LogInformation("Promoted bootstrap admin {Email} to SuperAdmin.", email);
                }
                else
                {
                    var errors = string.Join("; ", promoteResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    logger.LogWarning("Failed to promote bootstrap admin {Email} to SuperAdmin: {Errors}", email, errors);
                }
            }
            return;
        }

        var admin = new User
        {
            FullName = "System Administrator",
            UserName = email,
            Email = email,
            Role = UserRole.Admin,
            IsSuperAdmin = true,
            AccountStatus = AccountStatus.Active,
            PreferredLanguage = "bn",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            logger.LogError("Failed to seed admin user {Email}: {Errors}", email, errors);
            throw new InvalidOperationException($"Admin user seeding failed for '{email}'.");
        }

        // No Identity role tables exist (see AppDbContext note) -- UserRole.Admin
        // enum column + UserRoleClaimsTransformation carry authorization instead.
        logger.LogInformation("Seeded initial admin user {Email}.", email);
    }
}
