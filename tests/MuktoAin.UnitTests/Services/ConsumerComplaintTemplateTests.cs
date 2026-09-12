using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class ConsumerComplaintTemplateTests
{
    private readonly ConsumerComplaintTemplate _template = new();

    [Fact]
    public void DocumentType_ReturnsConsumerComplaint()
    {
        Assert.Equal(DocumentType.ConsumerComplaint, _template.DocumentType);
    }

    [Fact]
    public async Task RenderAsync_WithFullContext_RendersAllSectionsAndDisclaimers()
    {
        var district = new District { DistrictId = 4, Name = "Rajshahi" };
        var caseEntity = new Case
        {
            CaseId = 40,
            DistrictId = 4,
            District = district,
            Description = "Purchased expired dairy products from local superstore and seller refused replacement/refund."
        };

        var citedSections = new List<CitedSectionDto>
        {
            new(401, "Consumer Rights Protection Act, 2009", "45", "Penalty for selling adulterated or expired food products.", 0.94f, "Vector"),
            new(402, "Consumer Rights Protection Act, 2009", "76", "Complaint procedure and 25% reward from realized fines.", 0.96f, "Vector")
        };

        var explanation = new RightsExplanationDto(
            Explanation: "Under Section 45 and 76 of the Consumer Rights Protection Act, you can claim refund/compensation and are entitled to 25% of the fine realized.",
            CitedSections: citedSections,
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("TO", rendered);
        Assert.Contains("Directorate of National Consumer Rights Protection (DNCRP)", rendered);
        Assert.Contains("District Office: Rajshahi, Bangladesh", rendered);
        Assert.Contains("Subject: Complaint Under Section 45 of the Consumer Rights Protection Act, 2009", rendered);
        Assert.Contains("FACTS OF THE COMPLAINT:", rendered);
        Assert.Contains("Purchased expired dairy products from local superstore and seller refused replacement/refund.", rendered);
        Assert.Contains("APPLICABLE LEGAL PROVISIONS:", rendered);
        Assert.Contains("• Consumer Rights Protection Act, 2009, Section 45:", rendered);
        Assert.Contains("• Consumer Rights Protection Act, 2009, Section 76:", rendered);
        Assert.Contains("YOUR RIGHTS UNDER CONSUMER LAW:", rendered);
        Assert.Contains("RELIEF / REMEDY SOUGHT:", rendered);
        Assert.Contains("SUPPORTING EVIDENCE / ATTACHMENTS:", rendered);
        Assert.Contains("DECLARATION:", rendered);
        Assert.Contains("District: Rajshahi", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }

    [Fact]
    public async Task RenderAsync_WithoutCitedSections_RendersFallbackAndDistrictPlaceholder()
    {
        var caseEntity = new Case
        {
            CaseId = 41,
            District = null!,
            Description = "Overcharged for electronic appliance above MRP."
        };

        var explanation = new RightsExplanationDto(
            Explanation: string.Empty,
            CitedSections: Array.Empty<CitedSectionDto>(),
            Disclaimer: Disclaimers.Legal
        );

        var rendered = await _template.RenderAsync(caseEntity, explanation);

        Assert.Contains("District Office: ________, Bangladesh", rendered);
        Assert.Contains("Subject: Complaint the Consumer Rights Protection Act, 2009", rendered);
        Assert.Contains("• Consumer Rights Protection Act, 2009 (Relevant anti-consumer practice provisions)", rendered);
        Assert.Contains("District: ________", rendered);
        Assert.Contains(Disclaimers.Legal, rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);
    }
}
