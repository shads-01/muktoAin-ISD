using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

public class AiLog
{
    // BIGINT: high-volume table
    public long LogId { get; set; }

    public int? CaseId { get; set; }
    public Case? Case { get; set; }

    public AiRequestType RequestType { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public string ResponseText { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public int TokensUsed { get; set; }

    // Round-trip API call duration in milliseconds (required by FR-12)
    public int LatencyMs { get; set; }

    public DateTime CreatedAt { get; set; }
}
