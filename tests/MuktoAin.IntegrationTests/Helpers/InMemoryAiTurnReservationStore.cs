using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Helpers;

// The web test factories run on EF InMemory, where AiTurnReservationStore's
// raw SQL cannot run. Same rules, in memory: per-user free turns, a shared
// guest pool, credits from Paid/Refunded TopUp orders. Atomicity is covered on
// real SQL Server by ChatTurnReservationSqlTests.
public class InMemoryAiTurnReservationStore : IAiTurnReservationStore
{
    // Turns outlive a request scope, like CHAT_TURN rows.
    public class TurnLog
    {
        public readonly object Gate = new();
        public readonly List<(long Id, int? UserId, bool Credit, DateTime At)> Turns = new();
        public long NextId = 1;
    }

    private readonly AppDbContext _db;
    private readonly TurnLog _log;

    public InMemoryAiTurnReservationStore(AppDbContext db, TurnLog log)
    {
        _db = db;
        _log = log;
    }

    public async Task<TurnReservation?> TryReserveAsync(int? userId, DateTime sinceUtc, int freeLimit, CancellationToken ct = default)
    {
        var bought = userId.HasValue ? Bought(userId.Value) : 0;
        await Task.CompletedTask;
        lock (_log.Gate)
        {
            bool credit;
            if (_log.Turns.Count(t => t.UserId == userId && !t.Credit && t.At >= sinceUtc) < freeLimit)
                credit = false;
            else if (userId.HasValue && bought - _log.Turns.Count(t => t.UserId == userId && t.Credit) > 0)
                credit = true;
            else
                return null;
            var id = _log.NextId++;
            _log.Turns.Add((id, userId, credit, DateTime.UtcNow));
            return new TurnReservation(id, credit);
        }
    }

    public Task ReleaseAsync(long turnId, CancellationToken ct = default)
    {
        lock (_log.Gate) _log.Turns.RemoveAll(t => t.Id == turnId);
        return Task.CompletedTask;
    }

    public Task<int> CountFreeTurnsAsync(int? userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        lock (_log.Gate)
            return Task.FromResult(_log.Turns.Count(t => t.UserId == userId && !t.Credit && t.At >= sinceUtc));
    }

    public Task<int> GetCreditBalanceAsync(int userId, CancellationToken ct = default)
    {
        var bought = Bought(userId);
        lock (_log.Gate)
            return Task.FromResult(bought - _log.Turns.Count(t => t.UserId == userId && t.Credit));
    }

    private int Bought(int userId) => _db.PaymentOrders
        .Where(o => o.UserId == userId && o.Purpose == PaymentPurpose.TopUp
                    && (o.Status == PaymentStatus.Paid || o.Status == PaymentStatus.Refunded))
        .Sum(o => o.ChatCredits);
}
