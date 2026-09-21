namespace MuktoAin.Application.DTOs;

// Zero-shot vs few-shot comparison (S-3.3) — serialized to
// data/benchmark/results/comparison-zero-shot-vs-few-shot.json.
public sealed record BenchmarkCategoryComparisonDto(
    string Category,
    double ZeroShotMeanF1,
    double FewShotMeanF1,
    double MeanF1Delta);

public sealed record BenchmarkComparisonDto(
    string ZeroShotResultsPath,
    string FewShotResultsPath,
    double ZeroShotMeanF1,
    double FewShotMeanF1,
    double MeanF1Delta,
    int QuestionsImproved,
    int QuestionsRegressed,
    IReadOnlyList<BenchmarkCategoryComparisonDto> Categories)
{
    // Set by CompareAsync after writing the report file.
    public string OutputPath { get; set; } = string.Empty;
}
