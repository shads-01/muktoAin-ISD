using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

public interface IPromptAssembler
{
    Task<string> AssemblePromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default);

    // S-3.3: few-shot IRAC variant of the rights-explanation prompt
    // (requirements.md §4 "Few-Shot Steering").
    Task<string> AssembleFewShotIracPromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        CancellationToken ct = default);
}
