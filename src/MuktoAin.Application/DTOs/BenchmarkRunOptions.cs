using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public sealed record BenchmarkRunOptions
{
    public BenchmarkPromptVariant Variant { get; init; } = BenchmarkPromptVariant.ZeroShot;

    // Null/absent = run the whole dataset.
    public int? MaxQuestions { get; init; }

    // Null = auto-resolve (full dataset, falling back to the committed sample).
    public string? DatasetPath { get; init; }

    // Null = data/benchmark/results/<variant>.json under the repo root.
    public string? OutputPath { get; init; }

    public int TopK { get; init; } = 8;

    // Gemini free-tier pacing for full 2,165-question runs (e.g. 500).
    public int DelayBetweenQuestionsMs { get; init; }
}
