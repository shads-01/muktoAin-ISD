namespace MuktoAin.Domain.Entities;

public class ActSectionChunk
{
    public int ChunkId { get; set; }
    public int SectionId { get; set; }
    public ActSection Section { get; set; } = null!;

    // 1st, 2nd... slice within a long section
    public short ChunkOrder { get; set; }

    // ~300-500 token sub-chunk (or full section if unsplit)
    public string ChunkText { get; set; } = string.Empty;
    public int TokenCount { get; set; }

    // Qdrant point UUID/ID; null until Shads's EmbeddingBatchJob fills it in
    public string? VectorId { get; set; }

    // SHA-256 hash for incremental re-indexing
    public string? ContentHash { get; set; }
    public DateTime? LastEmbeddedAt { get; set; }
}
