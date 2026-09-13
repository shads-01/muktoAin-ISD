using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

// AUD-3: TryReserveTurnAsync now delegates to IAiTurnReservationStore, whose
// single-statement SQL makes the check+insert atomic. These tests pin the
// Application-layer contract: the daily window/limit derivation and the
// reservation/release choreography.
public class AiBudgetServiceTests
{
    private readonly Mock<IRepository<AiLog>> _logRepo = new();
    private readonly Mock<IAiTurnReservationStore> _store = new();

    private AiBudgetService CreateService() => new(_logRepo.Object, _store.Object);

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreReserves_ReturnsTrue()
    {
        _store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var reserved = await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        Assert.True(reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_WhenStoreDeniesQuotaExceeded_ReturnsFalse()
    {
        _store.Setup(s => s.TryReserveAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var reserved = await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        Assert.False(reserved);
    }

    [Fact]
    public async Task TryReserveTurnAsync_SignedInUser_ReservesWithSignedInDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: 42, sessionKey: null);

        _store.Verify(s => s.TryReserveAsync(
            It.IsAny<DateTime>(), 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryReserveTurnAsync_Guest_ReservesWithGuestDailyLimit()
    {
        await CreateService().TryReserveTurnAsync(userId: null, sessionKey: "guest-key");

        _store.Verify(s => s.TryReserveAsync(
            It.IsAny<DateTime>(), 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseReservationAsync_DelegatesToStore()
    {
        await CreateService().ReleaseReservationAsync();

        _store.Verify(s => s.ReleaseOneAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
