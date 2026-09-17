using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

// GetBySectionIdsAsync (FromSqlRaw/STRING_SPLIT) and FullTextSearchAsync
// (CONTAINSTABLE) execute raw SQL against SQL-Server-specific features the
// InMemory provider can't translate or execute at all. Per Tultul_plan.md
// Step 3.3's own note, these are covered in the T-3.3 integration tests
// against a real SQL Server instance instead. CountCaseReferencesAsync and
// CountScenarioMappingsAsync are plain LINQ (T-3.1) and are covered below.
public class ActSectionRepositoryTests
{
    [Fact]
    public async Task CountScenarioMappingsAsync_CountsMappingsAcrossTheActsSections()
    {
        using var context = TestDbContextFactory.Create();
        var act = new Act { Title = "Labour Act", Year = 2006 };
        var s1 = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "wages due" };
        var s2 = new ActSection { Act = act, OrdinalPosition = 2, SectionText = "overtime" };
        context.Acts.Add(act);
        context.ActSections.AddRange(s1, s2);
        context.ScenarioMappings.AddRange(
            new ScenarioMapping { Section = s1, ScenarioKeyword = "বেতন বাকি" },
            new ScenarioMapping { Section = s1, ScenarioKeyword = "wages unpaid" });
        await context.SaveChangesAsync();

        var repo = new ActSectionRepository(context);

        Assert.Equal(2, await repo.CountScenarioMappingsAsync(act.ActId));
        Assert.Equal(0, await repo.CountScenarioMappingsAsync(999));
    }

    [Fact]
    public async Task CountCaseReferencesAsync_CountsCitationsAcrossTheActsSections()
    {
        using var context = TestDbContextFactory.Create();
        var act = new Act { Title = "Labour Act", Year = 2006 };
        var section = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "text" };
        var @case = new Case { Title = "t", Description = "d" };
        context.Acts.Add(act);
        context.ActSections.Add(section);
        context.Cases.Add(@case);
        context.CaseActReferences.Add(new CaseActReference { Case = @case, Section = section });
        await context.SaveChangesAsync();

        var repo = new ActSectionRepository(context);

        Assert.Equal(1, await repo.CountCaseReferencesAsync(act.ActId));
        Assert.Equal(0, await repo.CountCaseReferencesAsync(999));
    }
}
