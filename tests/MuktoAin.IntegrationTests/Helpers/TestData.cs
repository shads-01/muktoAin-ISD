using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Helpers;

public static class TestData
{
    public const int DhakaDistrictId = 1;

    public static async Task<(District District, CaseCategory Category)> SeedBaselineAsync(AppDbContext db)
    {
        var district = await db.Districts.FindAsync((byte)DhakaDistrictId);
        if (district == null)
        {
            district = new District { DistrictId = (byte)DhakaDistrictId, Name = "Dhaka" };
            db.Districts.Add(district);
        }

        var category = await db.CaseCategories.FindAsync(1);
        if (category == null)
        {
            category = new CaseCategory { CategoryId = 1, Name = "Labour", NameBn = "শ্রম", Description = "Labour complaints" };
            db.CaseCategories.Add(category);
        }

        await db.SaveChangesAsync();
        await SeedActAsync(db);
        return (district, category);
    }

    public static async Task<Act> SeedActAsync(AppDbContext db)
    {
        var act = await db.Acts.FindAsync(1);
        if (act == null)
        {
            act = new Act { ActId = 1, Title = "Bangladesh Labour Act, 2006", ActNumber = "XLII", Year = 2006 };
            db.Acts.Add(act);
        }

        var section = await db.ActSections.FindAsync(1);
        if (section == null)
        {
            section = new ActSection
            {
                SectionId = 1,
                ActId = 1,
                Act = act,
                SectionNumber = "123",
                SectionText = "The wages of every worker shall be paid before the expiry of the seventh working day.",
            };
            db.ActSections.Add(section);
        }
        await db.SaveChangesAsync();
        return act;
    }

    public static async Task<User> NewUserAsync(AppDbContext db, string fullName, UserRole role)
    {
        var user = new User
        {
            UserName = $"{fullName}-{Guid.NewGuid():N}"[..24],
            Email = $"{Guid.NewGuid():N}@test.muktoain.bd",
            NormalizedUserName = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            NormalizedEmail = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = fullName,
            Role = role,
            AccountStatus = AccountStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<Case> NewCaseAsync(
        AppDbContext db, int? userId, byte districtId = (byte)DhakaDistrictId,
        int categoryId = 1, bool anonymous = false, string? trackingCode = null)
    {
        var c = new Case
        {
            UserId = anonymous ? null : userId,
            CategoryId = categoryId,
            DistrictId = districtId,
            Title = "৩ মাসের বকেয়া বেতন",
            Description = "Employer has not paid wages for three months.",
            Language = "en", // English templates keep existing tests independent of Task 5's A-3.9 routing
            Status = CaseStatus.Submitted,
            IsAnonymous = anonymous,
            AnonymousTrackingCode = trackingCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Cases.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public static GeneratedDocument NewDocument(int caseId, DocumentStatus status, string contentDraft = "Draft body text") =>
        new()
        {
            CaseId = caseId,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = contentDraft,
            Status = status,
            VersionNo = 1,
            CreatedAt = DateTime.UtcNow,
        };
}
