using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatHistoryRow>> GetPageAsync(
        int? userId, string? sessionKey, DateTime? beforeUpdatedAt,
        int? beforeId, CancellationToken ct = default);

    Task<int> AdoptGuestSessionsAsync(int userId, string sessionKey, CancellationToken ct = default);
}
