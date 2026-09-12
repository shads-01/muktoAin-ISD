using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class RtiRequestTemplateTests
{
    private readonly RtiRequestTemplate _template = new();

    [Fact]
    public void DocumentType_ReturnsRtiRequest()
    {
        Assert.Equal(DocumentType.RtiRequest, _template.DocumentType);
    }

    [Fact]
    public async Task RenderAsync_WithFullContext_RendersAllSectionsAndDisclaimers()
    {
        var district = new District { DistrictId = 3, Name = "Sylhet" };
        var caseEntity = new Case
        {
            CaseId = 30,
            DistrictId = 3,
            District = district,
            Description = "Requesting certified expenditure statement for local road construction project (2024-2025)."
        };

        var citedSections = new List<CitedSectionDto>
        {
            new(301, "Right to Information Act, 2009", "8", "Procedure for request and supply of information.", 0.95f, "Vector"),
            new(302, "Right to Information Act, 2009", "9", "Statutory time-limits for providing information (20 working days).", 0.91f, "Vector")
        };

        var explanation = new RightsExplanationDto(
            Explanation: "Under Section 8 and 9 of the RTI Act, 2009, the designated officer must provide the requested information within 20 working days.",
            CitedSections: citedSections,
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("TO", rendered);
        Assert.Contains("The Designated Officer / RTI Officer", rendered);
        Assert.Contains("Sylhet, Bangladesh", rendered);
        Assert.Contains("Subject: Application for Information Under Section 8 of the Right to Information Act, 2009", rendered);
        Assert.Contains("INFORMATION REQUESTED:", rendered);
        Assert.Contains("Requesting certified expenditure statement for local road construction project (2024-2025).", rendered);
        Assert.Contains("APPLICABLE LEGAL PROVISIONS / JUSTIFICATION:", rendered);
        Assert.Contains("• Right to Information Act, 2009, Section 8:", rendered);
        Assert.Contains("• Right to Information Act, 2009, Section 9:", rendered);
        Assert.Contains("STATUTORY RIGHTS & TIMELINES UNDER RTI ACT, 2009:", rendered);
        Assert.Contains("PREFERRED FORMAT OF INFORMATION:", rendered);
        Assert.Contains("DECLARATION:", rendered);
        Assert.Contains("District: Sylhet", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }

    [Fact]
    public async Task RenderAsync_WithoutCitedSections_RendersDefaultProvisionsAndDistrictPlaceholder()
    {
        var caseEntity = new Case
        {
            CaseId = 31,
            District = null!,
            Description = "Seeking government grant allocation data."
        };

        var explanation = new RightsExplanationDto(
            Explanation: string.Empty,
            CitedSections: Array.Empty<CitedSectionDto>(),
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("________, Bangladesh", rendered);
        Assert.Contains("Subject: Application for Information Under Section 8 of the Right to Information Act, 2009", rendered);
        Assert.Contains("• Right to Information Act, 2009, Section 8 (Procedure for obtaining information)", rendered);
        Assert.Contains("District: ________", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }
}
