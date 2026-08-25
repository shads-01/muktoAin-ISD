namespace MuktoAin.Application.DTOs;

// Lawyer review submission
public record ReviewDto(
    int DocumentId,
    string Decision,
    string? EditedContent,
    string Comments
);
