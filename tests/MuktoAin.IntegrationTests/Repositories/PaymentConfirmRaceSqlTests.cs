using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// PAYMENT_ORDER.RowVersion (scripts/18_payment_gateway.sql) on real SQL
// Server, which EF InMemory can't model. Two DbContexts stand in for two
// success callbacks for the same order arriving together (double submit,
// refresh): exactly one marks it Paid and notifies the lawyer.
[Collection("MuktoAinSqlDb")]
public class PaymentConfirmRaceSqlTests
{
    private const string ValId = "val-race";
    private readonly SqlDatabaseFixture _fx;

    public PaymentConfirmRaceSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Stale_PaymentOrder_Save_Throws_ConcurrencyConflictException()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (orderId, _, _) = await SeedPendingHonorariumAsync();

        await using var ctxA = _fx.CreateContext();
        await using var ctxB = _fx.CreateContext();
        var a = (await new Repository<PaymentOrder>(ctxA).GetByIdAsync(orderId))!;
        var repoB = new Repository<PaymentOrder>(ctxB);
        var b = (await repoB.GetByIdAsync(orderId))!;

        a.Status = PaymentStatus.Paid;
        await ctxA.SaveChangesAsync();

        b.Status = PaymentStatus.Failed;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repoB.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Two_Racing_Confirmations_Pay_Once_And_Notify_Once()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (orderId, tranId, lawyerUserId) = await SeedPendingHonorariumAsync();
        var gateway = new Mock<IPaymentGatewayClient>();
        gateway.Setup(g => g.ValidateAsync(ValId)).ReturnsAsync(
            new GatewayValidationResult(true, tranId, "BKS-RACE", "VALID", 500m, null));

        await using var ctxA = _fx.CreateContext();
        await using var ctxB = _fx.CreateContext();

        // Callback B has already read the Pending order...
        await ctxB.PaymentOrders.SingleAsync(o => o.PaymentOrderId == orderId);

        // ...when callback A confirms it.
        Assert.True(await CreateService(ctxA, gateway.Object).ConfirmPaymentAsync(orderId, ValId));

        // B's stale copy still says Pending, so it validates and tries to mark
        // Paid; the rowversion check stops the second write.
        Assert.True(await CreateService(ctxB, gateway.Object).ConfirmPaymentAsync(orderId, ValId));

        await using var verify = _fx.CreateContext();
        var stored = await verify.PaymentOrders.SingleAsync(o => o.PaymentOrderId == orderId);
        Assert.Equal(PaymentStatus.Paid, stored.Status);
        Assert.Equal("BKS-RACE", stored.GatewayRef);
        Assert.True((await verify.Cases.SingleAsync(c => c.CaseId == stored.CaseId)).HonorariumPaid);
        Assert.Equal(1, await verify.Notifications.CountAsync(n =>
            n.UserId == lawyerUserId && n.Type == NotificationType.PaymentReceived));
    }

    [SkippableFact]
    public async Task TransactionId_Is_Unique()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, tranId, _) = await SeedPendingHonorariumAsync();

        await using var ctx = _fx.CreateContext();
        ctx.PaymentOrders.Add(new PaymentOrder
        {
            Purpose = PaymentPurpose.TopUp, Status = PaymentStatus.Pending, Amount = 1m,
            TransactionId = tranId, CreatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    private static PaymentService CreateService(AppDbContext ctx, IPaymentGatewayClient gateway) =>
        new(new Repository<PaymentOrder>(ctx),
            new Repository<PayoutRequest>(ctx),
            new Repository<LawyerProfile>(ctx),
            new CaseRepository(ctx),
            new UserManager<User>(Mock.Of<IUserStore<User>>(), Options.Create(new IdentityOptions()),
                new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
                Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(), null!,
                Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>()),
            Mock.Of<IAdminAuditService>(),
            new Repository<Notification>(ctx),
            Mock.Of<IPaymentGatewayResolver>(r => r.Get(It.IsAny<PaymentGateway>()) == gateway),
            new AiTurnReservationStore(ctx));

    private async Task<(int OrderId, string TranId, int LawyerUserId)> SeedPendingHonorariumAsync()
    {
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        await using var ctx = _fx.CreateContext();
        var profile = new LawyerProfile
        {
            User = new User
            {
                FullName = "Race Payee",
                Email = $"payee-{Guid.NewGuid():N}@test.local",
                Role = UserRole.Lawyer,
            },
            BarRegistrationNumber = $"PAY-{Guid.NewGuid():N}"[..20],
            VerificationStatus = VerificationStatus.Approved,
        };
        var c = new Case
        {
            CategoryId = categoryId,
            DistrictId = (byte)districtId,
            Title = "Payment race test",
            Description = "Seeded by the payment race tests",
            Language = "en",
            Status = CaseStatus.UnderReview,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var tranId = $"MA-RACE-{Guid.NewGuid():N}";
        var order = new PaymentOrder
        {
            Case = c,
            LawyerProfile = profile,
            Purpose = PaymentPurpose.Honorarium,
            Status = PaymentStatus.Pending,
            Amount = 500m,
            Commission = 50m,
            TransactionId = tranId,
            CreatedAt = now,
        };
        ctx.PaymentOrders.Add(order);
        await ctx.SaveChangesAsync();
        return (order.PaymentOrderId, tranId, profile.UserId);
    }
}
