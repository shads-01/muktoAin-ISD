using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class LawyerQueueNotifierTests
{
    private const int Labour = 1, GeneralDiary = 2, Rti = 3, Consumer = 4;

    private readonly Mock<IRepository<LawyerProfile>> _profiles = new();
    private readonly Mock<IRepository<Notification>> _notifications = new();
    private readonly List<Notification> _added = new();
    private readonly LawyerQueueNotifier _notifier;

    public LawyerQueueNotifierTests()
    {
        _notifications.Setup(r => r.AddAsync(It.IsAny<Notification>()))
            .Callback<Notification>(_added.Add)
            .Returns(Task.CompletedTask);
        _notifier = new LawyerQueueNotifier(_profiles.Object, _notifications.Object);
    }

    private static LawyerProfile Lawyer(int id, string? spec, VerificationStatus status = VerificationStatus.Approved)
        => new() { LawyerProfileId = id, UserId = 100 + id, Specialization = spec, VerificationStatus = status };

    [Theory]
    [InlineData("Labour", Labour, true)]
    [InlineData("Employment & labor disputes", Labour, true)]
    [InlineData("শ্রম আইন", Labour, true)]
    [InlineData("Criminal Law, Family Law", GeneralDiary, true)]
    [InlineData("Criminal Law, Family Law", Labour, false)]
    [InlineData("Consumer Rights", Consumer, true)]
    [InlineData("Right to Information", Rti, true)]
    [InlineData("Property law", Rti, false)]
    [InlineData("সাধারণ আইন / General Law", Consumer, true)]
    [InlineData(null, Labour, false)]
    public void MatchesCategory_UsesKeywords(string? specialization, int categoryId, bool expected)
        => Assert.Equal(expected, LawyerQueueNotifier.MatchesCategory(specialization, categoryId));

    [Fact]
    public async Task NewDocument_NotifiesOnlyVerifiedMatchingLawyers()
    {
        _profiles.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            Lawyer(1, "Labour"),
            Lawyer(2, "Labour law", VerificationStatus.Pending), // not verified
            Lawyer(3, "Criminal Law"),
            Lawyer(4, null)
        });

        await _notifier.NotifyDocumentQueuedAsync(caseId: 9, documentId: 50, categoryId: Labour, assignedLawyerProfileId: null);

        var n = Assert.Single(_added);
        Assert.Equal(101, n.UserId);
        Assert.Equal(NotificationType.NewCaseInQueue, n.Type);
        Assert.Equal(50, n.RelatedDocumentId);
        _notifications.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task NewDocument_NoMatch_FallsBackToAllVerifiedLawyers()
    {
        _profiles.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            Lawyer(1, "Labour"),
            Lawyer(3, "Criminal Law"),
            Lawyer(4, null),
            Lawyer(5, "Consumer Rights", VerificationStatus.Rejected)
        });

        await _notifier.NotifyDocumentQueuedAsync(9, 50, categoryId: Rti, assignedLawyerProfileId: null);

        Assert.Equal(new[] { 101, 103, 104 }, _added.Select(n => n.UserId).OrderBy(x => x));
    }

    [Fact]
    public async Task Resubmission_NotifiesOnlyTheLawyerHoldingTheDocument()
    {
        _profiles.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { Lawyer(1, "Labour"), Lawyer(3, "Labour") });

        await _notifier.NotifyDocumentQueuedAsync(9, 50, categoryId: Labour, assignedLawyerProfileId: 3);

        var n = Assert.Single(_added);
        Assert.Equal(103, n.UserId);
        Assert.Equal(NotificationType.DocumentResubmitted, n.Type);
    }

    [Fact]
    public async Task RepositoryFailure_IsSwallowed()
    {
        _profiles.Setup(r => r.GetAllAsync()).ThrowsAsync(new InvalidOperationException("db down"));

        await _notifier.NotifyDocumentQueuedAsync(9, 50, Labour, null);
    }
}
