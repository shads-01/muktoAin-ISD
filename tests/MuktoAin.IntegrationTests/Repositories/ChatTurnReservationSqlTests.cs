using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// AiTurnReservationStore on real SQL Server (scripts/20_ai_chat_credits.sql):
// per-user free turns, the shared guest pool, chat credits from TopUp orders,
// and the UPDLOCK/HOLDLOCK guarantee that concurrent reservations can never
// overspend the last free turn or the last credit.
[Collection("MuktoAinSqlDb")]
public class ChatTurnReservationSqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public ChatTurnReservationSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    private static DateTime Since => DateTime.UtcNow.AddMinutes(-5);

    private async Task<int> NewUserAsync()
    {
        await using var ctx = _fx.CreateContext();
        var user = new User
        {
            FullName = "Credit Tester",
            Email = $"credits-{Guid.NewGuid():N}@test.local",
            Role = UserRole.Citizen
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task AddTopUpAsync(int userId, int credits, PaymentStatus status)
    {
        await using var ctx = _fx.CreateContext();
        ctx.PaymentOrders.Add(new PaymentOrder
        {
            UserId = userId,
            Purpose = PaymentPurpose.TopUp,
            Status = status,
            Amount = credits * 5m,
            ChatCredits = credits,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Free_Turns_Are_Metered_Per_User()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        await using var ctx = _fx.CreateContext();
        var store = new AiTurnReservationStore(ctx);

        Assert.NotNull(await store.TryReserveAsync(alice, Since, 2));
        Assert.NotNull(await store.TryReserveAsync(alice, Since, 2));
        Assert.Null(await store.TryReserveAsync(alice, Since, 2)); // Alice's 2 used, no credits

        // Bob's pool is his own.
        var bobTurn = await store.TryReserveAsync(bob, Since, 2);
        Assert.NotNull(bobTurn);
        Assert.False(bobTurn!.PaidWithCredit);
        Assert.Equal(2, await store.CountFreeTurnsAsync(alice, Since));
        Assert.Equal(1, await store.CountFreeTurnsAsync(bob, Since));
    }

    [SkippableFact]
    public async Task Release_Gives_The_Free_Turn_Back()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var user = await NewUserAsync();
        await using var ctx = _fx.CreateContext();
        var store = new AiTurnReservationStore(ctx);

        var turn = await store.TryReserveAsync(user, Since, 1);
        Assert.Null(await store.TryReserveAsync(user, Since, 1));

        await store.ReleaseAsync(turn!.TurnId);

        Assert.NotNull(await store.TryReserveAsync(user, Since, 1));
    }

    [SkippableFact]
    public async Task Credits_Are_Spent_After_Free_Turns_And_Released_Credit_Comes_Back()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var user = await NewUserAsync();
        await AddTopUpAsync(user, credits: 2, PaymentStatus.Paid);
        await using var ctx = _fx.CreateContext();
        var store = new AiTurnReservationStore(ctx);
        Assert.Equal(2, await store.GetCreditBalanceAsync(user));

        var free = await store.TryReserveAsync(user, Since, 1);
        var credit1 = await store.TryReserveAsync(user, Since, 1);
        var credit2 = await store.TryReserveAsync(user, Since, 1);

        Assert.False(free!.PaidWithCredit);
        Assert.True(credit1!.PaidWithCredit);
        Assert.True(credit2!.PaidWithCredit);
        Assert.Null(await store.TryReserveAsync(user, Since, 1));
        Assert.Equal(0, await store.GetCreditBalanceAsync(user));

        await store.ReleaseAsync(credit2.TurnId);
        Assert.Equal(1, await store.GetCreditBalanceAsync(user));
    }

    [SkippableFact]
    public async Task Only_Paid_And_Refunded_TopUps_Count_Towards_Credits()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var user = await NewUserAsync();
        await AddTopUpAsync(user, credits: 10, PaymentStatus.Paid);
        await AddTopUpAsync(user, credits: 3, PaymentStatus.Refunded); // spent part kept by the refund
        await AddTopUpAsync(user, credits: 50, PaymentStatus.Pending);
        await AddTopUpAsync(user, credits: 50, PaymentStatus.Failed);
        await using var ctx = _fx.CreateContext();

        Assert.Equal(13, await new AiTurnReservationStore(ctx).GetCreditBalanceAsync(user));
    }

    [SkippableFact]
    public async Task Guests_Share_One_Pool_Separate_From_Users_And_Never_Spend_Credits()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();
        var store = new AiTurnReservationStore(ctx);
        // Other tests' guest turns land in the same pool: allow exactly one more.
        var limit = await store.CountFreeTurnsAsync(null, Since) + 1;

        var guest = await store.TryReserveAsync(null, Since, limit);
        Assert.NotNull(guest);
        Assert.False(guest!.PaidWithCredit);
        Assert.Null(await store.TryReserveAsync(null, Since, limit));

        var user = await NewUserAsync();
        Assert.NotNull(await store.TryReserveAsync(user, Since, 1));

        await store.ReleaseAsync(guest.TurnId);
    }

    [SkippableFact]
    public async Task Concurrent_Reservations_Never_Overspend_The_Last_Credit()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var user = await NewUserAsync();
        await AddTopUpAsync(user, credits: 1, PaymentStatus.Paid);

        // freeLimit 0: every reservation must spend a credit. Separate contexts
        // = separate connections, like concurrent requests.
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = _fx.CreateContext();
            return await new AiTurnReservationStore(ctx).TryReserveAsync(user, Since, 0);
        });
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r != null);
        await using var check = _fx.CreateContext();
        Assert.Equal(0, await new AiTurnReservationStore(check).GetCreditBalanceAsync(user));
    }

    [SkippableFact]
    public async Task Concurrent_Reservations_Never_Overspend_The_Last_Free_Turn()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var user = await NewUserAsync();

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = _fx.CreateContext();
            return await new AiTurnReservationStore(ctx).TryReserveAsync(user, Since, 1);
        });
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r != null);
    }
}
