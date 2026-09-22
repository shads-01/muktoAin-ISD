using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: real-SQL-Server coverage for the two ActSectionRepository raw-SQL
// paths that the EF InMemory provider cannot execute and that the T-1.14
// unit tests deliberately excluded (see the empty
// tests/MuktoAin.UnitTests/Repositories/ActSectionRepositoryTests.cs):
//   - GetBySectionIdsAsync -> FromSqlRaw with STRING_SPLIT
//   - FullTextSearchAsync  -> FromSqlInterpolated with CONTAINSTABLE
[Collection("MuktoAinSqlDb")]
public class ActSectionRepositorySqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public ActSectionRepositorySqlTests(SqlDatabaseFixture fx) => _fx = fx;

    // ---- FromSqlRaw + STRING_SPLIT -----------------------------------------

    [SkippableFact]
    public async Task GetBySectionIdsAsync_Returns_Matching_Sections_With_Act_Loaded()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionA) = await _fx.SeedActWithSectionAsync("First section text.");
        var (_, sectionB) = await _fx.SeedActWithSectionAsync("Second section text.");
        await _fx.SeedActWithSectionAsync("Third section text."); // must NOT come back

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.GetBySectionIdsAsync(new[] { sectionA, sectionB })).ToList();

        Assert.Equal(2, results.Count);
        // The ids travel as ONE string parameter into STRING_SPLIT ({0}), and
        // the Include(s => s.Act) navigation is hydrated by a real join.
        Assert.All(results, s => Assert.NotNull(s.Act));
        Assert.Equal(
            new[] { sectionA, sectionB }.OrderBy(i => i),
            results.Select(s => s.SectionId).OrderBy(i => i));
    }

    [SkippableFact]
    public async Task GetBySectionIdsAsync_Returns_Empty_When_No_Id_Matches()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = await repo.GetBySectionIdsAsync(new[] { -1, -2 });

        Assert.Empty(results);
    }

    // ---- FromSqlInterpolated + CONTAINSTABLE (skips without the FTS feature) -

    [SkippableFact]
    public async Task FullTextSearchAsync_Phrase_Query_Finds_Section()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "The wages of every worker shall be paid before the expiry of the seventh working day.");
        await _fx.SeedActWithSectionAsync("Unrelated consumer protection tribunal text.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("\"wages of every worker\"", maxResults: 5)).ToList();

        var match = Assert.Single(results);
        Assert.Contains("wages of every worker", match.SectionText, StringComparison.OrdinalIgnoreCase);
        // The CONTAINSTABLE query joins [dbo].[ACT] so the title is available
        // for FR-7 display even though ACT_SECTION has no ActTitle column.
        Assert.NotNull(match.Act);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Ranks_Term_Repetitions_Higher()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "Wages wages wages: this section repeats wages to rank first for the wages query.");
        await _fx.SeedActWithSectionAsync("A single mention of wages here.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("wages", maxResults: 10)).ToList();

        // CONTAINSTABLE RANK grows with term frequency; the query orders by
        // ft.[RANK] DESC, so the repetition-heavy section must come first.
        Assert.True(results.Count >= 2, $"Expected both wages sections, got {results.Count}.");
        Assert.Contains("repeats", results[0].SectionText, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Apostrophe_Phrase_Does_Not_Throw()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        await _fx.SeedActWithSectionAsync(
            "If a worker's wages are withheld, the worker's remedy is a General Diary.");
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        // The search condition is a single SQL parameter ({query} in
        // FromSqlInterpolated); an apostrophe inside a quoted phrase must be
        // treated as term content, never as SQL string termination.
        var results = (await repo.FullTextSearchAsync("\"worker's wages\"", maxResults: 5)).ToList();

        Assert.Single(results);
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Never_Treats_Query_As_Raw_Sql()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        var before = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        try
        {
            // Malformed search conditions may legitimately be rejected by the
            // FTS parser with a SqlException — what must NEVER happen is the
            // string executing as SQL.
            await repo.FullTextSearchAsync("wages\"; DROP TABLE [dbo].[ACT_SECTION]; --", maxResults: 5);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // FTS parser rejected the value — acceptable.
        }

        var after = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));
        Assert.Equal(before, after); // table survived => parameterization held
    }

    [SkippableFact]
    public async Task FullTextSearchAsync_Respects_MaxResults()
    {
        Skip.IfNot(_fx.FullTextAvailable);
        for (var i = 0; i < 3; i++)
        {
            await _fx.SeedActWithSectionAsync($"Fine notice number {i} about unpaid wages.");
        }
        await _fx.WaitForFullTextIndexAsync();

        await using var ctx = _fx.CreateContext();
        var repo = new ActSectionRepository(ctx);

        var results = (await repo.FullTextSearchAsync("wages", maxResults: 2)).ToList();

        Assert.Equal(2, results.Count); // TOP({maxResults}) is parameterized, not concatenated
    }
}
