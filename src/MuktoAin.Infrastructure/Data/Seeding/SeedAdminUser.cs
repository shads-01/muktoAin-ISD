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
    public static async Task SeedAsync(
        UserManager<User> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration["SeedAdmin:Email"] ?? "admin@muktoain.bd";
        var password = configuration["SeedAdmin:Password"] ?? "ChangeMe!2026";

        if (string.IsNullOrWhiteSpace(configuration["SeedAdmin:Password"]))
        {
            logger.LogWarning(
                "SeedAdmin:Password not configured -- using default bootstrap password. " +
                "Set 'SeedAdmin__Password' via environment/secret before any real deployment.");
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return;
        }

        var admin = new User
        {
            FullName = "System Administrator",
            UserName = email,
            Email = email,
            Role = UserRole.Admin,
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
