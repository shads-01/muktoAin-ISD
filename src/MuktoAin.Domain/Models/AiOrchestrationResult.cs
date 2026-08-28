namespace MuktoAin.Domain.Models;

public record AiOrchestrationResult(
    string Content,
    IReadOnlyList<RetrievedSection> CitedSections,
    string Disclaimer,
    bool IsCached = false);
