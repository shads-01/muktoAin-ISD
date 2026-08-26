namespace MuktoAin.Application.DTOs;

// Returned by CaseService.SubmitCaseAsync. For anonymous submissions,
// AnonymousTrackingCode is a GUID shown ONCE to the submitter — their only
// way to use FR-8 tracking. Null for identified submissions.
public record CaseSubmissionResultDto(
    int CaseId,
    string? AnonymousTrackingCode
);
