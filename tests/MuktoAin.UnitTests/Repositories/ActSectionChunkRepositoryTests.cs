using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

// UpdateEmbeddingInfoAsync is NOT covered here: it uses EF Core's ExecuteUpdateAsync,
// which the InMemory provider doesn't support at all (throws InvalidOperationException
// -- confirmed empirically, not just assumed, while writing these tests). Same bucket
// as ActSectionRepository's FromSqlRaw/CONTAINSTABLE methods -- covered in the T-3.3
// integration tests against real SQL Server instead.
public class ActSectionChunkRepositoryTests
{
    private static async Task<ActSection> SeedSectionAsync(MuktoAin.Infrastructure.Data.AppDbContext context)
    {
        var act = new Act { Title = "Act", ActNumber = "I", Year = 2000, PublicationDate = "x", Language = "english", SourceUrl = "x" };
        var section = new ActSection { OrdinalPosition = 1, SectionText = "text" };
        act.Sections.Add(section);
        context.Acts.Add(act);
        await context.SaveChangesAsync();
        return section;
    }

    [Fact]
    public async Task GetUnembeddedChunksAsync_ReturnsOnlyChunksWithNullVectorId()
    {
        using var context = TestDbContextFactory.Create();
        var section = await SeedSectionAsync(context);
        section.Chunks.Add(new ActSectionChunk { ChunkOrder = 1, ChunkText = "chunk 1", VectorId = null });
        section.Chunks.Add(new ActSectionChunk { ChunkOrder = 2, ChunkText = "chunk 2", VectorId = "already-embedded" });
        section.Chunks.Add(new ActSectionChunk { ChunkOrder = 3, ChunkText = "chunk 3", VectorId = null });
        await context.SaveChangesAsync();

        var repo = new ActSectionChunkRepository(context);
        var result = await repo.GetUnembeddedChunksAsync(10);

        Assert.Equal(2, result.Count());
        Assert.All(result, c => Assert.Null(c.VectorId));
    }

    [Fact]
    public async Task GetUnembeddedChunksAsync_RespectsBatchSize()
    {
        using var context = TestDbContextFactory.Create();
        var section = await SeedSectionAsync(context);
        for (var i = 0; i < 5; i++)
        {
            section.Chunks.Add(new ActSectionChunk { ChunkOrder = (short)(i + 1), ChunkText = $"chunk {i}", VectorId = null });
        }
        await context.SaveChangesAsync();

        var repo = new ActSectionChunkRepository(context);
        var result = await repo.GetUnembeddedChunksAsync(3);

        Assert.Equal(3, result.Count());
    }

    [Fact]
    public async Task GetByActIdAsync_ReturnsAllChunksOfTheAct_RegardlessOfEmbedState()
    {
        using var context = TestDbContextFactory.Create();
        var act = new Act { Title = "Labour Act", Year = 2006 };
        var section = new ActSection { Act = act, OrdinalPosition = 1, SectionText = "text" };
        context.Acts.Add(act);
        context.ActSections.Add(section);
        context.ActSectionChunks.AddRange(
            new ActSectionChunk { Section = section, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1", LastEmbeddedAt = DateTime.UtcNow },
            new ActSectionChunk { Section = section, ChunkOrder = 2, ChunkText = "b", TokenCount = 1 });
        await context.SaveChangesAsync();

        var repo = new ActSectionChunkRepository(context);

        var chunks = (await repo.GetByActIdAsync(act.ActId)).ToList();

        Assert.Equal(2, chunks.Count);
    }
}
