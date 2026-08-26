using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Seeding;

// Splits ACT_SECTION.SectionText into ~300-500 token ACT_SECTION_CHUNK rows (T-1.9).
// VectorId/ContentHash are left null here -- Shads's EmbeddingBatchJob (S-1.8) fills
// them in once chunks are embedded into Qdrant.
//
// Token counting is a rough heuristic (Length/3), not an exact tokenizer -- it only
// needs to answer "is this section long enough to need splitting", not produce a
// billing-accurate count. Splitting prefers paragraph breaks, then sentence breaks,
// then falls back to the nearest word boundary, with a fixed character overlap so
// retrieval doesn't miss context that spans a split point.
public static class LegalChunkingService
{
    private const int TokenCharRatio = 3;      // rough chars-per-token for mixed Bangla/English text
    private const int MaxTokensBeforeSplit = 500;
    private const int TargetChunkTokens = 400;
    private const int OverlapTokens = 50;
    private const int BatchSize = 200;

    // A found boundary must land at least this far into the target window to be
    // used; otherwise a run of text with sparse/no boundaries (e.g. scraping
    // artifacts that glue words together) would keep matching the same distant
    // boundary as `start` inches forward, producing thousands of near-duplicate,
    // ever-shrinking chunks instead of properly advancing through the text.
    private const double MinAcceptableBoundaryFraction = 0.5;

    public static async Task ChunkAsync(AppDbContext context, ILogger? logger = null)
    {
        // NOT EXISTS-style query via the nav collection -- scales fine even once
        // most sections already have chunks, unlike pulling every chunked SectionId
        // into memory to check membership client-side.
        var pendingSectionIds = await context.ActSections
            .Where(s => !s.Chunks.Any())
            .Select(s => s.SectionId)
            .ToListAsync();

        if (pendingSectionIds.Count == 0)
        {
            logger?.LogInformation("LegalChunkingService: all sections already chunked, nothing to do.");
            return;
        }

        var chunkedSections = 0;
        var totalChunks = 0;

        foreach (var batch in pendingSectionIds.Chunk(BatchSize))
        {
            var sections = await context.ActSections
                .Where(s => batch.Contains(s.SectionId))
                .ToListAsync();

            foreach (var section in sections)
            {
                short order = 1;
                foreach (var chunkText in SplitIntoChunks(section.SectionText))
                {
                    context.ActSectionChunks.Add(new ActSectionChunk
                    {
                        SectionId = section.SectionId,
                        ChunkOrder = order++,
                        ChunkText = chunkText,
                        TokenCount = EstimateTokenCount(chunkText),
                        VectorId = null,
                        ContentHash = null,
                        LastEmbeddedAt = null
                    });
                    totalChunks++;
                }
                chunkedSections++;
            }

            await context.SaveChangesAsync();
            // Keeps the tracked-entity graph from growing across ~35k+ sections in
            // one long-lived DbContext scope.
            context.ChangeTracker.Clear();

            logger?.LogInformation(
                "LegalChunkingService: chunked {Chunked}/{Total} sections so far...",
                chunkedSections, pendingSectionIds.Count);
        }

        logger?.LogInformation(
            "LegalChunkingService: chunked {Chunked} sections into {TotalChunks} chunks.",
            chunkedSections, totalChunks);
    }

    private static int EstimateTokenCount(string text) => Math.Max(1, text.Length / TokenCharRatio);

    private static IEnumerable<string> SplitIntoChunks(string sectionText)
    {
        if (EstimateTokenCount(sectionText) <= MaxTokensBeforeSplit)
        {
            yield return sectionText;
            yield break;
        }

        var targetChars = TargetChunkTokens * TokenCharRatio;
        var overlapChars = OverlapTokens * TokenCharRatio;

        var start = 0;
        while (start < sectionText.Length)
        {
            var remaining = sectionText.Length - start;
            if (remaining <= targetChars)
            {
                yield return sectionText[start..];
                break;
            }

            var splitAt = FindSplitPoint(sectionText, start, start + targetChars);
            yield return sectionText[start..splitAt];

            // Guarantee forward progress even if the found boundary sits close to
            // (or before) the overlap window, to avoid looping forever.
            start = Math.Max(splitAt - overlapChars, start + 1);
        }
    }

    // Prefers a paragraph break, then a sentence break, then the nearest word
    // boundary within [start, idealEnd] -- but only if it's at least halfway into
    // the window; otherwise hard-cuts at idealEnd. Without that floor, text with a
    // long run of no boundaries (e.g. scraping artifacts gluing words together)
    // would keep matching the same distant boundary as `start` inches forward one
    // character at a time, instead of making real progress.
    private static int FindSplitPoint(string text, int start, int idealEnd)
    {
        var clampedEnd = Math.Min(idealEnd, text.Length);
        var window = text[start..clampedEnd];
        var minAcceptable = (int)(window.Length * MinAcceptableBoundaryFraction);

        var paragraphBreak = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraphBreak >= minAcceptable) return start + paragraphBreak + 2;

        var sentenceBreak = window.LastIndexOf(". ", StringComparison.Ordinal);
        if (sentenceBreak >= minAcceptable) return start + sentenceBreak + 2;

        var wordBreak = window.LastIndexOf(' ');
        if (wordBreak >= minAcceptable) return start + wordBreak + 1;

        return clampedEnd;
    }
}
