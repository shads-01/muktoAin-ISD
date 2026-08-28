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
}
