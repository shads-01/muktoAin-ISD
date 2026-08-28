using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.UnitTests.Services;

public class RightsExplanationServiceTests
{
    private readonly Mock<IAiOrchestrationService> _orchestrationMock = new();
    private readonly RightsExplanationService _service;

    public RightsExplanationServiceTests()
    {
        _service = new RightsExplanationService(_orchestrationMock.Object);
    }

    [Fact]
    public async Task ExplainRightsAsync_DelegatesToOrchestratorAndMapsToDto()
    {
        var @case = new Case { CaseId = 1, Description = "Unpaid salary", Language = "bn" };
        var sections = new List<RetrievedSection>
        {
            new(42, "Bangladesh Labour Act, 2006", "33", "Grievance procedure...", 0.92f, RetrievalMethod.Vector, "XLII", 2006)
        };

        var orchestrationResult = new AiOrchestrationResult(
            Content: "Your rights under section 33 are...",
            CitedSections: sections,
            Disclaimer: Disclaimers.LegalBangla);

        _orchestrationMock.Setup(o => o.ProcessCaseAsync(@case, AiRequestType.RightsExplanation, null, default))
            .ReturnsAsync(orchestrationResult);

        var dto = await _service.ExplainRightsAsync(@case);

        Assert.NotNull(dto);
        Assert.Equal("Your rights under section 33 are...", dto.Explanation);
        Assert.Equal(Disclaimers.LegalBangla, dto.Disclaimer);
        Assert.Single(dto.CitedSections);
        Assert.Equal(42, dto.CitedSections[0].SectionId);
        Assert.Equal("Bangladesh Labour Act, 2006", dto.CitedSections[0].ActTitle);
        Assert.Equal("33", dto.CitedSections[0].SectionNumber);
        Assert.Equal("Vector", dto.CitedSections[0].RetrievalMethod);
        Assert.Equal("XLII", dto.CitedSections[0].ActNumber);
        Assert.Equal(2006, dto.CitedSections[0].ActYear);
    }
}
