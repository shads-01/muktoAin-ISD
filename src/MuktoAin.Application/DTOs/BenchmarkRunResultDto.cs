namespace MuktoAin.Application.DTOs;

// One benchmark run (zero-shot or few-shot) — serialized to
// data/benchmark/results/*.json and re-read by CompareAsync (Task 6), hence
// plain settable properties rather than positional records.
public sealed class BenchmarkRunResultDto
{
    public string Variant { get; set; } = string.Empty;

    public string DatasetPath { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public int TotalQuestions { get; set; }

    public int ScoredQuestions { get; set; }

    public int SkippedQuestions { get; set; }

    public double MeanPrecision { get; set; }

    public double MeanRecall { get; set; }

    public double MeanF1 { get; set; }

    public List<BenchmarkCategoryResultDto> Categories { get; set; } = new();

    public List<BenchmarkQuestionScoreDto> Questions { get; set; } = new();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}
