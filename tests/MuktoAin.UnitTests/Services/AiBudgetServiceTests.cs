using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Interfaces;
using Xunit;

namespace MuktoAin.UnitTests.Services;

// AUD-3 + chat credits: TryReserveTurnAsync delegates to IAiTurnReservationStore,
// whose single-statement SQL makes the check+insert atomic (covered on real
// SQL in RepositoryAndConstraintSqlTests). These tests pin the Application-layer
// contract: per-user metering, the daily limit per tier, the credit balance in
// the snapshot, and the reservation/release choreography.
public class AiBudgetServiceTests
{
    private readonly Mock<IAiTurnReservationStore> _store = new();

    private AiBudgetService CreateService() => new(_store.Object);

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreReserves_ReturnsReservation()
    {
        var expected = new TurnReservation(7, PaidWithCredit: false);
        _store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(expected);

        var reserved = await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        Assert.Same(expected, reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreDenies_ReturnsNull()
    {
        _store.Setup(s => s.TryReserveAsync(It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((TurnReservation?)null);

        var reserved = await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        Assert.Null(reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_SignedInUser_ReservesForThatUserWithSignedInDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        _store.Verify(s => s.TryReserveAsync(42, It.IsAny<DateTime>(), 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryReserveTurnAsync_Guest_ReservesInSharedGuestPoolWithGuestDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        _store.Verify(s => s.TryReserveAsync(null, It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseReservationAsync_ReleasesThatTurn()
    {
        await CreateService().ReleaseReservationAsync(new TurnReservation(99, PaidWithCredit: true));

        _store.Verify(s => s.ReleaseAsync(99, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRemainingToday_SignedInUser_CountsOwnTurnsAndReportsCredits()
    {
        _store.Setup(s => s.CountFreeTurnsAsync(42, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(12);
        _store.Setup(s => s.GetCreditBalanceAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(20);

        var snap = await CreateService().GetRemainingToday(42, null);

        Assert.Equal(18, snap.RemainingToday);
        Assert.Equal(30, snap.DailyLimit);
        Assert.Equal(20, snap.Credits);
        Assert.True(snap.IsLoggedIn);
    }

    [Fact]
    public async Task GetRemainingToday_NegativeBalanceFromRefundRace_ShowsZeroCredits()
    {
        _store.Setup(s => s.GetCreditBalanceAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(-1);

        var snap = await CreateService().GetRemainingToday(42, null);

        Assert.Equal(0, snap.Credits);
    }

    [Fact]
    public async Task GetRemainingToday_Guest_UsesSharedPoolAndHasNoCredits()
    {
        _store.Setup(s => s.CountFreeTurnsAsync(null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var snap = await CreateService().GetRemainingToday(null, "guest-key");

        Assert.Equal(6, snap.RemainingToday);
        Assert.Equal(10, snap.DailyLimit);
        Assert.Equal(0, snap.Credits);
        _store.Verify(s => s.GetCreditBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
