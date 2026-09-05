namespace MuktoAin.Application.DTOs;

// One assistant answer rendered in the chat thread
public record ChatTurnDto(
    string Answer,
    IReadOnlyList<CitedSectionDto> CitedSections,
    string Disclaimer,
    bool FromCache,
    bool RetrievalOnly,
    string Tier // "full" | "capped" | "retrieval-only" | "wall"
);

public record ChatMessageDto(
    int ChatMessageId,
    string Role,
    string Content,
    string? CitedJson
);

public record RecentChatDto(
    int ChatSessionId,
    string Title,
    DateTime UpdatedAt,
    int MessageCount
);

public record ChatCommitResultDto(
    int CaseId,
    string? AnonymousTrackingCode,
    int DocumentId,
    string DocumentContent
);

public record QuotaSnapshotDto(
    int RemainingToday,
    int DailyLimit,
    bool IsLoggedIn
);
