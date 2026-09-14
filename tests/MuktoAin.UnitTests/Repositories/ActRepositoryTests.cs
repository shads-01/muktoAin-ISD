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
    private static Act NewAct(string title, int year = 2000) => new()
    {
        Title = title,
        ActNumber = "I",
        Year = year,
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

    [Fact]
    public async Task GetPagedAsync_ReturnsRequestedPageWithTitleFilter()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.AddRange(
            NewAct("The Labour Act, 2006"),
            NewAct("The Labour Welfare Act, 2010"),
            NewAct("The Customs Act, 1969"));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);

        var (items, total) = await repo.GetPagedAsync("Labour", page: 1, pageSize: 1);

        Assert.Equal(2, total);
        var act = Assert.Single(items);
        Assert.Contains("Labour", act.Title);
        // Sections are included so the service can compute SectionCount cheaply.
        Assert.NotNull(act.Sections);
    }

    [Fact]
    public async Task GetPagedAsync_SecondPage_SkipsFirstRow()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.AddRange(
            NewAct("The Labour Act, 2006"),
            NewAct("The Labour Welfare Act, 2010"));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);

        var (items, total) = await repo.GetPagedAsync("Labour", page: 2, pageSize: 1);

        Assert.Equal(2, total);
        var act = Assert.Single(items);
        Assert.Equal("The Labour Welfare Act, 2010", act.Title);
    }

    [Fact]
    public async Task GetPagedAsync_NullKeyword_ReturnsAllActs()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.AddRange(NewAct("Act A"), NewAct("Act B"));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);

        var (items, total) = await repo.GetPagedAsync(null, page: 1, pageSize: 10);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ExistsByTitleYearAsync_ReturnsTrueForExistingTitleYearPair()
    {
        using var context = TestDbContextFactory.Create();
        context.Acts.Add(NewAct("The Labour Act, 2006", year: 2006));
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);

        Assert.True(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2006));
        Assert.False(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2007));
    }

    [Fact]
    public async Task ExistsByTitleYearAsync_ExcludeActId_IgnoresThatRow()
    {
        using var context = TestDbContextFactory.Create();
        var act = NewAct("The Labour Act, 2006", year: 2006);
        context.Acts.Add(act);
        await context.SaveChangesAsync();

        var repo = new ActRepository(context);

        Assert.False(await repo.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, excludeActId: act.ActId));
    }
}
