using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Dev-only: gives the Quick Demo Fill citizen (citizen@muktoain.bd) finalized
// cases with a lawyer-approved document and no honorarium paid yet, so the
// "Send Lawyer Honorarium" button on /Case/Result/{id} is there to try the
// payment flow against.
//
// Tops up rather than seeding once: each startup brings the citizen back to
// TargetPayableCases unpaid cases, so a demo that paid them all gets fresh
// ones after a restart.
//
// Runs after SeedDemoData (the honorarium goes to its verified lawyer,
// lawyer1@demo.muktoain.bd) and SeedDemoUsers (the citizen account).
public static class SeedDemoPaymentCases
{
    private const int TargetPayableCases = 3;
    private const string VerifiedLawyerBarNumber = "DBA-2019-04521"; // SeedDemoData's lawyer1

    public static async Task SeedAsync(
        AppDbContext context,
        UserManager<User> userManager,
        IEncryptionService encryptionService,
        ILogger logger)
    {
        var citizen = await userManager.FindByEmailAsync(SeedDemoUsers.CitizenEmail);
        if (citizen is null)
        {
            logger.LogWarning("SeedDemoPaymentCases: demo citizen {Email} not found. Skipping.", SeedDemoUsers.CitizenEmail);
            return;
        }

        var payable = await context.Cases.CountAsync(c =>
            c.UserId == citizen.Id
            && !c.HonorariumPaid
            && c.Documents.Any(d => d.Status == DocumentStatus.Approved));
        var missing = TargetPayableCases - payable;
        if (missing <= 0) return;

        var categories = await context.CaseCategories.OrderBy(c => c.CategoryId).ToListAsync();
        var districts = await context.Districts.OrderBy(d => d.DistrictId).ToListAsync();
        if (categories.Count == 0 || districts.Count == 0)
        {
            logger.LogWarning("SeedDemoPaymentCases: no categories/districts found. Skipping.");
            return;
        }
        var dhaka = districts.FirstOrDefault(d => d.Name == "Dhaka") ?? districts[0];

        // Without an assigned lawyer the order still goes through, but no one
        // receives the net share, so prefer the verified demo lawyer.
        var lawyer = await context.LawyerProfiles.FirstOrDefaultAsync(l => l.BarRegistrationNumber == VerifiedLawyerBarNumber)
                     ?? await context.LawyerProfiles.FirstOrDefaultAsync(l => l.VerificationStatus == VerificationStatus.Approved);

        var templates = new[]
        {
            (Title: "বাড়িওয়ালার জামানত ফেরত না দেওয়া",
             Description: "বাসা ছাড়ার দুই মাস পরও বাড়িওয়ালা ২০,০০০ টাকা জামানত ফেরত দিচ্ছেন না।",
             DocType: DocumentType.GeneralDiary,
             Final: "চূড়ান্ত: জামানতের টাকা ফেরত না দেওয়া সংক্রান্ত সাধারণ ডায়েরি (আইনজীবী কর্তৃক পর্যালোচিত)।"),
            (Title: "বকেয়া বেতন পরিশোধে অস্বীকৃতি",
             Description: "চাকরি ছাড়ার পর নিয়োগকর্তা তিন মাসের বকেয়া বেতন পরিশোধ করছেন না।",
             DocType: DocumentType.LabourComplaint,
             Final: "চূড়ান্ত: বাংলাদেশ শ্রম আইন, ২০০৬ অনুযায়ী বকেয়া বেতন দাবি সংক্রান্ত অভিযোগপত্র (আইনজীবী কর্তৃক পর্যালোচিত)।"),
            (Title: "অনলাইনে কেনা ত্রুটিপূর্ণ পণ্য",
             Description: "অনলাইনে কেনা মোবাইল ফোনটি ত্রুটিপূর্ণ, বিক্রেতা ফেরত বা বদল করে দিচ্ছেন না।",
             DocType: DocumentType.ConsumerComplaint,
             Final: "চূড়ান্ত: ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ অনুযায়ী অভিযোগপত্র (আইনজীবী কর্তৃক পর্যালোচিত)।"),
        };

        var now = DateTime.UtcNow;
        for (var i = 0; i < missing; i++)
        {
            var t = templates[i % templates.Length];
            var createdAt = now.AddDays(-(i + 3));

            var demoCase = new Case
            {
                UserId = citizen.Id,
                CategoryId = categories[i % categories.Count].CategoryId,
                DistrictId = dhaka.DistrictId,
                Title = encryptionService.Encrypt(t.Title),
                Description = encryptionService.Encrypt(t.Description),
                Language = "bn",
                Status = CaseStatus.Finalized,
                CreatedAt = createdAt,
                UpdatedAt = now.AddDays(-1),
            };
            context.Cases.Add(demoCase);
            await context.SaveChangesAsync();

            var doc = new GeneratedDocument
            {
                CaseId = demoCase.CaseId,
                DocumentType = t.DocType,
                ContentDraft = "খসড়া: " + t.Description,
                ContentFinal = t.Final,
                Status = DocumentStatus.Approved,
                AssignedLawyerProfileId = lawyer?.LawyerProfileId,
                ClaimedAt = createdAt.AddDays(1),
                CreatedAt = createdAt,
            };
            context.GeneratedDocuments.Add(doc);
            await context.SaveChangesAsync();

            if (lawyer is not null)
            {
                context.LawyerReviews.Add(new LawyerReview
                {
                    DocumentId = doc.DocumentId,
                    LawyerProfileId = lawyer.LawyerProfileId,
                    Decision = ReviewDecision.Approved,
                    Comments = "খসড়াটি সঠিক আছে, অনুমোদন করা হলো।",
                    ReviewedAt = now.AddDays(-1),
                });
                await context.SaveChangesAsync();
            }
        }

        logger.LogInformation(
            "Seeded {Count} payable demo case(s) for {Email} (lawyer profile {LawyerProfileId}).",
            missing, SeedDemoUsers.CitizenEmail, lawyer?.LawyerProfileId);
    }
}
