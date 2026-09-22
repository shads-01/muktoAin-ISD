namespace MuktoAin.Application.DTOs;

// One assistant answer rendered in the chat thread
public record ChatTurnDto(
    string Answer,
    IReadOnlyList<CitedSectionDto> CitedSections,
    string Disclaimer,
    bool FromCache,
    bool RetrievalOnly,
    string Tier, // "full" | "retrieval-only" | "wall"
    bool Blocked = false,
    int? SuggestedCategoryId = null,
    string? CaseFileJson = null,
    IReadOnlyList<string>? MissingInfo = null,
    bool CanDraft = false
);

// Structured envelope the intake model returns each turn (spec 3.1). C# owns
// state; the model re-emits the full case file, which C# stores opaquely.
public record ChatEnvelope(
    string Intent, // normal | probing | injection | off_topic
    string Reply,
    string? CaseFileJson,
    IReadOnlyList<string> MissingInfo,
    bool ReadyToExplain,
    string? SuggestedDraftType,
    string? Language,
    bool CanDraft = false
);

public record ChatMessageDto(
    int ChatMessageId,
    string Role,
    string Content,
    string? CitedJson
);

public record RecentChatDto(
    int ChatSessionId, string Title, DateTime UpdatedAt,
    int MessageCount, string Status, int? CaseId);

public record ChatHistoryPageDto(
    IReadOnlyList<RecentChatDto> Chats,
    DateTime? BeforeUpdatedAt, int? BeforeId);

public record ChatCommitResultDto(
    int CaseId,
    string? AnonymousTrackingCode,
    int DocumentId,
    string DocumentContent
);

public record QuotaSnapshotDto(
    int RemainingToday,
    int DailyLimit,
    bool IsLoggedIn,
    int Credits = 0      // paid chat credits left (signed-in users only)
);
