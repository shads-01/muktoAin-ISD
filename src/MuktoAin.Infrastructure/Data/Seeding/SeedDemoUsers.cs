using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Dev/seed-only supplement to SeedAdminUser: creates the Citizen and Lawyer demo
// accounts promised by Views/Account/Login.cshtml's "Quick Demo Fill" buttons
// (citizen@muktoain.bd / Citizen@123, lawyer@muktoain.bd / Lawyer@123). Same
// idempotent style as SeedAdminUser -- each account is created once and skipped
// on later startups. Program.cs only invokes this in the Development environment,
// so staging/production never expose demo credentials.
//
// The credentials deliberately satisfy the Identity password policy configured in
// Program.cs (RequireDigit/RequireUppercase/RequireLowercase/RequireNonAlphanumeric,
// min 8), so UserManager.CreateAsync(..) succeeds without lowering the bar.
public static class SeedDemoUsers
{
    public const string CitizenEmail = "citizen@muktoain.bd";
    public const string CitizenPassword = "Citizen@123";

    public const string LawyerEmail = "lawyer@muktoain.bd";
    public const string LawyerPassword = "Lawyer@123";

    // Matches the UNIQUE constraint UQ_LAWYER_PROFILE_BarRegistrationNumber.
    public const string DemoBarRegistrationNumber = "DEMO-BAR-2026-0001";

    public static async Task SeedAsync(
        UserManager<User> userManager,
        IRepository<LawyerProfile> lawyerProfileRepo,
        ILogger logger)
    {
        // Idempotence: each demo account is seeded exactly once per environment,
        // mirroring SeedAdminUser's early-return when the login already exists.
        var citizen = await userManager.FindByEmailAsync(CitizenEmail);
        if (citizen is null)
        {
            citizen = new User
            {
                FullName = "ডেমো নাগরিক / Demo Citizen",
                UserName = CitizenEmail,
                Email = CitizenEmail,
                Role = UserRole.Citizen,
                AccountStatus = AccountStatus.Active,
                PreferredLanguage = "bn",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            var citizenResult = await userManager.CreateAsync(citizen, CitizenPassword);
            if (!citizenResult.Succeeded)
            {
                var errors = string.Join("; ", citizenResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                logger.LogError("Failed to seed demo citizen {Email}: {Errors}", CitizenEmail, errors);
                throw new InvalidOperationException($"Demo citizen seeding failed for '{CitizenEmail}'.");
            }

            logger.LogInformation("Seeded demo citizen {Email} (role {Role}).", CitizenEmail, UserRole.Citizen);
        }

        // Seeding the citizen may have failed above and thrown; only reach the
        // lawyer when the citizen branch completed. Recheck the lawyer login
        // independent of citizen state so a restart never duplicates either.
        var lawyer = await userManager.FindByEmailAsync(LawyerEmail);
        if (lawyer is null)
        {
            lawyer = new User
            {
                FullName = "ডেমো আইনজীবী / Demo Lawyer",
                UserName = LawyerEmail,
                Email = LawyerEmail,
                Role = UserRole.Lawyer,
                AccountStatus = AccountStatus.Active,
                PreferredLanguage = "bn",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            var lawyerResult = await userManager.CreateAsync(lawyer, LawyerPassword);
            if (!lawyerResult.Succeeded)
            {
                var errors = string.Join("; ", lawyerResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
                logger.LogError("Failed to seed demo lawyer {Email}: {Errors}", LawyerEmail, errors);
                throw new InvalidOperationException($"Demo lawyer seeding failed for '{LawyerEmail}'.");
            }

            // A demo lawyer must sit behind the same review gate as a real one:
            // verify-by-admin is required before Queue access activates, so the
            // profile is seeded Pending and an admin must approve it (FR gate).
            await lawyerProfileRepo.AddAsync(new LawyerProfile
            {
                UserId = lawyer.Id,
                BarRegistrationNumber = DemoBarRegistrationNumber,
                Specialization = "সাধারণ আইন / General Law",
                VerificationStatus = VerificationStatus.Pending
            });

            await lawyerProfileRepo.SaveChangesAsync();

            logger.LogInformation(
                "Seeded demo lawyer {Email} (role {Role}) with Pending LawyerProfile {BarNumber}.",
                LawyerEmail, UserRole.Lawyer, DemoBarRegistrationNumber);
        }
    }
}