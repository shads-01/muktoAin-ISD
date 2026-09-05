using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record QueueItemDto(
    int DocumentId,
    int CaseId,
    string CaseTitle,
    string CategoryName,
    string DistrictName,
    DocumentStatus Status,
    bool CitizenEdited,
    int VersionNo,
    string? ClaimedBy,       // lawyer display name when claimed by someone
    DateTime CreatedAt,
    DateTime? ClaimedAt,
    bool CanOpen             // false when claimed by another lawyer
);

public record ReviewWorkspaceDto(
    int DocumentId,
    int CaseId,
    string CaseTitle,
    string CategoryName,
    string DistrictName,
    string CitizenNarrative, // decrypted case description (PII — session-scoped)
    IReadOnlyList<CitedSectionDto> Citations,
    string OriginalDraft,     // ContentDraft — immutable
    string? CitizenEditedDraft, // ContentFinal if CitizenEdited
    int VersionNo,
    bool CitizenEdited
);

public record SubmitReviewDto(
    int DocumentId,
    int LawyerProfileId,
    ReviewDecision Decision,
    string Comments,          // MANDATORY for every decision; rejection shows to citizen
    string? EditedContent    // required when Decision == EditedApproved
);
