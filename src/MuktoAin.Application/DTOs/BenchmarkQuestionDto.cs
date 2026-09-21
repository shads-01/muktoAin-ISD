namespace MuktoAin.Application.DTOs;

// One canonical benchmark question, mapped from a raw
// momahadi/bangladesh-legal-qa-dataset row (schema: data/README.md §2.2).
public sealed record BenchmarkQuestionDto(
    int DatasetId,
    string Question,
    string Language,
    string Category,
    string QuestionType,
    string Difficulty,
    IReadOnlyList<string> ExpectedSectionReferences,
    string? GoldAnswer,
    string? IracReasoningJson);
