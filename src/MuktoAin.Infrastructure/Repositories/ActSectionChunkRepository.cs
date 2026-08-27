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
    // Shads's EmbeddingBatchJob (S-1.8) polls to find work.
    public async Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAsync(int batchSize)
        => await _dbSet
            .Include(c => c.Section)
                .ThenInclude(s => s.Act)
            .Where(c => c.VectorId == null)
            .Take(batchSize)
            .ToListAsync();

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
}
