using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class ActSectionChunkRepository : Repository<ActSectionChunk>, IActSectionChunkRepository
{
    public ActSectionChunkRepository(AppDbContext context) : base(context) { }

    // Matches the filtered index IX_ACT_SECTION_CHUNK_VectorId_Null
    // (WHERE VectorId IS NULL) already in scripts/02_schema.sql -- this is what
    // the EmbeddingBatchJob (S-1.8) polls to find work.
    //
    // No Include(): payload building only needs ChunkId/SectionId/ChunkOrder/
    // ChunkText, all on the chunk row itself. Section/Act data in the Qdrant
    // payload was dead weight -- SimilaritySearchService re-hydrates full
    // sections from SQL and only reads SectionId from the vector payload.
    // Dropping the join also cuts fetch latency and tracked-entity graph size.
    public async Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAsync(int batchSize)
        => await _dbSet
            .AsNoTracking()
            .Where(c => c.VectorId == null)
            .OrderBy(c => c.ChunkId)
            .Take(batchSize)
            .ToListAsync();

    public async Task<int> GetUnembeddedCountAsync()
        => await _dbSet
            .AsNoTracking()
            .Where(c => c.VectorId == null)
            .CountAsync();

    // Keyset pagination for the global dedupe pass: WHERE VectorId IS NULL AND
    // ChunkId > afterChunkId ORDER BY ChunkId — same filtered index, constant
    // memory regardless of how many rows are pending.
    public async Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAfterAsync(int afterChunkId, int batchSize, CancellationToken ct = default)
        => await _dbSet
            .AsNoTracking()
            .Where(c => c.VectorId == null && c.ChunkId > afterChunkId)
            .OrderBy(c => c.ChunkId)
            .Take(batchSize)
            .ToListAsync(ct);

    // Single round-trip UPDATE via EF Core's ExecuteUpdateAsync rather than
    // fetch-then-save -- this only ever touches 2 columns on one known row, so
    // there's no need to load the entity into the change tracker first.
    public async Task UpdateEmbeddingInfoAsync(int chunkId, string vectorId, string contentHash)
    {
        await _dbSet
            .Where(c => c.ChunkId == chunkId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.VectorId, vectorId)
                .SetProperty(c => c.ContentHash, contentHash)
                .SetProperty(c => c.LastEmbeddedAt, DateTime.UtcNow));
    }

    public async Task UpdateBatchEmbeddingInfoAsync(IReadOnlyList<(int chunkId, string vectorId, string contentHash)> updates, CancellationToken ct = default)
    {
        if (updates.Count == 0) return;

        var now = DateTime.UtcNow;

        if (_context.Database.IsSqlServer())
        {
            // Direct parameterized batch UPDATE with ROWLOCK to avoid page/table lock escalation and change tracker overhead
            foreach (var batch in updates.Chunk(25))
            {
                var sb = new System.Text.StringBuilder();
                var parameters = new List<object>();
                int idx = 0;
                foreach (var (chunkId, vectorId, contentHash) in batch)
                {
                    sb.AppendLine($"UPDATE [dbo].[ACT_SECTION_CHUNK] WITH (ROWLOCK) SET [VectorId] = @p{idx}, [ContentHash] = @p{idx + 1}, [LastEmbeddedAt] = @p{idx + 2} WHERE [ChunkId] = @p{idx + 3};");
                    parameters.Add(new Microsoft.Data.SqlClient.SqlParameter($"@p{idx}", vectorId));
                    parameters.Add(new Microsoft.Data.SqlClient.SqlParameter($"@p{idx + 1}", contentHash));
                    parameters.Add(new Microsoft.Data.SqlClient.SqlParameter($"@p{idx + 2}", now));
                    parameters.Add(new Microsoft.Data.SqlClient.SqlParameter($"@p{idx + 3}", chunkId));
                    idx += 4;
                }

                await _context.Database.ExecuteSqlRawAsync(sb.ToString(), parameters, ct);
            }
        }
        else
        {
            foreach (var (chunkId, vectorId, contentHash) in updates)
            {
                var chunk = new ActSectionChunk
                {
                    ChunkId = chunkId,
                    VectorId = vectorId,
                    ContentHash = contentHash,
                    LastEmbeddedAt = now
                };
                _context.ActSectionChunks.Attach(chunk);
                var entry = _context.Entry(chunk);
                entry.Property(c => c.VectorId).IsModified = true;
                entry.Property(c => c.ContentHash).IsModified = true;
                entry.Property(c => c.LastEmbeddedAt).IsModified = true;
            }

            await _context.SaveChangesAsync(ct);
        }
    }
}
