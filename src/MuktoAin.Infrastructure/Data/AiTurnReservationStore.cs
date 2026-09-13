using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Data;

// AUD-3: atomic chat-turn reservation. The reservation is an AI_LOG row
// (RequestType = RightsExplanation, CaseId = NULL, ModelUsed = marker), so
// AiBudgetService.GetRemainingToday counts it the moment it lands.
//
// The UPDLOCK/HOLDLOCK range lock on the counting subquery serializes the
// read-check-write inside ONE statement: when exactly one turn remains, two
// concurrent inserts cannot both see count < limit — the second blocks on the
// range lock, re-evaluates after the first commits, and inserts 0 rows.
public class AiTurnReservationStore : IAiTurnReservationStore
{
    // Sentinel in ModelUsed — a real pipeline log row always carries the
    // actual model name (e.g. "gemini-2.5-flash"), so this never collides.
    public const string ReservedModelMarker = "(reserved)";

    private readonly AppDbContext _db;

    public AiTurnReservationStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryReserveAsync(DateTime sinceUtc, int limit, CancellationToken ct = default)
    {
        var requestType = (int)AiRequestType.RightsExplanation;

        var inserted = await _db.Database.ExecuteSqlAsync($"""
            INSERT INTO [dbo].[AI_LOG]
                (CaseId, RequestType, PromptText, ResponseText, ModelUsed, TokensUsed, LatencyMs, CreatedAt)
            SELECT NULL, {requestType}, N'', N'', {ReservedModelMarker}, 0, 0, SYSUTCDATETIME()
            WHERE (
                SELECT COUNT_BIG(*)
                FROM [dbo].[AI_LOG] WITH (UPDLOCK, HOLDLOCK)
                WHERE RequestType = {requestType} AND CaseId IS NULL AND CreatedAt >= {sinceUtc}
            ) < {limit}
            """, ct);

        return inserted > 0;
    }

    public async Task ReleaseOneAsync(CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlAsync($"""
            DELETE FROM [dbo].[AI_LOG]
            WHERE LogId = (
                SELECT TOP (1) LogId
                FROM [dbo].[AI_LOG]
                WHERE ModelUsed = {ReservedModelMarker}
                ORDER BY LogId DESC)
            """, ct);
    }
}
