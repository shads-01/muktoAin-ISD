using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

public class ScenarioMappingRepositoryTests
{
    private static async Task<ActSection> SeedSectionAsync(MuktoAin.Infrastructure.Data.AppDbContext context)
    {
        var act = new Act { Title = "Act", ActNumber = "I", Year = 2000, PublicationDate = "x", Language = "english", SourceUrl = "x" };
        var section = new ActSection { OrdinalPosition = 1, SectionText = "text" };
        act.Sections.Add(section);
        context.Acts.Add(act);
        await context.SaveChangesAsync();
        return section;
    }

    [Fact]
    public async Task SearchByKeywordAsync_ReturnsPartialMatches()
    {
        using var context = TestDbContextFactory.Create();
        var section = await SeedSectionAsync(context);
        context.ScenarioMappings.AddRange(
            new ScenarioMapping { SectionId = section.SectionId, ScenarioKeyword = "unpaid wages" },
            new ScenarioMapping { SectionId = section.SectionId, ScenarioKeyword = "wrongful termination" });
        await context.SaveChangesAsync();

        var repo = new ScenarioMappingRepository(context);
        var result = await repo.SearchByKeywordAsync("wage");

        Assert.Single(result);
    }
}
