using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class LawyerVerificationServiceTests
{
    private readonly Mock<IRepository<LawyerProfile>> _profileRepo = new();
    private readonly LawyerVerificationService _service;

    public LawyerVerificationServiceTests()
    {
        _service = new LawyerVerificationService(_profileRepo.Object);
    }

    [Fact]
    public async Task ApplyAsync_CreatesPendingProfile_AndReturnsId()
    {
        _profileRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerProfile>());
        var captured = new List<LawyerProfile>();
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<LawyerProfile>()))
            .Callback<LawyerProfile>(p =>
            {
                p.LawyerProfileId = 7;
                captured.Add(p);
            })
            .Returns(Task.CompletedTask);

        var id = await _service.ApplyAsync(42, new LawyerApplicationDto("BAR-123", "Labour law"));

        Assert.Equal(7, id);
        var profile = Assert.Single(captured);
        Assert.Equal(VerificationStatus.Pending, profile.VerificationStatus);
        Assert.Equal("BAR-123", profile.BarRegistrationNumber);
    }

    [Fact]
    public async Task ApplyAsync_DuplicateApplication_Throws()
    {
        _profileRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerProfile>
        {
            new() { LawyerProfileId = 1, UserId = 42 }
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ApplyAsync(42, new LawyerApplicationDto("BAR-999", null)));
    }

    [Fact]
    public async Task VerifyAsync_Approve_SetsStatusAdminAndTimestamp()
    {
        var profile = new LawyerProfile { LawyerProfileId = 3, UserId = 42 };
        _profileRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(profile);

        await _service.VerifyAsync(3, adminUserId: 1, approve: true);

        Assert.Equal(VerificationStatus.Approved, profile.VerificationStatus);
        Assert.Equal(1, profile.VerifiedByAdminId);
        Assert.NotNull(profile.VerifiedAt);
        _profileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_Reject_SetsRejectedStatus()
    {
        var profile = new LawyerProfile { LawyerProfileId = 4, UserId = 42 };
        _profileRepo.Setup(r => r.GetByIdAsync(4)).ReturnsAsync(profile);

        await _service.VerifyAsync(4, adminUserId: 1, approve: false);

        Assert.Equal(VerificationStatus.Rejected, profile.VerificationStatus);
    }

    [Fact]
    public async Task VerifyAsync_UnknownProfile_Throws()
    {
        _profileRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((LawyerProfile?)null);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.VerifyAsync(99, adminUserId: 1, approve: true));
    }

    [Fact]
    public async Task GetPendingApplicationsAsync_ReturnsOnlyPending()
    {
        _profileRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerProfile>
        {
            new() { LawyerProfileId = 1, VerificationStatus = VerificationStatus.Pending },
            new() { LawyerProfileId = 2, VerificationStatus = VerificationStatus.Approved },
            new() { LawyerProfileId = 3, VerificationStatus = VerificationStatus.Rejected }
        });

        var pending = (await _service.GetPendingApplicationsAsync()).ToList();

        var profile = Assert.Single(pending);
        Assert.Equal(1, profile.LawyerProfileId);
    }
}
