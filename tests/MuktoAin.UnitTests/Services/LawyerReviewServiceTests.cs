using MuktoAin.Application.DTOs;
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

    // ── GetQueueAsync Tests ──────────────────────────────────────────────

    [Fact]
    public async Task GetQueueAsync_ReturnsUnderReviewDocuments_OldestFirst()
    {
        var doc1 = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            CreatedAt = new DateTime(2026, 9, 1, 10, 0, 0)
        };
        var doc2 = new GeneratedDocument
        {
            DocumentId = 2, CaseId = 20, Status = DocumentStatus.UnderReview,
            CreatedAt = new DateTime(2026, 8, 25, 10, 0, 0)
        };
        var docDraft = new GeneratedDocument
        {
            DocumentId = 3, CaseId = 30, Status = DocumentStatus.Draft,
            CreatedAt = new DateTime(2026, 8, 20, 10, 0, 0)
        };

        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<GeneratedDocument> { doc1, doc2, docDraft });
        SetUpCase(10, "Case A", 1, "Labour", 1, "Dhaka");
        SetUpCase(20, "Case B", 2, "General Diary", 2, "Gazipur");

        var queue = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "All");

        Assert.Equal(2, queue.Count);
        Assert.Equal(2, queue[0].DocumentId); // Oldest first
        Assert.Equal(1, queue[1].DocumentId);
    }

    [Fact]
    public async Task GetQueueAsync_FilterUnclaimed_ReturnsOnlyUnassignedDocuments()
    {
        var docUnclaimed = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = null, CreatedAt = DateTime.UtcNow
        };
        var docClaimed = new GeneratedDocument
        {
            DocumentId = 2, CaseId = 20, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 99, CreatedAt = DateTime.UtcNow
        };

        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<GeneratedDocument> { docUnclaimed, docClaimed });
        SetUpCase(10, "Case A", 1, "Labour", 1, "Dhaka");
        SetUpCase(20, "Case B", 2, "General Diary", 2, "Gazipur");

        var queue = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "Unclaimed");

        var item = Assert.Single(queue);
        Assert.Equal(1, item.DocumentId);
    }

    [Fact]
    public async Task GetQueueAsync_FilterMine_ReturnsOnlyDocumentsAssignedToCallingLawyer()
    {
        var docMine = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 5, CreatedAt = DateTime.UtcNow
        };
        var docOther = new GeneratedDocument
        {
            DocumentId = 2, CaseId = 20, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 99, CreatedAt = DateTime.UtcNow
        };

        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<GeneratedDocument> { docMine, docOther });
        SetUpCase(10, "Case A", 1, "Labour", 1, "Dhaka");
        SetUpCase(20, "Case B", 2, "General Diary", 2, "Gazipur");

        var queue = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "Mine");

        var item = Assert.Single(queue);
        Assert.Equal(1, item.DocumentId);
    }

    [Fact]
    public async Task GetQueueAsync_CanOpen_IsTrueForUnclaimedAndOwnClaim_IsFalseForOtherLawyerClaim()
    {
        var docUnclaimed = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = null, CreatedAt = DateTime.UtcNow
        };
        var docMine = new GeneratedDocument
        {
            DocumentId = 2, CaseId = 20, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 5, CreatedAt = DateTime.UtcNow
        };
        var docOther = new GeneratedDocument
        {
            DocumentId = 3, CaseId = 30, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 99, CreatedAt = DateTime.UtcNow
        };

        _docRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<GeneratedDocument> { docUnclaimed, docMine, docOther });
        SetUpCase(10, "Case A", 1, "Labour", 1, "Dhaka");
        SetUpCase(20, "Case B", 2, "General Diary", 2, "Gazipur");
        SetUpCase(30, "Case C", 3, "RTI", 3, "Sylhet");
        _profileRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync(new LawyerProfile { LawyerProfileId = 99, BarRegistrationNumber = "BAR-99" });

        var queue = await _service.GetQueueAsync(lawyerProfileId: 5, filter: "All");

        Assert.Equal(3, queue.Count);
        Assert.True(queue.Single(q => q.DocumentId == 1).CanOpen);
        Assert.True(queue.Single(q => q.DocumentId == 2).CanOpen);
        Assert.False(queue.Single(q => q.DocumentId == 3).CanOpen);
    }

    // ── ClaimAsync Security Edge-Case Tests ──────────────────────────────

    [Fact]
    public async Task ClaimAsync_UnclaimedDocument_SucceedsAndSetsAssignedLawyerAndClaimedAt()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1,
            Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = null
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
        _docRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var result = await _service.ClaimAsync(documentId: 1, lawyerProfileId: 5);

        Assert.True(result);
        Assert.Equal(5, doc.AssignedLawyerProfileId);
        Assert.NotNull(doc.ClaimedAt);
        _docRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ClaimAsync_AlreadyClaimedBySameLawyer_Succeeds()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1,
            Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 5,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);

        var result = await _service.ClaimAsync(documentId: 1, lawyerProfileId: 5);

        Assert.True(result);
        Assert.Equal(5, doc.AssignedLawyerProfileId);
        _docRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ClaimAsync_AlreadyClaimedByAnotherLawyer_ReturnsFalseAndDoesNotOverwrite()
    {
        var originalClaimedAt = DateTime.UtcNow.AddMinutes(-30);
        var doc = new GeneratedDocument
        {
            DocumentId = 1,
            Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 99,
            ClaimedAt = originalClaimedAt
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);

        var result = await _service.ClaimAsync(documentId: 1, lawyerProfileId: 5);

        Assert.False(result);
        Assert.Equal(99, doc.AssignedLawyerProfileId);
        Assert.Equal(originalClaimedAt, doc.ClaimedAt);
        _docRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ClaimAsync_DocumentNotInUnderReviewStatus_ReturnsFalse()
    {
        var docDraft = new GeneratedDocument { DocumentId = 1, Status = DocumentStatus.Draft };
        var docApproved = new GeneratedDocument { DocumentId = 2, Status = DocumentStatus.Approved };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(docDraft);
        _docRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(docApproved);

        Assert.False(await _service.ClaimAsync(1, 5));
        Assert.False(await _service.ClaimAsync(2, 5));
        _docRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task ClaimAsync_NonExistentDocument_ReturnsFalse()
    {
        _docRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _service.ClaimAsync(404, 5);

        Assert.False(result);
    }

    // ── GetForReviewAsync Tests ──────────────────────────────────────────

    [Fact]
    public async Task GetForReviewAsync_ExistingDocument_ReturnsFullWorkspaceWithCitations()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, ContentDraft = "AI Draft",
            ContentFinal = null, VersionNo = 1, CitizenEdited = false
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
        SetUpCase(10, "ENC_Title", 1, "Labour", 1, "Dhaka", "ENC_Description");

        _refRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<CaseActReference>
        {
            new() { CaseActReferenceId = 1, CaseId = 10, SectionId = 100, RelevanceScore = 0.95m, RetrievalMethod = RetrievalMethod.Vector }
        });
        _sectionRepo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync(new ActSection { SectionId = 100, ActId = 50, SectionNumber = "Section 33", SectionText = "Wages payment" });
        _actRepo.Setup(r => r.GetByIdAsync(50)).ReturnsAsync(new Act { ActId = 50, Title = "Labour Act 2006", ActNumber = "XLII", Year = 2006 });

        var workspace = await _service.GetForReviewAsync(1);

        Assert.NotNull(workspace);
        Assert.Equal(1, workspace!.DocumentId);
        Assert.Equal(10, workspace.CaseId);
        Assert.Equal("ENC_Title", workspace.CaseTitle);
        Assert.Equal("Labour", workspace.CategoryName);
        Assert.Equal("Dhaka", workspace.DistrictName);
        Assert.Equal("ENC_Description", workspace.CitizenNarrative);
        Assert.Equal("AI Draft", workspace.OriginalDraft);
        var citation = Assert.Single(workspace.Citations);
        Assert.Equal("Labour Act 2006", citation.ActTitle);
        Assert.Equal("Section 33", citation.SectionNumber);
    }

    [Fact]
    public async Task GetForReviewAsync_NonExistentDocument_ReturnsNull()
    {
        _docRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _service.GetForReviewAsync(404);

        Assert.Null(result);
    }

    // ── SubmitReviewAsync Business Logic & Security Edge-Case Tests ──────

    [Fact]
    public async Task SubmitReviewAsync_Approved_SetsStatusApproved_FinalEqualsDraft_TransitionsCaseFinalized()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            ContentDraft = "AI Draft Content", ContentFinal = null, AssignedLawyerProfileId = 5
        };
        var caseEntity = new Case { CaseId = 10, Status = CaseStatus.UnderReview };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(caseEntity);
        _reviewRepo.Setup(r => r.AddAsync(It.IsAny<LawyerReview>())).Returns(Task.CompletedTask);
        _reviewRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _docRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _caseRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.Approved,
            Comments: "Legally sound and accurate", EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.True(result);
        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal("AI Draft Content", doc.ContentFinal);
        Assert.Equal(CaseStatus.Finalized, caseEntity.Status);
        Assert.True(caseEntity.HasUnreadActivity);
        _reviewRepo.Verify(r => r.AddAsync(It.Is<LawyerReview>(rev =>
            rev.DocumentId == 1 && rev.LawyerProfileId == 5 && rev.Decision == ReviewDecision.Approved)), Times.Once);
    }

    [Fact]
    public async Task SubmitReviewAsync_EditedApproved_SetsStatusApproved_PreservesDraft_SetsFinalToEdited()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            ContentDraft = "Original AI Draft", ContentFinal = null, AssignedLawyerProfileId = 5
        };
        var caseEntity = new Case { CaseId = 10, Status = CaseStatus.UnderReview };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(caseEntity);

        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.EditedApproved,
            Comments: "Corrected employer name", EditedContent: "Lawyer modified finalized draft");

        var result = await _service.SubmitReviewAsync(dto);

        Assert.True(result);
        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal("Original AI Draft", doc.ContentDraft); // Immutable
        Assert.Equal("Lawyer modified finalized draft", doc.ContentFinal);
        Assert.Equal(CaseStatus.Finalized, caseEntity.Status);
        Assert.True(caseEntity.HasUnreadActivity);
    }

    [Fact]
    public async Task SubmitReviewAsync_Rejected_SetsStatusRejected_NullsContentFinal_KeepsCaseUnderReview()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            ContentDraft = "AI Draft", ContentFinal = null, AssignedLawyerProfileId = 5
        };
        var caseEntity = new Case { CaseId = 10, Status = CaseStatus.UnderReview };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);
        _caseRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(caseEntity);

        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.Rejected,
            Comments: "Missing incident date and location", EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.True(result);
        Assert.Equal(DocumentStatus.Rejected, doc.Status);
        Assert.Null(doc.ContentFinal);
        Assert.True(caseEntity.HasUnreadActivity);
        _reviewRepo.Verify(r => r.AddAsync(It.Is<LawyerReview>(rev => rev.Decision == ReviewDecision.Rejected)), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitReviewAsync_EmptyOrWhitespaceComments_FailsValidation(string? invalidComments)
    {
        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.Approved,
            Comments: invalidComments!, EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.False(result);
        _reviewRepo.Verify(r => r.AddAsync(It.IsAny<LawyerReview>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitReviewAsync_EditedApprovedWithEmptyEditedContent_FailsValidation(string? invalidEditedContent)
    {
        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.EditedApproved,
            Comments: "Valid comment", EditedContent: invalidEditedContent);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.False(result);
        _reviewRepo.Verify(r => r.AddAsync(It.IsAny<LawyerReview>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_ClaimedByAnotherLawyer_FailsOwnershipGuard()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.UnderReview,
            AssignedLawyerProfileId = 99 // Assigned to Lawyer 99
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);

        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.Approved, // Lawyer 5 trying to review
            Comments: "Unauthorized review", EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.False(result);
        _reviewRepo.Verify(r => r.AddAsync(It.IsAny<LawyerReview>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_DocumentNotInUnderReviewStatus_FailsStatusGuard()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 1, CaseId = 10, Status = DocumentStatus.Draft, // Still in Draft
            AssignedLawyerProfileId = 5
        };
        _docRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(doc);

        var dto = new SubmitReviewDto(
            DocumentId: 1, LawyerProfileId: 5, Decision: ReviewDecision.Approved,
            Comments: "Premature review", EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.False(result);
        _reviewRepo.Verify(r => r.AddAsync(It.IsAny<LawyerReview>()), Times.Never);
    }

    [Fact]
    public async Task SubmitReviewAsync_NonExistentDocument_ReturnsFalse()
    {
        _docRepo.Setup(r => r.GetByIdAsync(404)).ReturnsAsync((GeneratedDocument?)null);

        var dto = new SubmitReviewDto(
            DocumentId: 404, LawyerProfileId: 5, Decision: ReviewDecision.Approved,
            Comments: "Valid comment", EditedContent: null);

        var result = await _service.SubmitReviewAsync(dto);

        Assert.False(result);
    }

    private void SetUpCase(
        int caseId, string title, int categoryId, string categoryName,
        byte districtId, string districtName, string description = "description text")
    {
        var caseEntity = new Case
        {
            CaseId = caseId, Title = title, Description = description,
            CategoryId = categoryId, DistrictId = districtId, Status = CaseStatus.UnderReview
        };
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(caseId)).ReturnsAsync(caseEntity);
        _caseRepo.Setup(r => r.GetByIdAsync(caseId)).ReturnsAsync(caseEntity);
        _categoryRepo.Setup(r => r.GetByIdAsync(categoryId))
            .ReturnsAsync(new CaseCategory { CategoryId = categoryId, Name = categoryName });
        _districtRepo.Setup(r => r.GetByIdAsync(districtId))
            .ReturnsAsync(new District { DistrictId = districtId, Name = districtName });
    }
}
