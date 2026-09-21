using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkRunner
{
    Task<BenchmarkRunResultDto> RunAsync(BenchmarkRunOptions options, CancellationToken ct = default);
}
