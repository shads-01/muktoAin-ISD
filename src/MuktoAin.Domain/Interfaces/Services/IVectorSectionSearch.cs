using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (SimilaritySearchService, Qdrant via IVectorStore +
// GeminiEmbeddingService via IEmbeddingService). RagContextBuilder (T-2.3) depends on
// this abstraction, not on Infrastructure, so it can try vector search first and fall
// back to IKeywordSectionSearch when this is unavailable or returns nothing.
public interface IVectorSectionSearch
{
    Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int topK = 8);
}
