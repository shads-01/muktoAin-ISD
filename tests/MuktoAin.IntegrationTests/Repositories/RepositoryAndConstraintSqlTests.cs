using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: behaviors only a real SQL Server can demonstrate and that EF InMemory
// silently accepts (or never models): unique-index violations, filtered
// indexes with NULL semantics, and explicit transaction commit/rollback.
[Collection("MuktoAinSqlDb")]
public class RepositoryAndConstraintSqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public RepositoryAndConstraintSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Duplicate_User_Email_Violates_Unique_Constraint()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var email = $"dup-{Guid.NewGuid():N}@test.local";

        await using var ctx = _fx.CreateContext();
        ctx.Users.Add(new User { FullName = "First", Email = email, Role = UserRole.Citizen });
        await ctx.SaveChangesAsync();

        ctx.Users.Add(new User { FullName = "Second", Email = email, Role = UserRole.Citizen });
        // UQ_USER_Email (scripts/02_schema.sql) must reject the second row.
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Filtered_SessionKey_Index_Allows_Many_Null_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();

        // Logged-in sessions have NULL SessionKey; the filtered nonunique
        // index (IX_CHAT_SESSION_SessionKey ... WHERE SessionKey IS NOT NULL,
        // per scripts/14) must allow arbitrarily many NULLs.
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session A" });
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session B" });
        ctx.ChatSessions.Add(new ChatSession { Title = "Guest session C" });

        await ctx.SaveChangesAsync();

        Assert.All(ctx.ChatSessions.Local, s => Assert.True(s.ChatSessionId > 0));
    }

    [SkippableFact]
    public async Task Filtered_SessionKey_Index_Allows_Duplicate_Guest_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var key = Guid.NewGuid().ToString("N")[..22];
        await using var db = _fx.CreateContext();
        db.ChatSessions.AddRange(
            new ChatSession { SessionKey = key, Title = "Synthetic A" },
            new ChatSession { SessionKey = key, Title = "Synthetic B" });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ChatSessions.CountAsync(s => s.SessionKey == key));
    }

    [SkippableFact]
    public async Task Case_Insert_Persists_With_All_Required_Foreign_Keys()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        await using var ctx = _fx.CreateContext();
        var c = new Case
        {
            CategoryId = categoryId,
            DistrictId = (byte)districtId,
            Title = "FK chain test",
            Description = "Seeded by T-3.3",
            Language = "en",
            Status = CaseStatus.Submitted,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.Cases.Add(c);
        await ctx.SaveChangesAsync();

        // Re-query through a new context to prove the full FK chain
        // (CASE -> DISTRICT, CASE -> CASE_CATEGORY) resolved server-side.
        await using var verify = _fx.CreateContext();
        var stored = await verify.Cases
            .Include(x => x.District)
            .Include(x => x.Category)
            .SingleAsync(x => x.CaseId == c.CaseId);
        Assert.Equal("IntegrationTest District", stored.District.Name);
        Assert.Equal("IntegrationTest Category", stored.Category.Name);
        Assert.Equal(CaseStatus.Submitted, stored.Status);
    }

    [SkippableFact]
    public async Task Transaction_Commit_Persists_Row()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        int caseId;
        await using (var ctx = _fx.CreateContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var c = new Case
            {
                CategoryId = categoryId,
                DistrictId = (byte)districtId,
                Title = "Commit test",
                Description = "d",
                Language = "en",
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.Cases.Add(c);
            await ctx.SaveChangesAsync();
            await tx.CommitAsync();
            caseId = c.CaseId;
        }

        await using var verify = _fx.CreateContext();
        Assert.NotNull(await verify.Cases.FindAsync(caseId));
    }

    [SkippableFact]
    public async Task Transaction_Rollback_Leaves_No_Row()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        int caseId;
        await using (var ctx = _fx.CreateContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            var c = new Case
            {
                CategoryId = categoryId,
                DistrictId = (byte)districtId,
                Title = "Rollback test",
                Description = "d",
                Language = "en",
                CreatedAt = now,
                UpdatedAt = now,
            };
            ctx.Cases.Add(c);
            await ctx.SaveChangesAsync(); // row exists inside the uncommitted tx
            await tx.RollbackAsync();
            caseId = c.CaseId;
        }

        await using var verify = _fx.CreateContext();
        Assert.Null(await verify.Cases.FindAsync(caseId));
    }
}
