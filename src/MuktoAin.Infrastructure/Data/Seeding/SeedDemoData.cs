using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Dev-only demo data: a handful of citizens, lawyers and cases so the app has
// something to click through end-to-end (Track, Lawyer Queue, Admin Dashboard)
// without submitting everything by hand. Called from Program.cs only when
// app.Environment.IsDevelopment() -- never runs against a real deployment.
//
// Idempotent: short-circuits if any of the demo accounts already exist, so it's
// safe to leave in the startup path across repeated `dotnet run`s.
//
// Depends on SeedDistricts / SeedCategories / SeedAdminUser having already run
// (Program.cs seeding order) -- categories/districts are looked up here, not
// hardcoded, since CASE_CATEGORY ids are IDENTITY-assigned.
public static class SeedDemoData
{
    private const string DemoPassword = "Demo!2026";

    public static async Task SeedAsync(
        AppDbContext context,
        UserManager<User> userManager,
        IEncryptionService encryptionService,
        ILogger logger)
    {
        const string firstCitizenEmail = "citizen1@demo.muktoain.bd";
        if (await userManager.FindByEmailAsync(firstCitizenEmail) is not null)
        {
            return; // already seeded
        }

        var categories = await context.CaseCategories.OrderBy(c => c.CategoryId).ToListAsync();
        var districts = await context.Districts.OrderBy(d => d.DistrictId).ToListAsync();
        if (categories.Count == 0 || districts.Count == 0)
        {
            logger.LogWarning("SeedDemoData: no categories/districts found -- run SeedCategories/SeedDistricts first. Skipping.");
            return;
        }

        District DistrictByName(string name) =>
            districts.FirstOrDefault(d => d.Name == name) ?? districts[0];

        var dhaka = DistrictByName("Dhaka");
        var chattogram = DistrictByName("Chattogram");

        // --- Citizens -------------------------------------------------------
        var citizens = new List<User>();
        foreach (var (fullName, email) in new[]
        {
            ("Farhana Akter", "citizen1@demo.muktoain.bd"),
            ("Kamal Hossain", "citizen2@demo.muktoain.bd"),
            ("Nusrat Jahan", "citizen3@demo.muktoain.bd"),
        })
        {
            citizens.Add(await CreateUserAsync(userManager, logger, fullName, email, UserRole.Citizen));
        }

        // --- Lawyers ----------------------------------------------------------
        var verifiedLawyerUser = await CreateUserAsync(
            userManager, logger, "Barrister Rafiq Islam", "lawyer1@demo.muktoain.bd", UserRole.Lawyer);
        var pendingLawyerUser = await CreateUserAsync(
            userManager, logger, "Advocate Shirin Sultana", "lawyer2@demo.muktoain.bd", UserRole.Lawyer);

        var verifiedLawyerProfile = new LawyerProfile
        {
            UserId = verifiedLawyerUser.Id,
            BarRegistrationNumber = "DBA-2019-04521",
            VerificationStatus = VerificationStatus.Approved,
            Specialization = "Criminal Law, Family Law",
            VerifiedAt = DateTime.UtcNow,
        };
        var pendingLawyerProfile = new LawyerProfile
        {
            UserId = pendingLawyerUser.Id,
            BarRegistrationNumber = "DBA-2022-01187",
            VerificationStatus = VerificationStatus.Pending,
            Specialization = "Consumer Rights",
        };
        context.LawyerProfiles.AddRange(verifiedLawyerProfile, pendingLawyerProfile);
        await context.SaveChangesAsync();

        // --- Cases ------------------------------------------------------------
        CaseCategory CategoryByIndex(int i) => categories[i % categories.Count];

        var now = DateTime.UtcNow;
        var demoCases = new List<Case>
        {
            NewCase(citizens[0].Id, CategoryByIndex(0), dhaka,
                "মোবাইল চুরির অভিযোগ", "গতকাল রাতে বাসস্ট্যান্ডে আমার মোবাইল ফোনটি ছিনতাই হয়েছে।",
                CaseStatus.Submitted, now.AddDays(-1)),
            NewCase(citizens[0].Id, CategoryByIndex(1), dhaka,
                "বাড়িওয়ালার জামানত ফেরত না দেওয়া", "বাসা ছাড়ার পরও বাড়িওয়ালা জামানতের টাকা ফেরত দিচ্ছেন না।",
                CaseStatus.UnderReview, now.AddDays(-4)),
            NewCase(citizens[1].Id, CategoryByIndex(2), chattogram,
                "বেতন পরিশোধে অস্বীকৃতি", "চাকরি ছাড়ার পর তিন মাসের বকেয়া বেতন পরিশোধ করা হয়নি।",
                CaseStatus.UnderReview, now.AddDays(-6)),
            NewCase(citizens[1].Id, CategoryByIndex(3), chattogram,
                "ত্রুটিপূর্ণ পণ্য ফেরত না নেওয়া", "কেনার সাত দিনের মধ্যেই পণ্যটি নষ্ট হয়ে গেছে, দোকানদার ফেরত নিচ্ছেন না।",
                CaseStatus.Finalized, now.AddDays(-10)),
            NewCase(citizens[2].Id, CategoryByIndex(0), dhaka,
                "প্রতিবেশীর সাথে জমি বিরোধ", "সীমানা প্রাচীর নিয়ে প্রতিবেশীর সাথে দীর্ঘদিনের বিরোধ চলছে।",
                CaseStatus.Submitted, now.AddDays(-2)),
            NewCase(null, CategoryByIndex(1), dhaka,
                "নাম প্রকাশে অনিচ্ছুক অভিযোগকারী", "পারিবারিক সহিংসতার শিকার, পরিচয় গোপন রেখে সহায়তা চাই।",
                CaseStatus.UnderReview, now.AddDays(-3), isAnonymous: true),
        };

        foreach (var c in demoCases)
        {
            c.Title = encryptionService.Encrypt(c.Title);
            c.Description = encryptionService.Encrypt(c.Description);
        }
        context.Cases.AddRange(demoCases);
        await context.SaveChangesAsync();

        // --- Generated documents + lawyer reviews for the two most-progressed cases ---
        var underReviewCase = demoCases[2]; // salary complaint, UnderReview
        var finalizedCase = demoCases[3];   // consumer complaint, Finalized

        var underReviewDoc = new GeneratedDocument
        {
            CaseId = underReviewCase.CaseId,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "খসড়া: শ্রম আইন অনুযায়ী বকেয়া বেতন দাবি সংক্রান্ত অভিযোগপত্র।",
            Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = verifiedLawyerProfile.LawyerProfileId,
            CreatedAt = now.AddDays(-5),
        };

        var finalizedDoc = new GeneratedDocument
        {
            CaseId = finalizedCase.CaseId,
            DocumentType = DocumentType.ConsumerComplaint,
            ContentDraft = "খসড়া: ভোক্তা অধিকার সংরক্ষণ আইন অনুযায়ী অভিযোগপত্র।",
            ContentFinal = "চূড়ান্ত: ভোক্তা অধিকার সংরক্ষণ আইন অনুযায়ী অভিযোগপত্র (আইনজীবী কর্তৃক পর্যালোচিত)।",
            Status = DocumentStatus.Approved,
            AssignedLawyerProfileId = verifiedLawyerProfile.LawyerProfileId,
            CreatedAt = now.AddDays(-9),
        };

        context.GeneratedDocuments.AddRange(underReviewDoc, finalizedDoc);
        await context.SaveChangesAsync();

        context.LawyerReviews.Add(new LawyerReview
        {
            DocumentId = finalizedDoc.DocumentId,
            LawyerProfileId = verifiedLawyerProfile.LawyerProfileId,
            Decision = ReviewDecision.EditedApproved,
            Comments = "ভাষা কিছুটা সংশোধন করে অনুমোদন করা হলো।",
            ReviewedAt = now.AddDays(-8),
        });
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Seeded demo data: {Citizens} citizens, 2 lawyers (1 verified), {Cases} cases, 2 documents, 1 review. Demo password for all seeded accounts: {Password}",
            citizens.Count, demoCases.Count, DemoPassword);
    }

    private static Case NewCase(
        int? userId,
        CaseCategory category,
        District district,
        string title,
        string description,
        CaseStatus status,
        DateTime createdAt,
        bool isAnonymous = false) => new()
        {
            UserId = userId,
            CategoryId = category.CategoryId,
            DistrictId = district.DistrictId,
            Title = title,
            Description = description,
            Language = "bn",
            Status = status,
            IsAnonymous = isAnonymous,
            AnonymousTrackingCode = isAnonymous ? Guid.NewGuid().ToString("N") : null,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    private static async Task<User> CreateUserAsync(
        UserManager<User> userManager,
        ILogger logger,
        string fullName,
        string email,
        UserRole role)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return existing;

        var user = new User
        {
            FullName = fullName,
            UserName = email,
            Email = email,
            Role = role,
            AccountStatus = AccountStatus.Active,
            PreferredLanguage = "bn",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            logger.LogError("SeedDemoData: failed to create {Email}: {Errors}", email, errors);
            throw new InvalidOperationException($"Demo user seeding failed for '{email}'.");
        }

        return user;
    }
}
