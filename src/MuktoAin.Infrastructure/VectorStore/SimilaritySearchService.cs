using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Infrastructure.Common;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.Infrastructure.VectorStore;

// T-2.1: Qdrant vector retrieval. Embeds the query with Gemini (IEmbeddingService),
// searches the collection EmbeddingBatchJob (S-1.8) populated, then re-hydrates full
// ActSection rows from SQL Server -- Qdrant's payload only carries what
// EmbeddingBatchJob stashed there (ChunkId/SectionId/ChunkOrder/ChunkText[/
// SectionNumber/ActTitle]), not the source-of-truth entity text/title.
//
// RagContextBuilder (T-2.3) is the only intended caller: it treats this as the primary
// path and falls back to IKeywordSectionSearch (T-2.2) when this throws or returns
// nothing.
public class SimilaritySearchService : IVectorSectionSearch
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IActSectionRepository _sectionRepo;

    public SimilaritySearchService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IActSectionRepository sectionRepo)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _sectionRepo = sectionRepo;
    }

    public async Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int topK = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        var queryVector = await _embeddingService.GetEmbeddingAsync(query);
        var vectorResults = (await _vectorStore.SearchAsync(queryVector, topK)).ToList();
        if (vectorResults.Count == 0)
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        // LegalChunkingService (T-1.9) splits a section into multiple chunks, so it's
        // common for several of the top-K hits to point at the same ACT_SECTION --
        // keep only the highest-scoring hit per section rather than returning the same
        // section text more than once inside a topK-sized result set.
        var bestPerSection = new Dictionary<int, VectorSearchResult>();
        foreach (var result in vectorResults)
        {
            if (!result.Payload.TryGetValue("SectionId", out var rawSectionId) ||
                !int.TryParse(rawSectionId, out var sectionId))
            {
                continue; // Defensive: malformed/legacy payload without a SectionId.
            }

            if (!bestPerSection.TryGetValue(sectionId, out var existing) ||
                result.Score > existing.Score)
            {
                bestPerSection[sectionId] = result;
            }
        }

        if (bestPerSection.Count == 0)
        {
            return Enumerable.Empty<RetrievedSection>();
        }

        var sections = (await _sectionRepo.GetBySectionIdsAsync(bestPerSection.Keys))
            .ToDictionary(s => s.SectionId);

        return bestPerSection
            .Where(kv => sections.ContainsKey(kv.Key))
            .Select(kv => ToRetrievedSection(sections[kv.Key], kv.Value.Score))
            .OrderByDescending(r => r.RelevanceScore)
            .ToList();
    }

    private static RetrievedSection ToRetrievedSection(ActSection section, float score) =>
        new(
            section.SectionId,
            section.Act.Title,
            SectionNumberResolver.Resolve(section.SectionNumber, section.SectionText),
            section.SectionText,
            score,
            RetrievalMethod.Vector,
            section.Act.ActNumber,
            section.Act.Year);
}
