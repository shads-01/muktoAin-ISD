using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// AUD-7. Fail-safe by contract: any exception from the repository write is
// logged and swallowed. The admin action being audited has already succeeded
// by the time this is called; losing its audit row is logged (LogError), never
// rethrown (docs/PROJECT_AUDIT_REPORT.md — Admin Scope #5).
public class AdminAuditService : IAdminAuditService
{
    private readonly IRepository<AdminAuditLog> _repo;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(IRepository<AdminAuditLog> repo, ILogger<AdminAuditService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task LogAdminActionAsync(
        int adminUserId,
        string action,
        int? targetUserId = null,
        int? targetEntityId = null,
        string? details = null)
    {
        try
        {
            await _repo.AddAsync(new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                TargetUserId = targetUserId,
                TargetEntityId = targetEntityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
            await _repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write admin audit log (admin {AdminUserId}, action {Action})",
                adminUserId, action);
        }
    }
}
