using MuktoAin.Domain.Models;

namespace MuktoAin.Domain.Interfaces.Services;

// Implemented in Infrastructure (KeywordSearchService, SQL Server FTS via
// IActSectionRepository.FullTextSearchAsync). RagContextBuilder (T-2.3) depends on this
// abstraction, not on Infrastructure, so it can fall back to keyword search when the
// vector path (IVectorSectionSearch) is unavailable.
public interface IKeywordSectionSearch
{
    Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int maxResults = 20);
}
