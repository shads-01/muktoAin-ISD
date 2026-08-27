using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

// T-2.3: FR-3's retrieval seam -- vector search (T-2.1) is the primary path, with SQL
// FTS (T-2.2) as a fallback when the vector path throws or comes back empty (Qdrant
// outage, embedding call failure, or a query with no near neighbours in the
// collection). Callers (S-2.1's PromptAssembler) depend on this, not on either search
// implementation directly, so the vector-primary/keyword-fallback policy lives in one
// place.
public class RagContextBuilder : IRagContextBuilder
{
    private readonly IVectorSectionSearch _vectorSearch;
    private readonly IKeywordSectionSearch _keywordSearch;

    public RagContextBuilder(IVectorSectionSearch vectorSearch, IKeywordSectionSearch keywordSearch)
    {
        _vectorSearch = vectorSearch;
        _keywordSearch = keywordSearch;
    }

    public async Task<IEnumerable<RetrievedSection>> RetrieveContextAsync(string query, int topK = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        List<RetrievedSection> vectorResults;
        try
        {
            vectorResults = (await _vectorSearch.SearchAsync(query, topK)).ToList();
        }
        catch
        {
            // Qdrant/embedding-call failure -- treat exactly like an empty result and
            // fall through to keyword search rather than surfacing the outage to the
            // caller. SimilaritySearchService already swallows its own expected empty
            // cases; this catch is for the unexpected ones (network, API errors).
            vectorResults = new List<RetrievedSection>();
        }

        if (vectorResults.Count > 0)
        {
            return vectorResults;
        }

        // maxResults on the keyword path mirrors topK -- RagContextBuilder's contract is
        // "up to topK sections", not "up to FtsPoolSize"; SearchService's larger pool
        // size is a T-2.4-specific concern for paginating standalone search results.
        return await _keywordSearch.SearchAsync(query, maxResults: topK);
    }
}
