using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (QdrantVectorStore).
public interface IVectorStore
{
    Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload);
    Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK);
    Task DeleteAsync(string vectorId);
}
