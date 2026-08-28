using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.UnitTests.Services;

public class AiOrchestrationServiceTests
{
    private readonly Mock<IRagContextBuilder> _ragContextMock = new();
    private readonly Mock<IPromptAssembler> _promptAssemblerMock = new();
    private readonly Mock<MuktoAin.Domain.Interfaces.IAiService> _aiServiceMock = new();
    private readonly DisclaimerInjector _disclaimerInjector = new();
    private readonly Mock<IAiLogService> _aiLogServiceMock = new();
    private readonly Mock<IRepository<AiLog>> _logRepoMock = new();
    private readonly Mock<IRepository<CaseActReference>> _caseActRefRepoMock = new();

    private readonly AiOrchestrationService _service;

    public AiOrchestrationServiceTests()
    {
        _service = new AiOrchestrationService(
            _ragContextMock.Object,
            _promptAssemblerMock.Object,
            _aiServiceMock.Object,
            _disclaimerInjector,
            _aiLogServiceMock.Object,
            _logRepoMock.Object,
            _caseActRefRepoMock.Object,
            "gemini-2.0-flash");
    }

    [Fact]
    public async Task ProcessCaseAsync_EndToEndPipeline_GeneratesContentInjectsDisclaimerAndLogs()
    {
        var @case = new Case
        {
            CaseId = 1,
            Description = "My employer refused to pay my wages for 3 months.",
            Language = "en"
        };

        var sections = new List<RetrievedSection>
        {
            new(101, "Bangladesh Labour Act, 2006", "123", "Time of payment of wages...", 0.9f, RetrievalMethod.Vector)
        };

        _logRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AiLog>());
        _ragContextMock.Setup(r => r.RetrieveContextAsync(@case.Description, 8))
            .ReturnsAsync(sections);
        _promptAssemblerMock.Setup(p => p.AssemblePromptAsync(
            @case.Description, sections, "en", AiRequestType.RightsExplanation, null, default))
            .ReturnsAsync("Grounded prompt text");
        _aiServiceMock.Setup(a => a.GenerateContentAsync("Grounded prompt text", default))
            .ReturnsAsync("You have the right to receive wages under Section 123.");

        var result = await _service.ProcessCaseAsync(@case, AiRequestType.RightsExplanation);

        Assert.NotNull(result);
        Assert.False(result.IsCached);
        Assert.Contains("You have the right to receive wages under Section 123.", result.Content);
        Assert.Contains(Disclaimers.Legal, result.Content);
        Assert.Single(result.CitedSections);
        Assert.Equal(101, result.CitedSections[0].SectionId);

        _aiLogServiceMock.Verify(l => l.LogAsync(
            1,
            AiRequestType.RightsExplanation,
            "Grounded prompt text",
            It.Is<string>(s => s.Contains("You have the right") && s.Contains(Disclaimers.Legal)),
            "gemini-2.0-flash",
            It.IsAny<int>(),
            It.IsAny<int>(),
            default), Times.Once);

        _caseActRefRepoMock.Verify(r => r.AddAsync(It.Is<CaseActReference>(c => c.CaseId == 1 && c.SectionId == 101)), Times.Once);
        _caseActRefRepoMock.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessCaseAsync_WhenExistingCachedLogExists_ReturnsCachedResponseWithoutCallingAI()
    {
        var @case = new Case
        {
            CaseId = 42,
            Description = "Problem description",
            Language = "bn"
        };

        var existingLogs = new List<AiLog>
        {
            new()
            {
                CaseId = 42,
                RequestType = AiRequestType.RightsExplanation,
                ResponseText = "Cached advice content\n\n" + Disclaimers.LegalBangla,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            }
        };

        _logRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(existingLogs);

        var result = await _service.ProcessCaseAsync(@case, AiRequestType.RightsExplanation);

        Assert.True(result.IsCached);
        Assert.Contains("Cached advice content", result.Content);
        _aiServiceMock.Verify(a => a.GenerateContentAsync(It.IsAny<string>(), default), Times.Never);
        _aiLogServiceMock.Verify(l => l.LogAsync(
            It.IsAny<int?>(), It.IsAny<AiRequestType>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), default), Times.Never);
    }
}
