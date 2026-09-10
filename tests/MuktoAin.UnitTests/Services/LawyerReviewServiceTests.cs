using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

// Covers LawyerReviewService.GetHistoryAsync -- the "what did I review" page
// (FR-13/23 follow-up: a lawyer's own review history, filterable by decision
// and date range). Queue/Claim/Review/SubmitReview are exercised through the
// controller/integration paths elsewhere; this file is additive.
public class LawyerReviewServiceTests
{
    private readonly Mock<IRepository<GeneratedDocument>> _docRepo = new();
    private readonly Mock<IRepository<LawyerReview>> _reviewRepo = new();
    private readonly Mock<IRepository<LawyerProfile>> _profileRepo = new();
    private readonly Mock<ICaseRepository> _caseRepo = new();
    private readonly Mock<IRepository<CaseCategory>> _categoryRepo = new();
    private readonly Mock<IRepository<District>> _districtRepo = new();
    private readonly Mock<IRepository<CaseActReference>> _refRepo = new();
    private readonly Mock<IRepository<ActSection>> _sectionRepo = new();
    private readonly Mock<IRepository<Act>> _actRepo = new();
    private readonly Mock<IEncryptionService> _encryptionService = new();
    private readonly CaseService _caseService;
    private readonly LawyerReviewService _service;

    public LawyerReviewServiceTests()
    {
        _caseService = new CaseService(
            _caseRepo.Object, _categoryRepo.Object, _districtRepo.Object, _encryptionService.Object);

        _service = new LawyerReviewService(
            _docRepo.Object, _reviewRepo.Object, _profileRepo.Object, _caseRepo.Object,
            _categoryRepo.Object, _districtRepo.Object, _refRepo.Object, _sectionRepo.Object,
            _actRepo.Object, _encryptionService.Object, _caseService);

        _encryptionService.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s);
    }

    private void SetUpDocumentAndCase(
        int documentId, int caseId, string title, int categoryId, string categoryName,
        byte districtId = 1, string districtName = "Dhaka",
        string contentDraft = "draft text", string? contentFinal = null)
    {
        _docRepo.Setup(r => r.GetByIdAsync(documentId))
            .ReturnsAsync(new GeneratedDocument
            {
                DocumentId = documentId, CaseId = caseId,
                ContentDraft = contentDraft, ContentFinal = contentFinal
            });
        _caseRepo.Setup(r => r.GetByIdAsync(caseId))
            .ReturnsAsync(new Case { CaseId = caseId, Title = title, CategoryId = categoryId, DistrictId = districtId });
        _categoryRepo.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(new CaseCategory { CategoryId = categoryId, Name = categoryName });
        _districtRepo.Setup(r => r.GetByIdAsync(districtId))
            .ReturnsAsync(new District { DistrictId = districtId, Name = districtName });
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyReviewsForThatLawyer_NewestFirst()
    {
        SetUpDocumentAndCase(1, 10, "Case A", 1, "Family");
        SetUpDocumentAndCase(2, 20, "Case B", 2, "Labour");
        _reviewRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerReview>
        {
            new() { ReviewId = 1, DocumentId = 1, LawyerProfileId = 5, Decision = ReviewDecision.Approved, Comments = "ok", ReviewedAt = new DateTime(2026, 9, 1) },
            new() { ReviewId = 2, DocumentId = 2, LawyerProfileId = 5, Decision = ReviewDecision.Rejected, Comments = "no", ReviewedAt = new DateTime(2026, 9, 5) },
            new() { ReviewId = 3, DocumentId = 1, LawyerProfileId = 9, Decision = ReviewDecision.Approved, Comments = "other lawyer", ReviewedAt = new DateTime(2026, 9, 9) }
        });

        var result = await _service.GetHistoryAsync(lawyerProfileId: 5);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].ReviewId); // newest first
        Assert.Equal(1, result[1].ReviewId);
        Assert.Equal("Case B", result[0].CaseTitle);
        Assert.Equal("Labour", result[0].CategoryName);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByDecision_WhenGiven()
    {
        SetUpDocumentAndCase(1, 10, "Case A", 1, "Family");
        SetUpDocumentAndCase(2, 20, "Case B", 2, "Labour");
        _reviewRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerReview>
        {
            new() { ReviewId = 1, DocumentId = 1, LawyerProfileId = 5, Decision = ReviewDecision.Approved, Comments = "ok", ReviewedAt = new DateTime(2026, 9, 1) },
            new() { ReviewId = 2, DocumentId = 2, LawyerProfileId = 5, Decision = ReviewDecision.Rejected, Comments = "no", ReviewedAt = new DateTime(2026, 9, 5) }
        });

        var result = await _service.GetHistoryAsync(lawyerProfileId: 5, decisionFilter: "Rejected");

        var item = Assert.Single(result);
        Assert.Equal(ReviewDecision.Rejected, item.Decision);
    }

    [Fact]
    public async Task GetHistoryAsync_NoReviews_ReturnsEmptyList()
    {
        _reviewRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerReview>());

        var result = await _service.GetHistoryAsync(lawyerProfileId: 5);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetHistoryAsync_ApprovedReview_UsesContentFinalAndIncludesDistrict()
    {
        SetUpDocumentAndCase(1, 10, "Case A", 1, "Family",
            districtId: 3, districtName: "Chattogram",
            contentDraft: "original draft", contentFinal: "final approved text");
        _reviewRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerReview>
        {
            new() { ReviewId = 1, DocumentId = 1, LawyerProfileId = 5, Decision = ReviewDecision.Approved, Comments = "ok", ReviewedAt = new DateTime(2026, 9, 1) }
        });

        var result = await _service.GetHistoryAsync(lawyerProfileId: 5);

        var item = Assert.Single(result);
        Assert.Equal("Chattogram", item.DistrictName);
        Assert.Equal("final approved text", item.DocumentText);
    }

    [Fact]
    public async Task GetHistoryAsync_RejectedReview_FallsBackToContentDraft()
    {
        SetUpDocumentAndCase(1, 10, "Case A", 1, "Family",
            contentDraft: "original draft", contentFinal: null);
        _reviewRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<LawyerReview>
        {
            new() { ReviewId = 1, DocumentId = 1, LawyerProfileId = 5, Decision = ReviewDecision.Rejected, Comments = "no", ReviewedAt = new DateTime(2026, 9, 1) }
        });

        var result = await _service.GetHistoryAsync(lawyerProfileId: 5);

        var item = Assert.Single(result);
        Assert.Equal("original draft", item.DocumentText);
    }
}
