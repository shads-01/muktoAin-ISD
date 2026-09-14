namespace MuktoAin.IntegrationTests.Support;

[Collection("MuktoAinSqlDb")]
public class SqlDatabaseFixtureSmokeTests
{
    private readonly SqlDatabaseFixture _fx;

    public SqlDatabaseFixtureSmokeTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Fixture_Materialized_The_Schema_From_The_Sql_Scripts()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var tableCount = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo');"));
        // 14 core tables (scripts/02_schema.sql) + CHAT_SESSION, CHAT_MESSAGE,
        // ANSWER_CACHE, PAYMENT_ORDER, PAYOUT_REQUEST (scripts/08_redesign_tables.sql).
        Assert.True(tableCount >= 19, $"Expected at least 19 dbo tables, found {tableCount}.");
    }

    [SkippableFact]
    public async Task FullText_Availability_Matches_Server_Property()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var isFullTextInstalled = Convert.ToInt32(await _fx.ScalarAsync(
            "SELECT CAST(SERVERPROPERTY('IsFullTextInstalled') AS int);"));
        Assert.Equal(isFullTextInstalled == 1, _fx.FullTextAvailable);
    }
}
