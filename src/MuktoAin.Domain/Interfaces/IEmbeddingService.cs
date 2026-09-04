namespace MuktoAin.Domain.Interfaces;

/// <summary>
/// Abstraction over the embedding model used for vector indexing and retrieval.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    Task<IReadOnlyList<float[]>> GetBatchEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        return Task.WhenAll(texts.Select(t => GetEmbeddingAsync(t, ct))).ContinueWith(t => (IReadOnlyList<float[]>)t.Result);
    }
}
