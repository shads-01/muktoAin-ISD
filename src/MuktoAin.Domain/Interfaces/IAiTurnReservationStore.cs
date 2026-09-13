namespace MuktoAin.Domain.Interfaces;

// AUD-3 (TOCTOU quota race): the old TryReserveTurnAsync only COUNTED existing
// AI_LOG rows, so two concurrent Ask requests could both pass with one turn
// left. Implementations must perform the quota check and the reservation
// insert in a SINGLE atomic SQL statement so exactly one of two simultaneous
// requests can reserve the last turn. The reservation row is an AI_LOG row
// that GetRemainingToday already counts (RequestType = RightsExplanation,
// CaseId = NULL); ReleaseOneAsync removes it when the turn turned out to be
// cache-served or retrieval-only (no model call was made).
//
// Lives in Domain (not Application) so the SQL Server implementation in
// Infrastructure can see it — Infrastructure only references Domain (same
// seam pattern as IEncryptionService / IKeywordSectionSearch).
public interface IAiTurnReservationStore
{
    Task<bool> TryReserveAsync(DateTime sinceUtc, int limit, CancellationToken ct = default);

    Task ReleaseOneAsync(CancellationToken ct = default);
}
