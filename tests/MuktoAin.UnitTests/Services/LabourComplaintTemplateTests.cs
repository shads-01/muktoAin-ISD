using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class LabourComplaintTemplateTests
{
    private readonly LabourComplaintTemplate _template = new();

    [Fact]
    public void DocumentType_ReturnsLabourComplaint()
    {
        Assert.Equal(DocumentType.LabourComplaint, _template.DocumentType);
    }

    [Fact]
    public async Task RenderAsync_WithFullContext_RendersAllSectionsAndDisclaimers()
    {
        var district = new District { DistrictId = 1, Name = "Dhaka" };
        var caseEntity = new Case
        {
            CaseId = 10,
            DistrictId = 1,
            District = district,
            Description = "Employer has not paid salary for 3 months."
        };

        var citedSections = new List<CitedSectionDto>
        {
            new(101, "Bangladesh Labour Act, 2006", "33", "Grievance procedure for worker complaints.", 0.85f, "Vector"),
            new(102, "Bangladesh Labour Act, 2006", "121", "Payment of wages and time of payment.", 0.92f, "Vector")
        };

        var explanation = new RightsExplanationDto(
            Explanation: "Under Section 33 and 121, you have the right to claim unpaid wages within the statutory period.",
            CitedSections: citedSections,
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("TO", rendered);
        Assert.Contains("The Inspector General / District Labour Court", rendered);
        Assert.Contains("Dhaka, Bangladesh", rendered);
        Assert.Contains("Subject: Complaint Under Section 33 of the Bangladesh Labour Act, 2006", rendered);
        Assert.Contains("FACTS OF THE CASE:", rendered);
        Assert.Contains("Employer has not paid salary for 3 months.", rendered);
        Assert.Contains("APPLICABLE LEGAL PROVISIONS:", rendered);
        Assert.Contains("• Bangladesh Labour Act, 2006, Section 33:", rendered);
        Assert.Contains("• Bangladesh Labour Act, 2006, Section 121:", rendered);
        Assert.Contains("YOUR RIGHTS UNDER APPLICABLE LAW:", rendered);
        Assert.Contains("RELIEF SOUGHT:", rendered);
        Assert.Contains("DECLARATION:", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }

    [Fact]
    public async Task RenderAsync_WithoutCitedSections_RendersFallbackAndDistrictPlaceholder()
    {
        var caseEntity = new Case
        {
            CaseId = 11,
            District = null!,
            Description = "General grievance description."
        };

        var explanation = new RightsExplanationDto(
            Explanation: string.Empty,
            CitedSections: Array.Empty<CitedSectionDto>(),
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("________, Bangladesh", rendered);
        Assert.Contains("Subject: Complaint Under  the Bangladesh Labour Act, 2006", rendered);
        Assert.Contains("[No specific sections retrieved — consult a qualified advocate]", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }
}
