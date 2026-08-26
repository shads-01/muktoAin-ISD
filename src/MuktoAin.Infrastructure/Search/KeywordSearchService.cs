using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Infrastructure.Search;

// T-2.2: SQL Server Full-Text Search over ACT_SECTION (index built in T-1.10).
// Two callers:
//   - FR-7 standalone search: SearchService (Application) wraps this for citizens
//     browsing/searching the Acts corpus directly.
//   - FR-3 fallback: RagContextBuilder (T-2.3) calls this when the Qdrant vector
//     path (IVectorSectionSearch) is down or returns nothing.
public class KeywordSearchService : IKeywordSectionSearch
{
    private readonly IActSectionRepository _sectionRepo;

    public KeywordSearchService(IActSectionRepository sectionRepo)
    {
        _sectionRepo = sectionRepo;
    }

    public async Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        var ftsQuery = SanitizeForFts(query);
        var sections = await _sectionRepo.FullTextSearchAsync(ftsQuery, maxResults);

        // FTS doesn't hand back a usable relevance score through FromSqlInterpolated's
        // entity projection (CONTAINSTABLE's RANK column is consumed inside the query
        // for ORDER BY only), so keyword results carry 0f here. RagContextBuilder treats
        // vector results as primary and only reaches this fallback when vector search
        // is unavailable, so cross-method score comparison isn't a live requirement yet.
        return sections.Select(s => new RetrievedSection(
            s.SectionId,
            s.Act.Title,
            s.SectionNumber ?? string.Empty,
            s.SectionText,
            0f,
            RetrievalMethod.Keyword));
    }

    private static string SanitizeForFts(string query)
    {
        // CONTAINSTABLE's query string is itself a mini boolean-search grammar
        // ("word" OR "word", NEAR, FORMSOF, etc.) -- passing raw user input through
        // lets a bare double-quote or an operator character break the query or change
        // its meaning. Strip double quotes (the only character that can terminate our
        // wrapping phrase early) and wrap the whole term as one exact phrase search.
        // No stemmer covers Bangla in SQL Server, so exact-phrase is also the correct
        // behavior for Bangla terms; English terms lose FORMSOF(INFLECTIONAL, ...)
        // stemming as a result -- acceptable for T-2.2's scope, revisit if English
        // recall turns out to matter.
        var cleaned = query.Replace("\"", string.Empty).Trim();
        return $"\"{cleaned}\"";
    }
}
