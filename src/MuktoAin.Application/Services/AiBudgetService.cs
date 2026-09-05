using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// FR-20 quota meter. A chat turn = a RightsExplanation run logged with
// CaseId = null (unsaved chat cases); committed cases always carry their
// CaseId, so case-critical generation is never charged to a citizen's chat
// quota. Guests ~10/day per browser session, signed-in ~30/day. Resets at
// midnight Pacific (matches Gemini RPD reset).
public class AiBudgetService
{
    private const int GuestDailyLimit = 10;
    private const int SignedInDailyLimit = 30;

    private readonly IRepository<AiLog> _logRepo;

    public AiBudgetService(IRepository<AiLog> logRepo)
    {
        _logRepo = logRepo;
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
        var since = PacificMidnightUtc();
        var logs = await _logRepo.GetAllAsync();
        // A chat turn = a RightsExplanation run with NO case attached
        // (unsaved chat cases log CaseId = null; committed cases always
        // carry their CaseId). Case-critical generation is never counted.
        var used = logs.Count(l =>
            l.CreatedAt >= since
            && l.RequestType == AiRequestType.RightsExplanation
            && l.CaseId == null);
        var limit = DailyLimitFor(userId.HasValue);
        return new QuotaSnapshotDto(Math.Max(0, limit - used), limit, userId.HasValue);
    }

    public async Task<bool> TryReserveTurnAsync(int? userId, string? sessionKey)
    {
        var snapshot = await GetRemainingToday(userId, sessionKey);
        return snapshot.RemainingToday > 0;
    }

    public Task<QuotaSnapshotDto> RecordTurnUsed(int? userId, string? sessionKey)
    {
        // The turn was already logged to AI_LOG by the orchestration pipeline;
        // metering reads the log, so this is a read-back only.
        return GetRemainingToday(userId, sessionKey);
    }
}
