using Microsoft.Data.SqlClient;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

[Collection("MuktoAinSqlDb")]
public class ChatHistoryMigrationSqlTests
{
    private readonly SqlDatabaseFixture _fx;
    public ChatHistoryMigrationSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Migration_HandlesEveryLegacyVariant_AndRerun()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var script = File.ReadAllText(Path.Combine(
            SqlDatabaseFixture.FindScriptsRoot(), "14_chat_sidebar_history.sql"));
        var variants = new[]
        {
            "ALTER TABLE dbo.CHAT_SESSION ADD CONSTRAINT UQ_CHAT_SESSION_SessionKey UNIQUE(SessionKey)",
            "CREATE UNIQUE INDEX UQ_CHAT_SESSION_SessionKey ON dbo.CHAT_SESSION(SessionKey)",
            "CREATE UNIQUE INDEX UQ_CHAT_SESSION_SessionKey ON dbo.CHAT_SESSION(SessionKey) WHERE SessionKey IS NOT NULL",
            "CREATE UNIQUE INDEX IX_CHAT_SESSION_SessionKey ON dbo.CHAT_SESSION(SessionKey) WHERE SessionKey IS NOT NULL"
        };
        foreach (var variant in variants)
        {
            await using var conn = new SqlConnection(_fx.DbConnectionString);
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            async Task Execute(string sql)
            {
                await using var cmd = new SqlCommand(sql, conn, tx);
                await cmd.ExecuteNonQueryAsync();
            }
            try
            {
                await Execute("""
                    SET ANSI_NULLS ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
                    SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON;
                    SET QUOTED_IDENTIFIER ON; SET NUMERIC_ROUNDABORT OFF;
                    ALTER TABLE dbo.CHAT_MESSAGE DROP CONSTRAINT FK_CHAT_MESSAGE_Session;
                    DROP TABLE dbo.CHAT_SESSION;
                    CREATE TABLE dbo.CHAT_SESSION
                    (ChatSessionId int IDENTITY PRIMARY KEY, SessionKey nvarchar(64) NULL);
                    """);
                await Execute(variant);
                await Execute("INSERT dbo.CHAT_SESSION(SessionKey) VALUES (N'synthetic-preserved')");
                for (var pass = 0; pass < 2; pass++)
                    foreach (var batch in SqlScriptRunner.SplitIntoBatches(script))
                        await Execute(batch);
                await Execute("INSERT dbo.CHAT_SESSION(SessionKey) VALUES (N'synthetic-preserved'),(NULL),(NULL)");
                await using var count = new SqlCommand("SELECT COUNT(*) FROM dbo.CHAT_SESSION", conn, tx);
                Assert.Equal(4, Convert.ToInt32(await count.ExecuteScalarAsync()));
                await using var index = new SqlCommand("""
                    SELECT COUNT(*) FROM sys.indexes
                    WHERE object_id=OBJECT_ID(N'dbo.CHAT_SESSION')
                      AND name=N'IX_CHAT_SESSION_SessionKey'
                      AND is_unique=0 AND has_filter=1
                    """, conn, tx);
                Assert.Equal(1, Convert.ToInt32(await index.ExecuteScalarAsync()));
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }
    }
}
