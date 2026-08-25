namespace MuktoAin.Application.DTOs;

// Full case view for tracking
public record CaseDetailDto(
    int CaseId,
    string Title,
    string Description,
    string CategoryName,
    string DistrictName,
    string Status,
    bool IsAnonymous,
    DateTime CreatedAt,
    IReadOnlyList<DraftDocumentDto>? Documents
);
