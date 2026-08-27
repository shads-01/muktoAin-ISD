using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Loads the 4 core case categories from data/categories.json into CASE_CATEGORY.
// CategoryId is IDENTITY in the SSMS-authored schema, so the id in the JSON is
// informational only -- SQL Server assigns the real id on insert (the JSON is
// already ordered 1-4, so the assigned ids line up in practice).
public static class SeedCategories
{
    private const string CommonActionsDelimiter = "|";

    private sealed record CategorySeedDto(
        int CategoryId,
        string Name,
        string Description,
        string NameBn,
        string DescriptionBn,
        List<string>? CommonActions,
        List<string>? CommonActionsEn);

    public static async Task SeedAsync(AppDbContext context, string contentRootPath)
    {
        var dtos = await SeedJsonLoader.LoadAsync<CategorySeedDto>(contentRootPath, "categories.json");
        var existing = await context.CaseCategories.OrderBy(c => c.CategoryId).ToListAsync();

        if (existing.Count == 0)
        {
            var categories = dtos.Select(d => new CaseCategory
            {
                Name = d.Name,
                Description = d.Description,
                NameBn = d.NameBn,
                DescriptionBn = d.DescriptionBn,
                CommonActions = JoinCommonActions(d.CommonActions),
                CommonActionsEn = JoinCommonActions(d.CommonActionsEn),
            });
            context.CaseCategories.AddRange(categories);
            await context.SaveChangesAsync();
            return;
        }

        // Backfill: rows seeded before NameBn/DescriptionBn/CommonActions existed on
        // CaseCategory are still blank on those columns. Matched by insertion order --
        // CategoryId is IDENTITY-assigned in seed order, same order as the JSON.
        var changed = false;
        foreach (var (category, dto) in existing.Zip(dtos))
        {
            if (string.IsNullOrEmpty(category.NameBn))
            {
                category.NameBn = dto.NameBn;
                category.DescriptionBn = dto.DescriptionBn;
                changed = true;
            }

            if (string.IsNullOrEmpty(category.CommonActions))
            {
                category.CommonActions = JoinCommonActions(dto.CommonActions);
                changed = true;
            }

            if (string.IsNullOrEmpty(category.CommonActionsEn))
            {
                category.CommonActionsEn = JoinCommonActions(dto.CommonActionsEn);
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync();
        }
    }

    private static string JoinCommonActions(List<string>? items) =>
        items == null ? string.Empty : string.Join(CommonActionsDelimiter, items);
}
