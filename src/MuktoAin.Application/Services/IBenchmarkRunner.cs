using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkRunner
{
    Task<BenchmarkRunResultDto> RunAsync(BenchmarkRunOptions options, CancellationToken ct = default);

    // S-3.3: compares the two variant reports and writes the comparison JSON.
    Task<BenchmarkComparisonDto> CompareAsync(
        string zeroShotResultsPath,
        string fewShotResultsPath,
        string? outputPath = null,
        CancellationToken ct = default);
}
