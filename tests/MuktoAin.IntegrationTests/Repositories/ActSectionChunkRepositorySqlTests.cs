using Microsoft.EntityFrameworkCore;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// T-3.3: covers ActSectionChunkRepository's SQL-Server-only write paths that
// the T-1.14 EF InMemory unit tests excluded:
//   - UpdateEmbeddingInfoAsync      -> ExecuteUpdateAsync
//   - UpdateBatchEmbeddingInfoAsync -> ExecuteSqlRawAsync batch UPDATE with
//     ROWLOCK over Chunk(25) windows (the SQL Server branch of the
//     IsSqlServer() guard)
// Read paths (GetUnembedded*) also run here to prove the filtered index
// IX_ACT_SECTION_CHUNK_VectorId_Null behaves as the EmbeddingBatchJob expects.
[Collection("MuktoAinSqlDb")]
public class ActSectionChunkRepositorySqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public ActSectionChunkRepositorySqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task UpdateEmbeddingInfoAsync_Updates_Row_Without_Loading_Entity()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Chunked section for embedding tests.");
        var chunkId = await _fx.SeedChunkAsync(sectionId, "Chunk one text.");

        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx)
                .UpdateEmbeddingInfoAsync(chunkId, "vec-it-001", "hash-abc");
        }

        await using var verify = _fx.CreateContext();
        var chunk = await verify.ActSectionChunks
            .AsNoTracking()
            .SingleAsync(c => c.ChunkId == chunkId);
        Assert.Equal("vec-it-001", chunk.VectorId);
        // ACT_SECTION_CHUNK.ContentHash is CHAR(64) (fixed-width, sized for a
        // real SHA-256 hex digest) -- fake short test hashes come back
        // space-padded by SQL Server's CHAR semantics; TrimEnd() compensates.
        Assert.Equal("hash-abc", chunk.ContentHash?.TrimEnd());
        Assert.NotNull(chunk.LastEmbeddedAt); // ExecuteUpdateAsync stamps UtcNow server-side
    }

    [SkippableFact]
    public async Task UpdateEmbeddingInfoAsync_Removes_Chunk_From_Unembedded_Query()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Another chunked section.");
        var embeddedId = await _fx.SeedChunkAsync(sectionId, "Will be embedded.");
        var pendingId = await _fx.SeedChunkAsync(sectionId, "Stays pending.");

        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx)
                .UpdateEmbeddingInfoAsync(embeddedId, "vec-it-002", "hash-def");
        }

        await using var verify = _fx.CreateContext();
        var pending = (await new ActSectionChunkRepository(verify)
            .GetUnembeddedChunksAsync(batchSize: 100)).ToList();
        Assert.Contains(pending, c => c.ChunkId == pendingId);
        Assert.DoesNotContain(pending, c => c.ChunkId == embeddedId);
    }

    [SkippableFact]
    public async Task GetUnembeddedChunksAsync_Returns_Only_Null_VectorId_Rows_In_ChunkId_Order()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for the ordering test.");
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await _fx.SeedChunkAsync(sectionId, $"Chunk {i}."));
        }
        await _fx.SeedChunkAsync(sectionId, "Already embedded.", vectorId: "vec-pre");

        await using var ctx = _fx.CreateContext();
        var results = (await new ActSectionChunkRepository(ctx)
            .GetUnembeddedChunksAsync(batchSize: 100)).ToList();

        // Filter to this test's rows: the shared DB accumulates rows across tests.
        var ours = results.Where(c => ids.Contains(c.ChunkId)).Select(c => c.ChunkId).ToList();
        Assert.Equal(ids.OrderBy(i => i), ours);
        Assert.All(results, c => Assert.Null(c.VectorId));
    }

    [SkippableFact]
    public async Task GetUnembeddedChunksAfterAsync_Uses_Keyset_Pagination()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for keyset pagination.");
        var ids = new List<int>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add(await _fx.SeedChunkAsync(sectionId, $"Keyset chunk {i}."));
        }

        await using var ctx = _fx.CreateContext();
        var page = (await new ActSectionChunkRepository(ctx)
            .GetUnembeddedChunksAfterAsync(afterChunkId: ids[1], batchSize: 100)).ToList();

        Assert.All(page, c => Assert.True(c.ChunkId > ids[1]));
        var ours = page.Where(c => ids.Contains(c.ChunkId)).Select(c => c.ChunkId).ToList();
        Assert.Equal(ids.Skip(2).ToList(), ours); // ids[0..1] excluded by the keyset
    }

    [SkippableFact]
    public async Task UpdateBatchEmbeddingInfoAsync_Applies_All_Updates_Across_Rowlock_Batches()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (_, sectionId) = await _fx.SeedActWithSectionAsync("Section for the 60-row batch test.");
        var chunkIds = new List<int>();
        for (var i = 0; i < 60; i++)
        {
            chunkIds.Add(await _fx.SeedChunkAsync(sectionId, $"Batch chunk {i}."));
        }

        // 60 updates -> the Chunk(25) loop issues 3 ExecuteSqlRawAsync batches
        // (25 + 25 + 10). Every parameterized UPDATE must land.
        var updates = chunkIds
            .Select((id, i) => (chunkId: id, vectorId: $"vec-batch-{i:D3}", contentHash: $"hash-{i:D3}"))
            .ToList();
        await using (var ctx = _fx.CreateContext())
        {
            await new ActSectionChunkRepository(ctx).UpdateBatchEmbeddingInfoAsync(updates);
        }

        await using var verify = _fx.CreateContext();
        foreach (var (chunkId, vectorId, contentHash) in updates)
        {
            var chunk = await verify.ActSectionChunks
                .AsNoTracking()
                .SingleAsync(c => c.ChunkId == chunkId);
            Assert.Equal(vectorId, chunk.VectorId);
            Assert.Equal(contentHash, chunk.ContentHash?.TrimEnd()); // CHAR(64) padding, see note above
            Assert.NotNull(chunk.LastEmbeddedAt);
        }
        var stillPending = await verify.ActSectionChunks
            .CountAsync(c => c.SectionId == sectionId && c.VectorId == null);
        Assert.Equal(0, stillPending);
    }

    [SkippableFact]
    public async Task UpdateBatchEmbeddingInfoAsync_Empty_List_Is_A_NoOp()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        await using var ctx = _fx.CreateContext();
        // Must not throw and must not open a doomed ExecuteSqlRawAsync with an
        // empty statement string.
        await new ActSectionChunkRepository(ctx).UpdateBatchEmbeddingInfoAsync(new List<(int, string, string)>());
    }
}
