using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class DocumentControllerTests
{
    private readonly Mock<IRepository<GeneratedDocument>> _docRepo;
    private readonly Mock<ICaseRepository> _caseRepo;
    private readonly DocumentController _controller;

    public DocumentControllerTests()
    {
        _docRepo = new Mock<IRepository<GeneratedDocument>>();
        _caseRepo = new Mock<ICaseRepository>();

        // AUD-10: the controller now ALWAYS runs the ownership check through
        // CaseService, so the fixture authenticates user 42 and backs the
        // service with a mock case repo the tests point at per-case.
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<string>()))
                  .Returns<string>(s => s ?? string.Empty);
        var categoryRepo = new Mock<IRepository<CaseCategory>>();
        categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                    .ReturnsAsync(new CaseCategory { CategoryId = 1, Name = "শ্রম / Labour" });
        var districtRepo = new Mock<IRepository<District>>();
        districtRepo.Setup(r => r.GetByIdAsync(It.IsAny<byte>()))
                    .ReturnsAsync(new District { DistrictId = 1, Name = "Dhaka" });
        var caseService = new CaseService(
            _caseRepo.Object, categoryRepo.Object, districtRepo.Object, encryption.Object,
            new Mock<IRepository<Notification>>().Object);

        var httpContext = new DefaultHttpContext
        {
            // ResolveTrackingCode reads HttpContext.Session when no tracking
            // code is in TempData/query — the fixture context needs one.
            Session = new TestSession(),
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "42")
            }))
        };

        _controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            null!, // DocumentService — only touched on the approved-PDF stream path
            caseService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    // Grants the authenticated user (42) ownership of the given case so the
    // always-on CaseService ownership check in Preview/Download passes.
    private void SetupOwnedCase(int caseId)
    {
        _caseRepo.Setup(r => r.GetWithDocumentsAsync(caseId)).ReturnsAsync(new Case
        {
            CaseId = caseId,
            UserId = 42,
            CategoryId = 1,
            DistrictId = 1,
            Status = CaseStatus.Submitted
        });
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
        SetupOwnedCase(42);

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
        SetupOwnedCase(42);

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
        SetupOwnedCase(55);

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
    public async Task Preview_WhenDocumentNotFoundInRepo_ReturnsNotFound_WithoutMockFallback()
    {
        _docRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((GeneratedDocument?)null);

        var result = await _controller.Preview(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Preview_WhenCaseServicePresentAndUserUnauthorized_ReturnsForbid()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 30,
            CaseId = 77,
            Status = DocumentStatus.Draft
        };
        _docRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(doc);

        var caseRepo = new Mock<ICaseRepository>();
        caseRepo.Setup(r => r.GetWithDocumentsAsync(77)).ReturnsAsync((Case?)null);

        var caseService = new CaseService(
            caseRepo.Object,
            Mock.Of<IRepository<CaseCategory>>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IEncryptionService>(),
            Mock.Of<IRepository<Notification>>());

        var controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            null!, // DocumentService — not reached on the Forbid path
            caseService)
        {
            // ResolveTrackingCode peeks TempData and reads HttpContext.Session
            // before the ownership check — both must exist or this path throws
            // instead of returning Forbid.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = new TestSession() }
            },
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var result = await controller.Preview(30);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Download_WhenCaseServicePresentAndUserUnauthorized_ReturnsForbid()
    {
        var doc = new GeneratedDocument
        {
            DocumentId = 30,
            CaseId = 77,
            Status = DocumentStatus.Approved
        };
        _docRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(doc);

        var caseRepo = new Mock<ICaseRepository>();
        caseRepo.Setup(r => r.GetWithDocumentsAsync(77)).ReturnsAsync((Case?)null);

        var caseService = new CaseService(
            caseRepo.Object,
            Mock.Of<IRepository<CaseCategory>>(),
            Mock.Of<IRepository<District>>(),
            Mock.Of<MuktoAin.Domain.Interfaces.IEncryptionService>(),
            Mock.Of<IRepository<Notification>>());

        var controller = new DocumentController(
            Mock.Of<ILogger<DocumentController>>(),
            _docRepo.Object,
            null!, // DocumentService — not reached on the Forbid path
            caseService)
        {
            // See the Preview Forbid test — TempData + Session are required
            // on this path.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = new TestSession() }
            },
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var result = await controller.Download(30);

        Assert.IsType<ForbidResult>(result);
    }
}

