namespace MuktoAin.Domain.Interfaces;

/// <summary>
/// Abstraction over the embedding model used for vector indexing and retrieval.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
}
