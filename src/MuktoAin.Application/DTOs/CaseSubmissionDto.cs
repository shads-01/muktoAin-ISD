namespace MuktoAin.Application.DTOs;

// Citizen intake form data
public record CaseSubmissionDto(
    int CategoryId,
    byte DistrictId,
    string Title,
    string Description,
    string Language,
    bool IsAnonymous
);
