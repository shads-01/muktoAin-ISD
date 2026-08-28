using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

public interface IAiOrchestrationService
{
    Task<AiOrchestrationResult> ProcessCaseAsync(
        Case @case,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default);
}
