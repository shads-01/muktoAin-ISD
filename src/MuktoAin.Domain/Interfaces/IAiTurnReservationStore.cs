namespace MuktoAin.Domain.Interfaces;

// AUD-3 (TOCTOU quota race) + chat credits. Implementations must perform the
// quota check and the reservation insert in a SINGLE atomic SQL statement so
// exactly one of two simultaneous requests can reserve the last free turn (or
// spend the last credit). A reservation is a CHAT_TURN row; ReleaseAsync
// deletes it when the turn turned out to be free (cache hit, retrieval-only,
// blocked), which also gives back a spent credit.
//
// Metering is per user: userId null = the one shared guest pool.
//
// Lives in Domain (not Application) so the SQL Server implementation in
// Infrastructure can see it — Infrastructure only references Domain (same
// seam pattern as IEncryptionService / IKeywordSectionSearch).
public interface IAiTurnReservationStore
{
    // A free turn while fewer than freeLimit free turns exist since sinceUtc;
    // otherwise (signed-in users only) a credit turn while the credit balance
    // is above 0. Null when neither is available.
    Task<TurnReservation?> TryReserveAsync(int? userId, DateTime sinceUtc, int freeLimit, CancellationToken ct = default);

    Task ReleaseAsync(long turnId, CancellationToken ct = default);

    Task<int> CountFreeTurnsAsync(int? userId, DateTime sinceUtc, CancellationToken ct = default);

    // Credits bought (Paid/Refunded TopUp orders) minus credit turns. Can be
    // below 0 only briefly, in a refund race; callers clamp for display.
    Task<int> GetCreditBalanceAsync(int userId, CancellationToken ct = default);
}

public record TurnReservation(long TurnId, bool PaidWithCredit);
