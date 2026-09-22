using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
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

    // FR-7 extension: browsing an Act with no keyword (e.g. the Search page's Act
    // dropdown used on its own). No FTS involved -- just the Act's own sections, in
    // their real statutory order (OrdinalPosition; ActSection.SectionNumber is left
    // null for the entire corpus by ActImportService -- see SectionNumberResolver --
    // so it can't be used to tell real sections from anything else, and isn't a
    // reliable filter key at all: filtering on it excludes every row).
    public async Task<SearchResultDto> BrowseActAsync(int actId, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : pageSize;

        var act = await _actRepo.GetWithSectionsAsync(actId);
        var sections = (act?.Sections ?? new List<ActSection>())
            .OrderBy(s => s.OrdinalPosition)
            .ToList();

        var paged = sections.Skip((page - 1) * pageSize).Take(pageSize);

        return new SearchResultDto(
            Query: string.Empty,
            TotalResults: sections.Count,
            Page: page,
            Results: paged.Select(s => new CitedSectionDto(
                s.SectionId, act!.Title, SectionNumberResolver.Resolve(s.SectionNumber, s.SectionText), s.SectionText,
                RelevanceScore: 0f, RetrievalMethod: "Browse", act.ActNumber, act.Year)).ToList());
    }
}
