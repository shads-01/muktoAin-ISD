using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class CaseServiceTests
{
    private readonly Mock<ICaseRepository> _caseRepo = new();
    private readonly Mock<IRepository<CaseCategory>> _categoryRepo = new();
    private readonly Mock<IRepository<District>> _districtRepo = new();
    private readonly Mock<IEncryptionService> _encryptionService = new();
    private readonly CaseService _service;

    public CaseServiceTests()
    {
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns<string>(s => string.IsNullOrEmpty(s) ? s : $"ENC_{s}");
        _encryptionService.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns<string>(s => s.StartsWith("ENC_") ? s.Substring(4) : s);

        _service = new CaseService(_caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object);
    }

    [Fact]
    public async Task SubmitCaseAsync_EncryptsTitleAndDescriptionBeforeSaving()
    {
        var dto = new CaseSubmissionDto(1, 5, "Secret Title", "Secret Description", "bn", IsAnonymous: false);

        var result = await _service.SubmitCaseAsync(dto, userId: 42);

        _caseRepo.Verify(r => r.AddAsync(It.Is<Case>(c =>
            c.Title == "ENC_Secret Title" &&
            c.Description == "ENC_Secret Description")), Times.Once);
    }

    [Fact]
    public async Task GetCaseDetailAsync_DecryptsTitleAndDescriptionOnRead()
    {
        var caseEntity = new Case
        {
            CaseId = 1,
            Title = "ENC_Encrypted Title",
            Description = "ENC_Encrypted Description",
            CategoryId = 1,
            DistrictId = 1,
            Status = CaseStatus.Submitted,
            UserId = 42,
            IsAnonymous = false
        };
        SetupLookups(caseEntity);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(1)).ReturnsAsync(caseEntity);

        var detail = await _service.GetCaseDetailAsync(1, 42, UserRole.Citizen);

        Assert.NotNull(detail);
        Assert.Equal("Encrypted Title", detail!.Title);
        Assert.Equal("Encrypted Description", detail.Description);
    }

    [Fact]
    public async Task SubmitCaseAsync_IdentifiedCase_HasNoTrackingCodeAndOwnerSet()
    {
        var dto = new CaseSubmissionDto(1, 5, "Title", "Desc", "bn", IsAnonymous: false);

        var result = await _service.SubmitCaseAsync(dto, userId: 42);

        Assert.Null(result.AnonymousTrackingCode);
        _caseRepo.Verify(r => r.AddAsync(It.Is<Case>(c =>
            c.UserId == 42 &&
            !c.IsAnonymous &&
            c.Status == CaseStatus.Submitted)), Times.Once);
        _caseRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SubmitCaseAsync_AnonymousCase_ClearsUserIdAndIssuesTrackingCode()
    {
        var dto = new CaseSubmissionDto(1, 5, "Title", "Desc", "en", IsAnonymous: true);

        var result = await _service.SubmitCaseAsync(dto, userId: 42);

        Assert.False(string.IsNullOrWhiteSpace(result.AnonymousTrackingCode));
        _caseRepo.Verify(r => r.AddAsync(It.Is<Case>(c =>
            c.UserId == null &&
            c.IsAnonymous &&
            c.AnonymousTrackingCode == result.AnonymousTrackingCode)), Times.Once);
    }

    [Fact]
    public async Task TransitionStatus_Submitted_To_Finalized_Should_Fail()
    {
        _caseRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new Case { CaseId = 1, Status = CaseStatus.Submitted });

        var result = await _service.TransitionStatusAsync(1, CaseStatus.Finalized);

        Assert.False(result);
        _caseRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task TransitionStatus_Submitted_To_UnderReview_Succeeds()
    {
        var entity = new Case { CaseId = 1, Status = CaseStatus.Submitted };
        _caseRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(entity);

        var result = await _service.TransitionStatusAsync(1, CaseStatus.UnderReview);

        Assert.True(result);
        Assert.Equal(CaseStatus.UnderReview, entity.Status);
    }

    [Fact]
    public async Task TransitionStatus_Finalized_To_Submitted_ReOpenSucceeds()
    {
        var entity = new Case { CaseId = 1, Status = CaseStatus.Finalized };
        _caseRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(entity);

        var result = await _service.TransitionStatusAsync(1, CaseStatus.Submitted);

        Assert.True(result);
        Assert.Equal(CaseStatus.Submitted, entity.Status);
    }

    [Fact]
    public async Task GetCaseDetailAsync_GuestWithoutMatchingCode_ReturnsNull()
    {
        var anonymous = new Case
        {
            CaseId = 10,
            IsAnonymous = true,
            UserId = null,
            AnonymousTrackingCode = "secret",
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(anonymous);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(10)).ReturnsAsync(anonymous);

        var wrongCode = await _service.GetCaseDetailAsync(10, null, UserRole.Citizen, "wrong");
        var noCode = await _service.GetCaseDetailAsync(10, null, UserRole.Citizen);

        Assert.Null(wrongCode);
        Assert.Null(noCode);
    }

    [Fact]
    public async Task GetCaseDetailAsync_GuestWithCorrectCode_ReturnsAnonymousCase()
    {
        var anonymous = new Case
        {
            CaseId = 10,
            IsAnonymous = true,
            UserId = null,
            AnonymousTrackingCode = "secret",
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(anonymous);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(10)).ReturnsAsync(anonymous);

        var result = await _service.GetCaseDetailAsync(10, null, UserRole.Citizen, "secret");

        Assert.NotNull(result);
        Assert.Equal("Labour", result!.CategoryName);
        Assert.Equal("Dhaka", result.DistrictName);
    }

    [Fact]
    public async Task GetCaseDetailAsync_CitizenCannotReadOthersAnonymousCase()
    {
        var anonymous = new Case
        {
            CaseId = 11,
            IsAnonymous = true,
            UserId = null,
            AnonymousTrackingCode = "secret",
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(anonymous);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(11)).ReturnsAsync(anonymous);

        var result = await _service.GetCaseDetailAsync(11, 42, UserRole.Citizen);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCaseDetailAsync_LawyerCanReadAnyCase()
    {
        var anonymous = new Case
        {
            CaseId = 12,
            IsAnonymous = true,
            UserId = null,
            AnonymousTrackingCode = "secret",
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(anonymous);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(12)).ReturnsAsync(anonymous);

        var lawyerView = await _service.GetCaseDetailAsync(12, null, UserRole.Lawyer);
        var adminView = await _service.GetCaseDetailAsync(12, null, UserRole.Admin);

        Assert.NotNull(lawyerView);
        Assert.NotNull(adminView);
    }

    [Fact]
    public async Task GetCaseDetailAsync_CitizenCanReadOwnNonAnonymousCase()
    {
        var own = new Case
        {
            CaseId = 13,
            IsAnonymous = false,
            UserId = 42,
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(own);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(13)).ReturnsAsync(own);

        var owner = await _service.GetCaseDetailAsync(13, 42, UserRole.Citizen);
        var other = await _service.GetCaseDetailAsync(13, 99, UserRole.Citizen);

        Assert.NotNull(owner);
        Assert.Null(other);
    }

    [Fact]
    public async Task SubmitCaseAsync_GuestNonAnonymous_StillIssuesTrackingCode()
    {
        var dto = new CaseSubmissionDto(1, 5, "Title", "Desc", "bn", IsAnonymous: false);

        var result = await _service.SubmitCaseAsync(dto, userId: null);

        Assert.False(string.IsNullOrWhiteSpace(result.AnonymousTrackingCode));
    }

    [Fact]
    public async Task GetCaseDetailAsync_GuestWithCorrectCode_CanReadOwnGuestSubmission()
    {
        var guest = new Case
        {
            CaseId = 14,
            IsAnonymous = false,
            UserId = null,
            AnonymousTrackingCode = "guest-code",
            Status = CaseStatus.Submitted,
            CategoryId = 1,
            DistrictId = 1
        };
        SetupLookups(guest);
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(14)).ReturnsAsync(guest);

        var result = await _service.GetCaseDetailAsync(14, null, UserRole.Citizen, "guest-code");

        Assert.NotNull(result);
        Assert.Null(await _service.GetCaseDetailAsync(14, null, UserRole.Citizen, "wrong"));
    }

    private void SetupLookups(Case c)
    {
        _categoryRepo.Setup(r => r.GetByIdAsync(c.CategoryId))
            .ReturnsAsync(new CaseCategory { CategoryId = c.CategoryId, Name = "Labour" });
        _districtRepo.Setup(r => r.GetByIdAsync(c.DistrictId))
            .ReturnsAsync(new District { DistrictId = c.DistrictId, Name = "Dhaka" });
    }
}
