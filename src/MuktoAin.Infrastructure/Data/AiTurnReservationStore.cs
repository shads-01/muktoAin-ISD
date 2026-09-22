using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Data;

// AUD-3: atomic chat-turn reservation, metered per user (scripts/20_ai_chat_credits.sql).
// A reservation is a CHAT_TURN row. UserId NULL = the shared guest pool.
//
// The UPDLOCK/HOLDLOCK range lock on the counting subqueries, held by the
// transaction until COMMIT, serializes read-check-write: when exactly one free
// turn (or one credit) remains, two concurrent reservations cannot both see it
// — the second blocks on the range lock, re-reads after the first commits,
// and gets nothing.
public class AiTurnReservationStore : IAiTurnReservationStore
{
    private const int TopUp = (int)PaymentPurpose.TopUp;
    private const int Paid = (int)PaymentStatus.Paid;
    private const int Refunded = (int)PaymentStatus.Refunded;

    // Credits bought minus credits spent, for @u. The CHAT_TURN count takes the
    // range lock when run inside the reservation transaction.
    private static readonly string CreditBalanceSql = $"""
        (SELECT ISNULL(SUM(CAST(ChatCredits AS BIGINT)), 0) FROM [dbo].[PAYMENT_ORDER]
         WHERE UserId = @u AND Purpose = {TopUp} AND Status IN ({Paid}, {Refunded}))
        - (SELECT COUNT_BIG(*) FROM [dbo].[CHAT_TURN] WITH (UPDLOCK, HOLDLOCK)
           WHERE UserId = @u AND PaidWithCredit = 1)
        """;

    private readonly AppDbContext _db;

    public AiTurnReservationStore(AppDbContext db)
    {
        _db = db;
    }

    // Guests match UserId IS NULL; "= NULL" would never match.
    private static string UserFilter(int? userId) => userId.HasValue ? "UserId = @u" : "UserId IS NULL";

    public async Task<TurnReservation?> TryReserveAsync(int? userId, DateTime sinceUtc, int freeLimit, CancellationToken ct = default)
    {
        var sql = $"""
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @id BIGINT = NULL, @credit BIT = 0;
            IF (SELECT COUNT_BIG(*) FROM [dbo].[CHAT_TURN] WITH (UPDLOCK, HOLDLOCK)
                WHERE {UserFilter(userId)} AND PaidWithCredit = 0 AND CreatedAt >= @since) < @limit
            BEGIN
                INSERT INTO [dbo].[CHAT_TURN] (UserId, PaidWithCredit, CreatedAt) VALUES (@u, 0, SYSUTCDATETIME());
                SET @id = SCOPE_IDENTITY();
            END
            ELSE IF @u IS NOT NULL AND ({CreditBalanceSql}) > 0
            BEGIN
                INSERT INTO [dbo].[CHAT_TURN] (UserId, PaidWithCredit, CreatedAt) VALUES (@u, 1, SYSUTCDATETIME());
                SET @id = SCOPE_IDENTITY();
                SET @credit = 1;
            END
            COMMIT;
            SELECT @id, @credit;
            """;

        return await WithCommandAsync(sql, cmd =>
        {
            AddParameter(cmd, "@u", userId);
            AddParameter(cmd, "@since", sinceUtc);
            AddParameter(cmd, "@limit", freeLimit);
        }, async cmd =>
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0)) return null;
            return new TurnReservation(reader.GetInt64(0), reader.GetBoolean(1));
        });
    }

    public Task ReleaseAsync(long turnId, CancellationToken ct = default) =>
        WithCommandAsync("DELETE FROM [dbo].[CHAT_TURN] WHERE ChatTurnId = @id;",
            cmd => AddParameter(cmd, "@id", turnId),
            cmd => cmd.ExecuteNonQueryAsync(ct));

    public async Task<int> CountFreeTurnsAsync(int? userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT COUNT_BIG(*) FROM [dbo].[CHAT_TURN]
            WHERE {UserFilter(userId)} AND PaidWithCredit = 0 AND CreatedAt >= @since;
            """;
        var count = await WithCommandAsync(sql, cmd =>
        {
            AddParameter(cmd, "@u", userId);
            AddParameter(cmd, "@since", sinceUtc);
        }, cmd => cmd.ExecuteScalarAsync(ct));
        return Convert.ToInt32(count);
    }

    public async Task<int> GetCreditBalanceAsync(int userId, CancellationToken ct = default)
    {
        // Outside a transaction the lock hints only cost a brief shared lock.
        var balance = await WithCommandAsync($"SELECT {CreditBalanceSql};",
            cmd => AddParameter(cmd, "@u", userId),
            cmd => cmd.ExecuteScalarAsync(ct));
        return Convert.ToInt32(balance);
    }

    // Raw ADO on the context's connection: the reservation batch returns a
    // row after DML, which EF's SqlQuery cannot compose.
    private async Task<T> WithCommandAsync<T>(string sql, Action<DbCommand> bind, Func<DbCommand, Task<T>> run)
    {
        var conn = _db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            bind(cmd);
            return await run(cmd);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        if (value is null) p.DbType = DbType.Int32; // only @u is ever null
        if (value is DateTime) p.DbType = DbType.DateTime2; // CreatedAt is DATETIME2
        cmd.Parameters.Add(p);
    }
}
