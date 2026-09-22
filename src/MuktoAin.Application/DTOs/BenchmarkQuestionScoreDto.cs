namespace MuktoAin.Application.DTOs;

public sealed class BenchmarkQuestionScoreDto
{
    public int DatasetId { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    // Final response (disclaimer already injected by DisclaimerInjector).
    public string Response { get; set; } = string.Empty;

    // Non-null => this question failed mid-pipeline and was skipped.
    public string? Error { get; set; }

    public List<string> CitedReferences { get; set; } = new();

    public List<string> ExpectedReferences { get; set; } = new();

    public double Precision { get; set; }

    public double Recall { get; set; }

    public double F1 { get; set; }
}
