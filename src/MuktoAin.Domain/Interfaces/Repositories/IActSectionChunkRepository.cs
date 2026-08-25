using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActSectionChunkRepository : IRepository<ActSectionChunk>
{
    Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAsync(int batchSize);
    Task UpdateEmbeddingInfoAsync(int chunkId, string vectorId, string contentHash);
}
