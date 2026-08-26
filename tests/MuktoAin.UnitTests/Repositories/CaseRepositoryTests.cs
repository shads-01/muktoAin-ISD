using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

public class CaseRepositoryTests
{
    private static async Task<(District district, CaseCategory category)> SeedLookupsAsync(AppDbContext context)
    {
        var district = new District { DistrictId = 1, Name = "Dhaka" };
        var category = new CaseCategory { Name = "Labour Complaint", Description = "desc" };
        context.Districts.Add(district);
        context.CaseCategories.Add(category);
        await context.SaveChangesAsync();
        return (district, category);
    }

    private static Case NewCase(int? userId, int categoryId, byte districtId) => new()
    {
        UserId = userId,
        CategoryId = categoryId,
        DistrictId = districtId,
        Title = "Title",
        Description = "Description",
        Language = "en",
        Status = CaseStatus.Submitted,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOnlyThatUsersCases()
    {
        using var context = TestDbContextFactory.Create();
        var (district, category) = await SeedLookupsAsync(context);
        context.Cases.AddRange(
            NewCase(1, category.CategoryId, district.DistrictId),
            NewCase(1, category.CategoryId, district.DistrictId),
            NewCase(2, category.CategoryId, district.DistrictId));
        await context.SaveChangesAsync();

        var repo = new CaseRepository(context);
        var result = await repo.GetByUserIdAsync(1);

        Assert.Equal(2, result.Count());
        Assert.All(result, c => Assert.Equal(1, c.UserId));
    }

    [Fact]
    public async Task GetWithDocumentsAsync_ReturnsCaseWithLoadedDocuments()
    {
        using var context = TestDbContextFactory.Create();
        var (district, category) = await SeedLookupsAsync(context);
        var kase = NewCase(1, category.CategoryId, district.DistrictId);
        kase.Documents.Add(new GeneratedDocument
        {
            DocumentType = DocumentType.GeneralDiary,
            ContentDraft = "draft",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        });
        context.Cases.Add(kase);
        await context.SaveChangesAsync();

        var repo = new CaseRepository(context);
        var result = await repo.GetWithDocumentsAsync(kase.CaseId);

        Assert.NotNull(result);
        Assert.Single(result!.Documents);
    }
}
