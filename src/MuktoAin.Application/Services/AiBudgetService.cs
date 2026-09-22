using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Application.Services;

// FR-20 quota meter, per user. A chat turn = a CHAT_TURN reservation row
// (AiTurnReservationStore). Signed-in users get SignedInDailyLimit free turns
// a day each, then spend chat credits bought by TopUp. Guests share one pool
// of GuestDailyLimit free turns a day and cannot buy credits. Resets at
// midnight Pacific (matches Gemini RPD reset).
public class AiBudgetService
{
    private const int GuestDailyLimit = 10;
    private const int SignedInDailyLimit = 30;

    private readonly IAiTurnReservationStore _reservationStore;

    public AiBudgetService(IAiTurnReservationStore reservationStore)
    {
        _reservationStore = reservationStore;
    }

    private static bool IsPacificDaylight =>
        DateTime.UtcNow.Month > 3 && DateTime.UtcNow.Month < 11;

    // UTC instant of the most recent midnight Pacific (approximation is fine
    // for a quota meter; Google's exact reset instant is not contractual).
    private static DateTime PacificMidnightUtc()
    {
        var ptTodayMidnight = DateTime.UtcNow.AddHours(IsPacificDaylight ? -7 : -8).Date;
        var asUtc = ptTodayMidnight.AddHours(IsPacificDaylight ? 7 : 8);
        return asUtc > DateTime.UtcNow ? asUtc.AddDays(-1) : asUtc;
    }

    public int DailyLimitFor(bool isLoggedIn) =>
        isLoggedIn ? SignedInDailyLimit : GuestDailyLimit;

    public async Task<QuotaSnapshotDto> GetRemainingToday(int? userId, string? sessionKey)
    {
        var used = await _reservationStore.CountFreeTurnsAsync(userId, PacificMidnightUtc());
        var limit = DailyLimitFor(userId.HasValue);
        var credits = userId.HasValue
            ? Math.Max(0, await _reservationStore.GetCreditBalanceAsync(userId.Value))
            : 0;
        return new QuotaSnapshotDto(Math.Max(0, limit - used), limit, userId.HasValue, credits);
    }

    // AUD-3: atomic reserve-before-call — a second concurrent request hits the
    // wall here instead of after both Gemini calls have already fired. Uses a
    // free turn first, then a credit. Null = daily limit reached and no credits.
    public Task<TurnReservation?> TryReserveTurnAsync(int? userId, string? sessionKey) =>
        _reservationStore.TryReserveAsync(userId, PacificMidnightUtc(), DailyLimitFor(userId.HasValue));

    // Gives a reserved turn back (and its credit, if it spent one) when the
    // turn turned out to be free: cache hit, retrieval-only, blocked, or failed.
    public Task ReleaseReservationAsync(TurnReservation reservation) =>
        _reservationStore.ReleaseAsync(reservation.TurnId);

    // The kept reservation row is the charge; this is a read-back only.
    public Task<QuotaSnapshotDto> RecordTurnUsed(int? userId, string? sessionKey) =>
        GetRemainingToday(userId, sessionKey);
}
