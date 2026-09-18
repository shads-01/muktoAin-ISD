using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

[Collection("MuktoAinSqlDb")]
public class ChatHistoryRepositorySqlTests
{
    private readonly SqlDatabaseFixture _fx;
    public ChatHistoryRepositorySqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Adoption_ClaimsAnonymousRowsOnly_RetainsKey_AndIsIdempotent()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var owner = await _fx.GetUserIdAsync();
        var key = Guid.NewGuid().ToString("N")[..22];
        await using var db = _fx.CreateContext();
        var guest = new ChatSession { SessionKey = key, Title = "Synthetic guest", UpdatedAt = DateTime.UtcNow };
        var committed = new ChatSession
            { SessionKey = key, Title = "Synthetic committed", Status = ChatSessionStatus.Committed, UpdatedAt = DateTime.UtcNow };
        var unrelated = new ChatSession { SessionKey = key + "x", Title = "Synthetic other" };
        var owned = new ChatSession { UserId = owner, SessionKey = key, Title = "Already adopted" };
        db.ChatSessions.AddRange(guest, committed, unrelated, owned);
        await db.SaveChangesAsync();
        var repo = new ChatHistoryRepository(db);
        Assert.Equal(2, await repo.AdoptGuestSessionsAsync(owner, key));
        Assert.Equal(0, await repo.AdoptGuestSessionsAsync(owner, key));
        db.ChangeTracker.Clear();
        var stored = await db.ChatSessions.SingleAsync(s => s.ChatSessionId == guest.ChatSessionId);
        Assert.Equal(owner, stored.UserId);
        Assert.Equal(key, stored.SessionKey);
        Assert.Null((await db.ChatSessions.SingleAsync(s => s.ChatSessionId == unrelated.ChatSessionId)).UserId);
        Assert.Empty(await repo.GetPageAsync(null, key, null, null));
        Assert.Contains(await repo.GetPageAsync(owner, null, null, null), s => s.ChatSessionId == guest.ChatSessionId);
    }

    [SkippableFact]
    public async Task Pages_AreBoundedOrderedAndExcludeAdoptedRowsFromGuestHistory()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var key = Guid.NewGuid().ToString("N")[..22];
        var userId = await _fx.GetUserIdAsync();
        var stamp = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
        await using var db = _fx.CreateContext();
        var rows = Enumerable.Range(1, 30).Select(i => new ChatSession
        {
            SessionKey = key, Title = "Synthetic " + i, UpdatedAt = stamp,
            CreatedAt = stamp, Status = i % 2 == 0 ? ChatSessionStatus.Committed : ChatSessionStatus.InProgress
        }).ToList();
        db.ChatSessions.AddRange(rows);
        db.ChatSessions.Add(new ChatSession
        {
            SessionKey = key, UserId = userId, Title = "Adopted", UpdatedAt = stamp.AddDays(1)
        });
        await db.SaveChangesAsync();
        var repo = new ChatHistoryRepository(db);
        var first = await repo.GetPageAsync(null, key, null, null);
        Assert.Equal(26, first.Count);
        Assert.Equal(rows.Select(s => s.ChatSessionId).OrderByDescending(i => i).Take(26),
            first.Select(s => s.ChatSessionId));
        Assert.Contains(first, s => s.Status == ChatSessionStatus.Committed);
        var lastVisible = first[24];
        var second = await repo.GetPageAsync(null, key, lastVisible.UpdatedAt, lastVisible.ChatSessionId);
        Assert.Equal(5, second.Count);
        Assert.Empty(first.Take(25).Select(s => s.ChatSessionId).Intersect(second.Select(s => s.ChatSessionId)));
        Assert.Empty(await repo.GetPageAsync(null, null, null, null));
        Assert.Empty(await repo.GetPageAsync(null, "unrelated-synthetic", null, null));
    }
}
