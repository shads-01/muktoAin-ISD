namespace MuktoAin.Application.Services;

// AUD-7: append-only administrative audit trail. Implementations MUST be
// fail-safe — LogAdminActionAsync never throws, so a failed audit write can
// never turn into a 500 for the admin action it is recording.
public interface IAdminAuditService
{
    Task LogAdminActionAsync(
        int adminUserId,
        string action,
        int? targetUserId = null,
        int? targetEntityId = null,
        string? details = null);
}
