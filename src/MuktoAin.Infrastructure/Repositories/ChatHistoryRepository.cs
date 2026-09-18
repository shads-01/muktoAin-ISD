using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Models;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class ChatHistoryRepository : IChatHistoryRepository
{
    private readonly AppDbContext _db;
    public ChatHistoryRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ChatHistoryRow>> GetPageAsync(
        int? userId, string? sessionKey, DateTime? beforeUpdatedAt,
        int? beforeId, CancellationToken ct = default)
    {
        if (!userId.HasValue && string.IsNullOrEmpty(sessionKey))
            return Array.Empty<ChatHistoryRow>();
        return await _db.Database.SqlQuery<ChatHistoryRow>($"""
            SELECT TOP (26) s.ChatSessionId, s.Title, s.UpdatedAt,
                (SELECT COUNT(*) FROM dbo.CHAT_MESSAGE m
                 WHERE m.ChatSessionId=s.ChatSessionId) AS MessageCount,
                s.Status, s.CommittedCaseId AS CaseId
            FROM dbo.CHAT_SESSION s
            WHERE (({userId} IS NOT NULL AND s.UserId={userId})
                OR ({userId} IS NULL AND s.UserId IS NULL
                    AND {sessionKey} IS NOT NULL AND {sessionKey}<>N''
                    AND s.SessionKey={sessionKey}))
              AND ({beforeUpdatedAt} IS NULL
                OR s.UpdatedAt<{beforeUpdatedAt}
                OR (s.UpdatedAt={beforeUpdatedAt} AND s.ChatSessionId<{beforeId}))
            ORDER BY s.UpdatedAt DESC, s.ChatSessionId DESC
            """).ToListAsync(ct);
    }

    public async Task<int> AdoptGuestSessionsAsync(int userId, string sessionKey, CancellationToken ct = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        if (string.IsNullOrEmpty(sessionKey)) return 0;
        return await _db.Database.ExecuteSqlAsync($"""
            UPDATE dbo.CHAT_SESSION SET UserId={userId}
            WHERE SessionKey={sessionKey} AND UserId IS NULL
            """, ct);
    }
}
