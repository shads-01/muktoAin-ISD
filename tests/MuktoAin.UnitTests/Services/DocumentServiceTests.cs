using Moq;
using MuktoAin.Application.Documents;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentTemplate> _mockTemplate;
    private readonly DocumentGenerator _generator;
    private readonly Mock<IRepository<GeneratedDocument>> _mockDocRepo;
    private readonly Mock<ICaseRepository> _mockCaseRepo;
    private readonly Mock<IRepository<District>> _mockDistrictRepo;
    private readonly Mock<IRepository<CaseCategory>> _mockCategoryRepo;
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _mockTemplate = new Mock<IDocumentTemplate>();
        _mockTemplate.Setup(t => t.DocumentType).Returns(DocumentType.LabourComplaint);
        _mockTemplate.Setup(t => t.RenderAsync(It.IsAny<Case>(), It.IsAny<RightsExplanationDto>()))
            .ReturnsAsync("Rendered Draft Content");

        _generator = new DocumentGenerator(new[] { _mockTemplate.Object });
        _mockDocRepo = new Mock<IRepository<GeneratedDocument>>();
        _mockCaseRepo = new Mock<ICaseRepository>();
        _mockDistrictRepo = new Mock<IRepository<District>>();
        _mockCategoryRepo = new Mock<IRepository<CaseCategory>>();

        _service = new DocumentService(
            _generator,
            _mockDocRepo.Object,
            _mockCaseRepo.Object,
            _mockDistrictRepo.Object,
            _mockCategoryRepo.Object);
    }

    [Fact]
    public async Task GenerateDocumentAsync_ValidCase_CreatesDraftAndPersists()
    {
        var caseEntity = new Case
        {
            CaseId = 42,
            CategoryId = 1,
            DistrictId = 5,
            Description = "Case details"
        };
        var district = new District { DistrictId = 5, Name = "Chittagong" };
        var category = new CaseCategory { CategoryId = 1, Name = "Labour Complaint" };

        _mockCaseRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(caseEntity);
        _mockDistrictRepo.Setup(r => r.GetByIdAsync((byte)5)).ReturnsAsync(district);
        _mockCategoryRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(category);

        GeneratedDocument? savedDoc = null;
        _mockDocRepo.Setup(r => r.AddAsync(It.IsAny<GeneratedDocument>()))
            .Callback<GeneratedDocument>(d => { d.DocumentId = 100; savedDoc = d; })
            .Returns(Task.CompletedTask);
        _mockDocRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var explanation = new RightsExplanationDto("Explanation", Array.Empty<CitedSectionDto>(), "Disclaimer");

        var result = await _service.GenerateDocumentAsync(42, explanation);

        Assert.NotNull(result);
        Assert.Equal(42, result.CaseId);
        Assert.Equal("LabourComplaint", result.DocumentType);
        Assert.Equal("Rendered Draft Content", result.ContentDraft);
        Assert.Equal("Draft", result.Status);

        Assert.NotNull(savedDoc);
        Assert.Equal("Rendered Draft Content", savedDoc.ContentDraft);
        Assert.Null(savedDoc.ContentFinal); // Invariant: ContentFinal is null until review
        Assert.Equal(DocumentStatus.Draft, savedDoc.Status);
        Assert.Equal(DocumentType.LabourComplaint, savedDoc.DocumentType);

        _mockDocRepo.Verify(r => r.AddAsync(It.IsAny<GeneratedDocument>()), Times.Once);
        _mockDocRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GenerateDocumentAsync_CaseNotFound_ThrowsArgumentException()
    {
        _mockCaseRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Case?)null);
        var explanation = new RightsExplanationDto("Explanation", Array.Empty<CitedSectionDto>(), "Disclaimer");

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GenerateDocumentAsync(999, explanation));
    }

    [Fact]
    public async Task GetDocumentAsync_ExistingDocument_ReturnsMappedDto()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 7,
            CaseId = 3,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "Draft preview",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _mockDocRepo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync(doc);

        var result = await _service.GetDocumentAsync(7);

        Assert.NotNull(result);
        Assert.Equal(7, result.DocumentId);
        Assert.Equal(3, result.CaseId);
        Assert.Equal("LabourComplaint", result.DocumentType);
        Assert.Equal("Draft preview", result.ContentDraft);
        Assert.Equal("Draft", result.Status);
    }

    [Fact]
    public async Task GetDocumentAsync_NotFound_ReturnsNull()
    {
        _mockDocRepo.Setup(r => r.GetByIdAsync(888)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _service.GetDocumentAsync(888);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_ApprovedWithoutEdits_SetsFinalEqualToDraft()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            ContentDraft = "Original AI Draft",
            ContentFinal = null,
            Status = DocumentStatus.UnderReview
        };
        _mockDocRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);
        _mockDocRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        await _service.UpdateStatusAsync(10, DocumentStatus.Approved, editedContent: null);

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal("Original AI Draft", doc.ContentDraft); // Immutable
        Assert.Equal("Original AI Draft", doc.ContentFinal); // Set to draft
        _mockDocRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_EditedApproved_PreservesDraftAndSetsFinal()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 11,
            ContentDraft = "Original AI Draft",
            ContentFinal = null,
            Status = DocumentStatus.UnderReview
        };
        _mockDocRepo.Setup(r => r.GetByIdAsync(11)).ReturnsAsync(doc);
        _mockDocRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        await _service.UpdateStatusAsync(11, DocumentStatus.Approved, editedContent: "Lawyer modified content");

        Assert.Equal(DocumentStatus.Approved, doc.Status);
        Assert.Equal("Original AI Draft", doc.ContentDraft); // Immutable AI original preserved
        Assert.Equal("Lawyer modified content", doc.ContentFinal);
        _mockDocRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_Rejected_SetsStatusWithoutModifyingContent()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 12,
            ContentDraft = "Original AI Draft",
            ContentFinal = null,
            Status = DocumentStatus.UnderReview
        };
        _mockDocRepo.Setup(r => r.GetByIdAsync(12)).ReturnsAsync(doc);
        _mockDocRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        await _service.UpdateStatusAsync(12, DocumentStatus.Rejected, editedContent: null);

        Assert.Equal(DocumentStatus.Rejected, doc.Status);
        Assert.Equal("Original AI Draft", doc.ContentDraft);
        Assert.Null(doc.ContentFinal);
        _mockDocRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
