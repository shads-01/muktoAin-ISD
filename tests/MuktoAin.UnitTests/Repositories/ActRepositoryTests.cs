using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

// SearchByTitleAsync (EF.Functions.Like) is exercised here too -- the InMemory
// provider does support it for simple wildcard patterns. FullTextSearchAsync and
// GetBySectionIdsAsync on ActSectionRepository are NOT covered here: they use
// FromSqlRaw/CONTAINSTABLE, which the InMemory provider can't translate at all.
// Those are covered in the T-3.3 integration tests against real SQL Server.
public class ActRepositoryTests
{
    private static Act NewAct(string title) => new()
    {
        Title = title,
        ActNumber = "I",
        Year = 2000,
        PublicationDate = "01/01/2000",
        Language = "english",
        SourceUrl = "http://example.com"
    };

    [Fact]
    public async Task GetWithSectionsAsync_ReturnsActWithLoadedSections()
    {
        using var context = TestDbContextFactory.Create();
        var act = NewAct("Test Act");
        act.Sections.Add(new ActSection { OrdinalPosition = 1, SectionText = "Section one text." });
        act.Sections.Add(new ActSection { OrdinalPosition = 2, SectionText = "Section two text." });
        context.Acts.Add(act);
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);
        var result = await repo.GetWithSectionsAsync(act.ActId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Sections.Count);
    }

    [Fact]
    public async Task GetWithSectionsAsync_ReturnsNull_WhenActDoesNotExist()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new ActRepository(context);

        var result = await repo.GetWithSectionsAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchByTitleAsync_ReturnsPartialCaseInsensitiveMatches()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.AddRange(NewAct("The Customs Act, 1969"), NewAct("The Labour Act, 2006"));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);
        var result = await repo.SearchByTitleAsync("Customs");

        Assert.Single(result);
    }

    [Fact]
    public async Task SearchByTitleAsync_ReturnsEmpty_WhenNoMatch()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.Add(NewAct("The Labour Act, 2006"));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);
        var result = await repo.SearchByTitleAsync("Customs");

        Assert.Empty(result);
    }
}
