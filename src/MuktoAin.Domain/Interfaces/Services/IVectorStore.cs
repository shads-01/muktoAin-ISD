using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (QdrantVectorStore).
public interface IVectorStore
{
    Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload);
    Task UpsertBatchAsync(IReadOnlyList<(string vectorId, float[] embedding, Dictionary<string, string> payload)> points)
    {
        return Task.WhenAll(points.Select(p => UpsertAsync(p.vectorId, p.embedding, p.payload)));
    }
    Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK);
    Task DeleteAsync(string vectorId);
}
