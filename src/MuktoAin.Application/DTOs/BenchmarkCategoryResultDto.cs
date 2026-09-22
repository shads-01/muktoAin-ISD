namespace MuktoAin.Application.DTOs;

public sealed class BenchmarkCategoryResultDto
{
    public string Category { get; set; } = string.Empty;

    public int QuestionCount { get; set; }

    public double MeanPrecision { get; set; }

    public double MeanRecall { get; set; }

    public double MeanF1 { get; set; }
}
