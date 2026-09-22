using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

// T-2.3: FR-3's retrieval seam -- vector search (T-2.1) is the primary path, with SQL
// FTS (T-2.2) as a fallback when the vector path throws or comes back empty (Qdrant
// outage, embedding call failure, or a query with no near neighbours in the
// collection). Callers (S-2.1's PromptAssembler) depend on this, not on either search
// implementation directly, so the vector-primary/keyword-fallback policy lives in one
// place.
//
// FR-18 scenario priors: when a curated SCENARIO_MAPPING keyword matches the query,
// the mapped sections are MERGED into the retrieved context (deduped, appended at
// the tail) so the grounding text actually contains the curated statute — the
// mapping's designed role per design.md §3 step 5. Free-tier cross-language recall
// (Bangla query vs English-dominant corpus) makes this merge decisive for the 4
// seeded staple scenarios.
public class RagContextBuilder : IRagContextBuilder
{
    private readonly IVectorSectionSearch _vectorSearch;
    private readonly IKeywordSectionSearch _keywordSearch;
    private readonly IScenarioMappingRepository _scenarioMappingRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActRepository _actRepo;
    private readonly ILogger<RagContextBuilder> _logger;

    public RagContextBuilder(
        IVectorSectionSearch vectorSearch,
        IKeywordSectionSearch keywordSearch,
        IScenarioMappingRepository scenarioMappingRepo,
        IActSectionRepository sectionRepo,
        IActRepository actRepo,
        ILogger<RagContextBuilder> logger)
    {
        _vectorSearch = vectorSearch;
        _keywordSearch = keywordSearch;
        _scenarioMappingRepo = scenarioMappingRepo;
        _sectionRepo = sectionRepo;
        _actRepo = actRepo;
        _logger = logger;
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

        if (vectorResults.Count == 0)
        {
            // maxResults on the keyword path mirrors topK -- RagContextBuilder's contract is
            // "up to topK sections", not "up to FtsPoolSize"; SearchService's larger pool
            // size is a T-2.4-specific concern for paginating standalone search results.
            vectorResults = (await _keywordSearch.SearchAsync(query, maxResults: topK)).ToList();
        }

        return await MergeScenarioPriorsAsync(query, vectorResults);
    }

    // Appends curated mapping sections not already in the retrieved set. The
    // merged result stays capped (mapping hits are few by construction —
    // typically 1-4 sections) so the prompt doesn't balloon.
    private async Task<List<RetrievedSection>> MergeScenarioPriorsAsync(
        string query, List<RetrievedSection> retrieved)
    {
        try
        {
            // Containment direction matters: the QUERY contains the keyword
            // ("গার্মেন্টসে ৩ মাস বেতন পাইনি" contains "বেতন বাকি"? no — it
            // contains "বেতন দেয়নি"... we check query.Contains(keyword)),
            // unlike ScenarioMappingRepository.SearchByKeywordAsync which
            // matches keyword-fragment-inside-query via SQL LIKE and only
            // works for short fragments. Load all mappings (26 rows) and
            // filter in memory — trivial cost, correct semantics.
            var allMappings = (await _scenarioMappingRepo.GetAllAsync()).ToList();
            var hits = allMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.ScenarioKeyword)
                            && query.Contains(m.ScenarioKeyword, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.SectionId)
                .Distinct()
                .ToList();
            if (hits.Count == 0) return retrieved;

            var existingIds = retrieved.Select(r => r.SectionId).ToHashSet();
            var sections = await _sectionRepo.GetBySectionIdsAsync(hits);
            var sectionList = sections.ToList();
            var acts = (await _actRepo.GetAllAsync()).ToList();

            foreach (var s in sectionList)
            {
                if (existingIds.Contains(s.SectionId)) continue;
                var act = acts.FirstOrDefault(a => a.ActId == s.ActId);
                retrieved.Add(new RetrievedSection(
                    s.SectionId,
                    act?.Title ?? string.Empty,
                    s.SectionNumber ?? string.Empty,
                    s.SectionText,
                    1.0f, // curated prior — full relevance, method marks the origin
                    MuktoAin.Domain.Enums.RetrievalMethod.Keyword,
                    act?.ActNumber ?? string.Empty,
                    act?.Year ?? 0));
                existingIds.Add(s.SectionId);
            }
        }
        catch (Exception ex)
        {
            // Prior merge is an enhancement, never a blocker — but never silent.
            _logger.LogWarning("Scenario prior merge failed: {Message}", ex.Message);
        }
        return retrieved;
    }
}
