using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class GeneralDiaryTemplateTests
{
    private readonly GeneralDiaryTemplate _template = new();

    [Fact]
    public void DocumentType_ReturnsGeneralDiary()
    {
        Assert.Equal(DocumentType.GeneralDiary, _template.DocumentType);
    }

    [Fact]
    public async Task RenderAsync_WithFullContext_RendersAllSectionsAndDisclaimers()
    {
        var district = new District { DistrictId = 2, Name = "Chattogram" };
        var caseEntity = new Case
        {
            CaseId = 20,
            DistrictId = 2,
            District = district,
            Description = "Lost National ID card (NID) and received unknown threatening phone calls."
        };

        var citedSections = new List<CitedSectionDto>
        {
            new(201, "Code of Criminal Procedure, 1898", "154", "Information in cognizable cases and police station entries.", 0.90f, "Vector"),
            new(202, "Penal Code, 1860", "506", "Punishment for criminal intimidation.", 0.88f, "Vector")
        };

        var explanation = new RightsExplanationDto(
            Explanation: "You have the statutory right to record an official GD entry at your local police station for document loss and security threats.",
            CitedSections: citedSections,
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("TO", rendered);
        Assert.Contains("The Officer-in-Charge", rendered);
        Assert.Contains("Police Station / Thana, Chattogram, Bangladesh", rendered);
        Assert.Contains("Subject: General Diary (GD) Entry Application (Relating to Code of Criminal Procedure, 1898, Section 154)", rendered);
        Assert.Contains("STATEMENT OF FACTS:", rendered);
        Assert.Contains("Lost National ID card (NID) and received unknown threatening phone calls.", rendered);
        Assert.Contains("APPLICABLE LEGAL PROVISIONS:", rendered);
        Assert.Contains("• Code of Criminal Procedure, 1898, Section 154:", rendered);
        Assert.Contains("• Penal Code, 1860, Section 506:", rendered);
        Assert.Contains("APPLICABLE LEGAL RIGHTS & REMEDIES:", rendered);
        Assert.Contains("PRAYER:", rendered);
        Assert.Contains("DECLARATION:", rendered);
        Assert.Contains("District: Chattogram", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }

    [Fact]
    public async Task RenderAsync_WithoutCitedSections_RendersFallbackAndDistrictPlaceholder()
    {
        var caseEntity = new Case
        {
            CaseId = 21,
            District = null!,
            Description = "Lost SSC academic certificates on transit."
        };

        var explanation = new RightsExplanationDto(
            Explanation: string.Empty,
            CitedSections: Array.Empty<CitedSectionDto>(),
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("Police Station / Thana, ________, Bangladesh", rendered);
        Assert.Contains("Subject: General Diary (GD) Entry Application", rendered);
        Assert.Contains("[No specific sections retrieved — consult the duty officer or a qualified advocate]", rendered);
        Assert.Contains("District: ________", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }
}
