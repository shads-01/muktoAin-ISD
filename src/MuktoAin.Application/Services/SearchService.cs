using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

// FR-7: standalone Acts search page (/Search) -- citizens searching the corpus
// directly, independent of the AI "explain my rights" flow. Wraps
// IKeywordSectionSearch (Infrastructure's SQL FTS) with pagination, optional
// filtering to a single Act, and DTO formatting.
public class SearchService
{
    private const int FtsPoolSize = 100;

    private readonly IKeywordSectionSearch _keywordSearch;
    private readonly IActRepository _actRepo;

    public SearchService(IKeywordSectionSearch keywordSearch, IActRepository actRepo)
    {
        _keywordSearch = keywordSearch;
        _actRepo = actRepo;
    }

    public async Task<SearchResultDto> SearchActsAsync(string query, int page = 1, int pageSize = 20, int? actId = null)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var results = (await _keywordSearch.SearchAsync(query, maxResults: FtsPoolSize)).ToList();

        if (actId.HasValue)
        {
            // RetrievedSection carries ActTitle, not ActId, so filtering to one Act
            // means resolving that Act's section ids first and intersecting on those.
            var act = await _actRepo.GetWithSectionsAsync(actId.Value);
            var sectionIds = act?.Sections.Select(s => s.SectionId).ToHashSet() ?? new HashSet<int>();
            results = results.Where(r => sectionIds.Contains(r.SectionId)).ToList();
        }

        var paged = results.Skip((page - 1) * pageSize).Take(pageSize);

        return new SearchResultDto(
            Query: query,
            TotalResults: results.Count,
            Page: page,
            Results: paged.Select(r => new CitedSectionDto(
                r.SectionId, r.ActTitle, r.SectionNumber, r.SectionText,
                r.RelevanceScore, r.Method.ToString(), r.ActNumber, r.ActYear)).ToList());
    }
}
