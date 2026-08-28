using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

public class RightsExplanationService : IRightsExplanationService
{
    private readonly IAiOrchestrationService _orchestrationService;

    public RightsExplanationService(IAiOrchestrationService orchestrationService)
    {
        _orchestrationService = orchestrationService;
    }

    public async Task<RightsExplanationDto> ExplainRightsAsync(Case @case, CancellationToken ct = default)
    {
        var result = await _orchestrationService.ProcessCaseAsync(
            @case,
            AiRequestType.RightsExplanation,
            documentType: null,
            ct: ct);

        var citedSections = result.CitedSections
            .Select(s => new CitedSectionDto(
                SectionId: s.SectionId,
                ActTitle: s.ActTitle,
                SectionNumber: s.SectionNumber,
                SectionText: s.SectionText,
                RelevanceScore: s.RelevanceScore,
                RetrievalMethod: s.Method.ToString(),
                ActNumber: s.ActNumber,
                ActYear: s.ActYear))
            .ToList();

        return new RightsExplanationDto(
            Explanation: result.Content,
            CitedSections: citedSections,
            Disclaimer: result.Disclaimer);
    }
}
