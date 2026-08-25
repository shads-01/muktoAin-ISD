namespace MuktoAin.Application.DTOs;

// Acts search results
public record SearchResultDto(
    string Query,
    int TotalResults,
    int Page,
    IReadOnlyList<CitedSectionDto> Results
);
