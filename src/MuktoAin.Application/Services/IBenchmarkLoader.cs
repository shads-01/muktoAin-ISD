using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

public interface IBenchmarkLoader
{
    Task<BenchmarkDataset> LoadAsync(string? datasetPath = null, CancellationToken ct = default);
}
