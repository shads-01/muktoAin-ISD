using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Interfaces.Services;

public interface IAiLogService
{
    Task LogAsync(
        int? caseId,
        AiRequestType type,
        string prompt,
        string response,
        string model,
        int tokensUsed,
        int latencyMs,
        CancellationToken ct = default);
}
