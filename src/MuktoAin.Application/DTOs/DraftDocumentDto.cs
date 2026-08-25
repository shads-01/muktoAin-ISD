namespace MuktoAin.Application.DTOs;

// Generated document preview
public record DraftDocumentDto(
    int DocumentId,
    int CaseId,
    string DocumentType,
    string ContentDraft,
    string Status,
    DateTime CreatedAt
);
