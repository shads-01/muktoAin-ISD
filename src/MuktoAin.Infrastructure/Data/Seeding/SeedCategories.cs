using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Loads the 4 core case categories from data/categories.json into CASE_CATEGORY.
// CategoryId is IDENTITY in the SSMS-authored schema, so the id in the JSON is
// informational only -- SQL Server assigns the real id on insert (the JSON is
// already ordered 1-4, so the assigned ids line up in practice).
public static class SeedCategories
{
    private sealed record CategorySeedDto(int CategoryId, string Name, string Description);

    public static async Task SeedAsync(AppDbContext context, string contentRootPath)
    {
        if (await context.CaseCategories.AnyAsync()) return; // idempotent

        var dtos = await SeedJsonLoader.LoadAsync<CategorySeedDto>(contentRootPath, "categories.json");
        var categories = dtos.Select(d => new CaseCategory { Name = d.Name, Description = d.Description });
        context.CaseCategories.AddRange(categories);
        await context.SaveChangesAsync();
    }
}
