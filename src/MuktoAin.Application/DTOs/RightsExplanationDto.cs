namespace MuktoAin.Application.DTOs;

// AI output for "Explain My Rights"
public record RightsExplanationDto(
    string Explanation,
    IReadOnlyList<CitedSectionDto> CitedSections,
    string Disclaimer
);

public record CitedSectionDto(
    int SectionId,
    string ActTitle,
    string SectionNumber,
    string SectionText,
    float RelevanceScore,
    string RetrievalMethod
);
