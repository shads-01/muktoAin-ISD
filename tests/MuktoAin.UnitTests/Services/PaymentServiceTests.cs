using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

// FR-24: covers the honorarium order's lawyer attribution, which is what
// GetLawyerEarningsAsync later filters on for the lawyer's Payments page.
// Bug: CreateHonorariumOrderAsync used to fetch the case via the generic
// IRepository<Case>.GetByIdAsync (plain DbSet.FindAsync, no .Include), so
// Case.Documents came back empty and AssignedLawyerProfileId was always
// null -- no honorarium ever showed up for any lawyer. Fixed by switching
// to ICaseRepository.GetWithDocumentsAsync, which eager-loads Documents.
public class PaymentServiceTests
{
    private readonly Mock<IRepository<PaymentOrder>> _orderRepo = new();
    private readonly Mock<IRepository<PayoutRequest>> _payoutRepo = new();
    private readonly Mock<IRepository<LawyerProfile>> _lawyerRepo = new();
    private readonly Mock<ICaseRepository> _caseRepo = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(
            _orderRepo.Object, _payoutRepo.Object, _lawyerRepo.Object, _caseRepo.Object, NewUserManager());
    }

    private static UserManager<User> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new UserManager<User>(store.Object, Mock.Of<IOptions<IdentityOptions>>(),
            new PasswordHasher<User>(), Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(), new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(), null!, Mock.Of<Microsoft.Extensions.Logging.ILogger<UserManager<User>>>());
    }

    [Fact]
    public async Task CreateHonorariumOrderAsync_ResolvesLawyerFromCasesClaimedDocument()
    {
        var doc = new GeneratedDocument { DocumentId = 1, CaseId = 5, AssignedLawyerProfileId = 42 };
        var c = new Case { CaseId = 5, Documents = new List<GeneratedDocument> { doc } };
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(5)).ReturnsAsync(c);

        var order = await _service.CreateHonorariumOrderAsync(caseId: 5, userId: 7, amount: 1000m);

        Assert.Equal(42, order.LawyerProfileId);
    }
}
