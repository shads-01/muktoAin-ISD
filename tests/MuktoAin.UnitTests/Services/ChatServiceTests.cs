using MuktoAin.Application.Documents;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Moq;

namespace MuktoAin.UnitTests.Services;

// Covers ChatService.CommitToCaseAsync's encryption round-trip. Case.Title
// and Case.Description are field-level-encrypted PII (S-1.7/AUD-2), but the
// AI rights service and the document templates (e.g. RtiRequestTemplate
// embeds Description verbatim in the letter body) need PLAINTEXT to work
// with. Confirmed live in the dev DB: cases committed through this path were
// ending up with PLAINTEXT Title/Description at rest, because
// GetWithDocumentsAsync/GetByIdAsync resolve to the SAME EF-tracked Case
// instance (identity map on the shared scoped DbContext) that CommitToCaseAsync
// temporarily flips to plaintext for rendering, and DocumentService's own
// SaveChangesAsync (inside GenerateDocumentAsync) flushed that plaintext
// straight to the database.
public class ChatServiceTests
{
    private readonly Mock<IRepository<ChatSession>> _sessionRepo = new();
    private readonly Mock<IRepository<ChatMessage>> _messageRepo = new();
    private readonly Mock<IRepository<Case>> _caseRepo = new();
    private readonly Mock<ICaseRepository> _caseRepoTyped = new();
    private readonly Mock<IRepository<AnswerCache>> _cacheRepo = new();
    private readonly Mock<IRightsExplanationService> _rightsService = new();
    private readonly Mock<IEncryptionService> _encryptionService = new();
    private readonly Mock<IScenarioMappingRepository> _scenarioRepo = new();
    private readonly Mock<IKeywordSectionSearch> _keywordSearch = new();
    private readonly Mock<IRepository<CaseCategory>> _categoryRepo = new();

    private readonly Mock<IRepository<GeneratedDocument>> _docRepo = new();
    private readonly Mock<IRepository<District>> _districtRepo = new();
    private readonly Mock<IPdfExporter> _pdfExporter = new();
    private readonly Mock<IDocumentTemplate> _template = new();

    private readonly ChatService _service;

    public ChatServiceTests()
    {
        _encryptionService.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns<string>(s => string.IsNullOrEmpty(s) ? s : $"ENC_{s}");

        // DocumentGenerator reads DocumentType once, at construction, to
        // build its lookup dictionary -- must be set up before that happens.
        _template.Setup(t => t.DocumentType).Returns(DocumentType.RtiRequest);

        var documentService = new DocumentService(
            new DocumentGenerator(new[] { _template.Object }),
            _docRepo.Object, _caseRepoTyped.Object, _districtRepo.Object, _categoryRepo.Object,
            _pdfExporter.Object);

        _service = new ChatService(
            _sessionRepo.Object, _messageRepo.Object, _caseRepo.Object, _caseRepoTyped.Object,
            _cacheRepo.Object, _rightsService.Object, documentService, _encryptionService.Object,
            _scenarioRepo.Object, _keywordSearch.Object, _categoryRepo.Object);
    }

    [Fact]
    public async Task CommitToCaseAsync_RestoresEncryptedTitleAndDescription_AfterDocumentGeneration()
    {
        const int sessionId = 1;
        const int categoryId = 3; // RTI Request
        _sessionRepo.Setup(r => r.GetByIdAsync(sessionId))
            .ReturnsAsync(new ChatSession { ChatSessionId = sessionId, Status = ChatSessionStatus.InProgress });
        _messageRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ChatMessage>
        {
            new() { ChatSessionId = sessionId, Role = "user", Content = "আমি তথ্য চাই।" }
        });

        Case? saved = null;
        _caseRepo.Setup(r => r.AddAsync(It.IsAny<Case>()))
            .Callback<Case>(c => saved = c)
            .Returns(Task.CompletedTask);
        _caseRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        // Identity-map simulation: every lookup for this case returns the
        // SAME tracked instance that AddAsync captured -- exactly like EF
        // Core's change tracker does within one scoped DbContext.
        _caseRepoTyped.Setup(r => r.GetWithDocumentsAsync(It.IsAny<int>()))
            .ReturnsAsync(() => saved);
        _docRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync(() => null);
        _caseRepoTyped.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync(() => saved);
        _districtRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((District?)null);
        _categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((CaseCategory?)null);

        _template.Setup(t => t.RenderAsync(It.IsAny<Case>(), It.IsAny<RightsExplanationDto>()))
            .ReturnsAsync("rendered content");

        string? descriptionSeenByAi = null;
        _rightsService.Setup(s => s.ExplainRightsAsync(It.IsAny<Case>(), It.IsAny<CancellationToken>()))
            .Callback<Case, CancellationToken>((c, _) => descriptionSeenByAi = c.Description)
            .ReturnsAsync(new RightsExplanationDto("explanation", new List<CitedSectionDto>(), "disclaimer"));

        _docRepo.Setup(r => r.AddAsync(It.IsAny<GeneratedDocument>())).Returns(Task.CompletedTask);
        _docRepo.Setup(r => r.SaveChangesAsync())
            // DocumentService.GenerateDocumentAsync's own SaveChangesAsync
            // runs on the SAME shared DbContext -- this is exactly the moment
            // that used to flush the temporarily-plaintext Case to the DB.
            .Callback(() => Assert.NotNull(saved))
            .Returns(Task.CompletedTask);

        await _service.CommitToCaseAsync(
            sessionId, categoryId, districtId: 1, title: "গোপন শিরোনাম",
            notificationEmail: null, isAnonymous: false, userId: 42);

        // The AI pipeline must have been handed PLAINTEXT (needed for the
        // prompt / rendered document body), not the stored ciphertext.
        Assert.NotNull(descriptionSeenByAi);
        Assert.DoesNotContain("ENC_", descriptionSeenByAi);

        // But by the time CommitToCaseAsync returns, the DB-tracked entity
        // must be back to the encrypted values -- this is the regression:
        // it used to stay stuck at the plaintext set for the AI/template step.
        Assert.NotNull(saved);
        Assert.Equal("ENC_গোপন শিরোনাম", saved!.Title);
        Assert.StartsWith("ENC_", saved.Description);
    }
}
