using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class DocumentControllerTests
{
    private readonly Mock<IRepository<GeneratedDocument>> _docRepo;
    private readonly Mock<IDocumentTranslationService> _translationService;
    private readonly DocumentController _controller;

    public DocumentControllerTests()
    {
        _docRepo = new Mock<IRepository<GeneratedDocument>>();
        _translationService = new Mock<IDocumentTranslationService>();
        var httpContext = new DefaultHttpContext();
        _controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            documentService: null,
            translationService: _translationService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task Preview_WhenInvalidId_ReturnsNotFound()
    {
        var result = await _controller.Preview(0);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Preview_WhenDocumentFoundInRepo_ReturnsViewWithDocumentData()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "Draft complaint text",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);

        var result = await _controller.Preview(10);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DocumentPreviewViewModel>(view.Model);
        Assert.Equal(10, model.DocumentId);
        Assert.Equal(42, model.CaseId);
        Assert.False(model.CanDownloadPdf);
        Assert.Equal("Draft", model.Status);
    }

    [Fact]
    public async Task Preview_WhenApproved_AllowsPdfDownload()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 15,
            CaseId = 42,
            DocumentType = DocumentType.GeneralDiary,
            ContentDraft = "Draft GD",
            ContentFinal = "Approved GD with advocate edits",
            Status = DocumentStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };

        _docRepo.Setup(r => r.GetByIdAsync(15)).ReturnsAsync(doc);

        var result = await _controller.Preview(15);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DocumentPreviewViewModel>(view.Model);
        Assert.True(model.CanDownloadPdf);
        Assert.Equal("Approved GD with advocate edits", model.ContentFinal);
    }

    [Fact]
    public async Task Download_WhenNotApproved_BlocksDownloadAndRedirectsWithWarning()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 20,
            CaseId = 55,
            Status = DocumentStatus.UnderReview
        };

        _docRepo.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(doc);

        var result = await _controller.Download(20);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(DocumentController.Preview), redirect.ActionName);
        Assert.True(_controller.TempData.ContainsKey("Error"));
    }

    [Fact]
    public async Task Download_WhenInvalidId_ReturnsNotFound()
    {
        var result = await _controller.Download(-1);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Translate_InvalidId_ReturnsBadRequest()
    {
        var result = await _controller.Translate(0, "en");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Translate_InvalidLanguage_ReturnsBadRequest()
    {
        var result = await _controller.Translate(10, "fr");
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Translate_DocumentNotFound_ReturnsNotFound()
    {
        _docRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _controller.Translate(99, "en");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Translate_SameAsSourceLanguage_ReturnsOriginalWithoutCallingTranslationService()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);

        var result = await _controller.Translate(10, "bn");

        var ok = Assert.IsType<OkObjectResult>(result);
        _translationService.Verify(
            t => t.GetOrTranslateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Translate_DifferentLanguage_CallsTranslationServiceAndReturnsOk()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);
        _translationService
            .Setup(t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTranslationResult("English text", true, "AI-translated disclaimer"));

        var result = await _controller.Translate(10, "en");

        Assert.IsType<OkObjectResult>(result);
        _translationService.Verify(
            t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Translate_TranslationServiceThrows_ReturnsErrorStatus()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 10,
            CaseId = 42,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = "বাংলা লেখা এখানে আছে",
            Status = DocumentStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _docRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(doc);
        _translationService
            .Setup(t => t.GetOrTranslateAsync(10, doc.ContentDraft, "bn", "en", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Translation result failed the quality check."));

        var result = await _controller.Translate(10, "en");

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, status.StatusCode);
    }
}
