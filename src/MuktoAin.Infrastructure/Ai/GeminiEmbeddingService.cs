using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Thin wrapper over GeminiClient for the embedding model.
/// Kept as a separate service because other services depend on IEmbeddingService,
/// not IAiService — the embedding and generation models may diverge later.
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly GeminiClient _client;

    public GeminiEmbeddingService(GeminiClient client)
    {
        _client = client;
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default) =>
        _client.EmbedContentAsync(text, ct);

    public Task<IReadOnlyList<float[]>> GetBatchEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        _client.BatchEmbedContentAsync(texts, ct);
}
