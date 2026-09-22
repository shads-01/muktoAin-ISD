using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActSectionChunkRepository : IRepository<ActSectionChunk>
{
    Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAsync(int batchSize);

    /// <summary>Keyset-paged un-embedded rows after a ChunkId — lets the global
    /// dedupe pass stream the whole pending set in constant memory.</summary>
    Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAfterAsync(int afterChunkId, int batchSize, CancellationToken ct = default);

    /// <summary>Cheap COUNT against the filtered index (WHERE VectorId IS NULL)
    /// so the EmbeddingBatchJob can compute a real ETA instead of guessing.</summary>
    Task<int> GetUnembeddedCountAsync();

    Task UpdateEmbeddingInfoAsync(int chunkId, string vectorId, string contentHash);
    Task UpdateBatchEmbeddingInfoAsync(IReadOnlyList<(int chunkId, string vectorId, string contentHash)> updates, CancellationToken ct = default)
    {
        return Task.WhenAll(updates.Select(u => UpdateEmbeddingInfoAsync(u.chunkId, u.vectorId, u.contentHash)));
    }

    /// <summary>T-3.1: every chunk of an act in any embed state — the re-index
    /// scan and the pre-delete Qdrant vector cleanup both stream from here.</summary>
    Task<IEnumerable<ActSectionChunk>> GetByActIdAsync(int actId);

    /// <summary>T-3.1 staleness stamp: clears the Qdrant pointer so the
    /// EmbeddingBatchJob's "VectorId IS NULL" work query re-embeds the chunk,
    /// and stores the recomputed hash. ExecuteUpdateAsync is not translatable
    /// on InMemory — T-3.3 integration coverage.</summary>
    Task MarkStaleAsync(int chunkId, string contentHash);
}
