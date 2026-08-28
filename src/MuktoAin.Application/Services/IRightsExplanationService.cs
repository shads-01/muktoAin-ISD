using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Application.Services;

public interface IRightsExplanationService
{
    Task<RightsExplanationDto> ExplainRightsAsync(Case @case, CancellationToken ct = default);
}
