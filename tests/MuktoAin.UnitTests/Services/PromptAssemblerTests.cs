using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Models;

namespace MuktoAin.UnitTests.Services;

public class PromptAssemblerTests
{
    private readonly Mock<IScenarioMappingRepository> _scenarioRepoMock = new();
    private readonly PromptAssembler _assembler;

    public PromptAssemblerTests()
    {
        _assembler = new PromptAssembler(_scenarioRepoMock.Object);
    }

    [Fact]
    public async Task AssemblePromptAsync_ForRightsExplanation_IncludesContextAndDisclaimer()
    {
        var sections = new List<RetrievedSection>
        {
            new(10, "Bangladesh Labour Act, 2006", "33", "A worker may submit a grievance...", 0.95f, RetrievalMethod.Vector)
        };

        _scenarioRepoMock.Setup(r => r.SearchByKeywordAsync(It.IsAny<string>()))
            .ReturnsAsync(Enumerable.Empty<ScenarioMapping>());

        var prompt = await _assembler.AssemblePromptAsync(
            "I have not been paid salary",
            sections,
            "bn",
            AiRequestType.RightsExplanation);

        Assert.Contains("I have not been paid salary", prompt);
        Assert.Contains("Bangladesh Labour Act, 2006", prompt);
        Assert.Contains("Section 33", prompt);
        Assert.Contains("A worker may submit a grievance", prompt);
        Assert.Contains("Bengali", prompt);
        Assert.Contains(Disclaimers.LegalBangla, prompt);
    }

    [Fact]
    public async Task AssemblePromptAsync_ForDrafting_SelectsDraftingTemplateWithDocumentType()
    {
        var sections = new List<RetrievedSection>
        {
            new(20, "Bangladesh Labour Act, 2006", "123", "Payment of wages...", 0.88f, RetrievalMethod.Vector)
        };

        _scenarioRepoMock.Setup(r => r.SearchByKeywordAsync(It.IsAny<string>()))
            .ReturnsAsync(Enumerable.Empty<ScenarioMapping>());

        var prompt = await _assembler.AssemblePromptAsync(
            "Unpaid wages for 3 months",
            sections,
            "en",
            AiRequestType.Drafting,
            documentType: "Labour Complaint");

        Assert.Contains("Labour Complaint", prompt);
        Assert.Contains("Unpaid wages for 3 months", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains(Disclaimers.Legal, prompt);
    }

    [Fact]
    public async Task AssemblePromptAsync_InjectsScenarioMappingHintsWhenFound()
    {
        var mappings = new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = 33, ScenarioKeyword = "unpaid salary", Notes = "Labour Act s.33" }
        };

        _scenarioRepoMock.Setup(r => r.SearchByKeywordAsync("unpaid salary"))
            .ReturnsAsync(mappings);

        var prompt = await _assembler.AssemblePromptAsync(
            "unpaid salary",
            Enumerable.Empty<RetrievedSection>(),
            "en",
            AiRequestType.RightsExplanation);

        Assert.Contains("Curated Scenario Guidance", prompt);
        Assert.Contains("Keyword 'unpaid salary' maps to Section ID 33", prompt);
    }
}
