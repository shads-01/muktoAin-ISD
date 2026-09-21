namespace MuktoAin.Application.DTOs;

public sealed record BenchmarkDataset(
    IReadOnlyList<BenchmarkQuestionDto> Questions,
    string SourcePath,
    int SkippedRowCount);
