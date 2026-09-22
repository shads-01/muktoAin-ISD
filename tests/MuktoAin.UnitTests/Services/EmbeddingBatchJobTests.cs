using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.VectorStore;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.UnitTests.Services;

public class EmbeddingBatchJobTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IServiceScope> _scopeMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly Mock<IActSectionChunkRepository> _chunkRepoMock = new();
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<ILogger<EmbeddingBatchJob>> _loggerMock = new();

    public EmbeddingBatchJobTests()
    {
        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceProviderMock
            .Setup(sp => sp.GetService(typeof(IActSectionChunkRepository)))
            .Returns(_chunkRepoMock.Object);
        _chunkRepoMock
            .Setup(r => r.GetUnembeddedCountAsync())
            .ReturnsAsync(2);
        // Default keyset-paged scan for the global dedupe pass: return the
        // configured pages once, then empty (drained).
        _chunkRepoMock
            .Setup(r => r.GetUnembeddedChunksAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActSectionChunk>());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Embedding:RunOnStartup"] = "true",
            ["Embedding:StartupDelayMs"] = "0",
            ["Embedding:MaxConcurrentBatches"] = "1"
        };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private EmbeddingBatchJob CreateJob(IConfiguration? config = null) => new(
        _scopeFactoryMock.Object,
        _embeddingServiceMock.Object,
        _vectorStoreMock.Object,
        _loggerMock.Object,
        config ?? BuildConfig());

    private void SetupDedupeScan(params ActSectionChunk[] chunks)
    {
        var served = false;
        _chunkRepoMock
            .Setup(r => r.GetUnembeddedChunksAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (served) return new List<ActSectionChunk>();
                served = true;
                return chunks.ToList();
            });
    }

    [Fact]
    public async Task StartAsync_WhenRunOnStartupIsFalse_SkipsExecution()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:RunOnStartup"] = "false"
            })
            .Build();

        var job = CreateJob(config);

        await job.StartAsync(CancellationToken.None);

        _chunkRepoMock.Verify(r => r.GetUnembeddedChunksAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _embeddingServiceMock.Verify(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(It.IsAny<IReadOnlyList<(string, float[], Dictionary<string, string>)>>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenRunOnStartupIsTrue_ProcessesUnembeddedChunks()
    {
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Chunk 1 text" },
            new() { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "Chunk 2 text" }
        };

        SetupDedupeScan(chunks.ToArray());

        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<float[]> { new float[] { 0.1f, 0.2f }, new float[] { 0.3f, 0.4f } });

        var job = CreateJob();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _embeddingServiceMock.Verify(e => e.GetBatchEmbeddingsAsync(It.Is<IReadOnlyList<string>>(l => l.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(
            It.Is<IReadOnlyList<(string vectorId, float[] embedding, Dictionary<string, string> payload)>>(points =>
                points.Count == 2 &&
                points.All(p => p.payload["SectionId"] == "10"))),
            Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateBatchEmbeddingInfoAsync(It.Is<IReadOnlyList<(int, string, string)>>(l => l.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenBatchCallFails_RetriesOnNextIteration()
    {
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Chunk 1" },
            new() { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "Chunk 2" }
        };

        SetupDedupeScan(chunks.ToArray());

        _embeddingServiceMock
            .SetupSequence(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API timeout"))
            .ReturnsAsync(new List<float[]> { new float[] { 0.5f }, new float[] { 0.6f } });

        var job = CreateJob();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(It.IsAny<IReadOnlyList<(string, float[], Dictionary<string, string>)>>()), Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateBatchEmbeddingInfoAsync(It.Is<IReadOnlyList<(int, string, string)>>(l => l.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_IdenticalChunkTexts_CollapsedIntoOneEmbeddingAndSharedVector()
    {
        // The corpus has duplicate-text groups; each must cost ONE embedding,
        // and every duplicate row is stamped with the SAME VectorId.
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Duplicate text" },
            new() { ChunkId = 2, SectionId = 11, ChunkOrder = 1, ChunkText = "Duplicate text" },
            new() { ChunkId = 3, SectionId = 12, ChunkOrder = 1, ChunkText = "Unique text" }
        };

        SetupDedupeScan(chunks.ToArray());

        IReadOnlyList<string>? embeddedTexts = null;
        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, CancellationToken>((texts, _) => embeddedTexts = texts)
            .ReturnsAsync(new List<float[]> { new float[] { 0.5f }, new float[] { 0.7f } });

        // Capture ALL stamped rows across leader + follower passes.
        var stamped = new List<(int chunkId, string vectorId, string contentHash)>();
        _chunkRepoMock
            .Setup(r => r.UpdateBatchEmbeddingInfoAsync(It.IsAny<IReadOnlyList<(int, string, string)>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<(int chunkId, string vectorId, string contentHash)>, CancellationToken>((l, _) => stamped.AddRange(l));

        var job = CreateJob();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        // Only the 2 DISTINCT texts hit the API (global dedupe).
        Assert.NotNull(embeddedTexts);
        Assert.Equal(2, embeddedTexts!.Count);
        Assert.Contains("Duplicate text", embeddedTexts);
        Assert.Contains("Unique text", embeddedTexts);

        // Only 2 Qdrant points (one per distinct text).
        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(
            It.Is<IReadOnlyList<(string vectorId, float[] embedding, Dictionary<string, string> payload)>>(p => p.Count == 2)),
            Times.Once);

        // But ALL 3 rows got stamped, duplicates sharing the same VectorId.
        Assert.Equal(3, stamped.Count);
        var dup1 = stamped.Single(s => s.chunkId == 1);
        var dup2 = stamped.Single(s => s.chunkId == 2);
        var unique = stamped.Single(s => s.chunkId == 3);
        Assert.Equal(dup1.vectorId, dup2.vectorId); // duplicates share the vector
        Assert.NotEqual(dup1.vectorId, unique.vectorId);
    }

    [Fact]
    public async Task StartAsync_AlreadyCurrentRows_AreSkippedWithoutEmbedding()
    {
        // Stale poll: hash matches + VectorId present => nothing new to do.
        var hash = EmbeddingBatchJob.ComputeSha256("Stable text");
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Stable text", VectorId = "vec-1", ContentHash = hash }
        };

        SetupDedupeScan(chunks.ToArray());

        var job = CreateJob();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _embeddingServiceMock.Verify(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(It.IsAny<IReadOnlyList<(string, float[], Dictionary<string, string>)>>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_PermanentClientError_DropsBatchWithoutRetryBackoff()
    {
        // Regression: a 400 INVALID_ARGUMENT (e.g. oversized batch) can never
        // succeed on retry — the job must drop the batch and finish fast, not
        // burn 5 attempts x exponential backoff (~3 min) per batch.
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Chunk 1" },
            new() { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "Chunk 2" }
        };

        SetupDedupeScan(chunks.ToArray());

        var embedCalls = 0;
        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => embedCalls++)
            .ThrowsAsync(new GeminiApiException(
                "Gemini API returned 400: * BatchEmbedContentsRequest.requests: at most 100 requests can be in one batch", 400, "{ }"));

        var job = CreateJob();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;
        sw.Stop();

        // Exactly ONE embed attempt (no retries), and the job terminated
        // quickly (the old path slept 5+10+20+40+120s before dropping).
        Assert.Equal(1, embedCalls);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Permanent 4xx must drop the batch immediately; took {sw.Elapsed}.");
        _vectorStoreMock.Verify(v => v.UpsertBatchAsync(It.IsAny<IReadOnlyList<(string, float[], Dictionary<string, string>)>>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_BatchRespectsMaxTextsCap()
    {
        // 5 fetchable chunks, BatchMaxTexts=2 -> exactly 2 texts per request,
        // 3 embed calls total (then the queue drains).
        var chunks = Enumerable.Range(0, 5)
            .Select(i => new ActSectionChunk { ChunkId = i + 1, SectionId = 10, ChunkOrder = (short)(i + 1), ChunkText = $"Chunk {i}" })
            .ToList();

        SetupDedupeScan(chunks.ToArray());

        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 0.1f }).ToList());

        var job = CreateJob(BuildConfig(new Dictionary<string, string?> { ["Embedding:BatchMaxTexts"] = "2" }));
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _embeddingServiceMock.Verify(e => e.GetBatchEmbeddingsAsync(It.Is<IReadOnlyList<string>>(l => l.Count <= 2), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task StartAsync_DuplicateAcrossKeysetPages_CollapsedIntoOneEmbedding()
    {
        // The whole point of the global dedupe pass: duplicates that land in
        // DIFFERENT keyset pages (far apart in ChunkId order) must still share
        // one embedding — the old per-window dedupe missed these.
        var page1 = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Far duplicate" }
        };
        var page2 = new List<ActSectionChunk>
        {
            new() { ChunkId = 5000, SectionId = 99, ChunkOrder = 1, ChunkText = "Far duplicate" },
            new() { ChunkId = 5001, SectionId = 99, ChunkOrder = 2, ChunkText = "Solo text" }
        };

        var page = 0;
        _chunkRepoMock
            .Setup(r => r.GetUnembeddedChunksAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++page switch
            {
                1 => page1,
                2 => page2,
                _ => new List<ActSectionChunk>()
            });

        IReadOnlyList<string>? embeddedTexts = null;
        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, CancellationToken>((texts, _) => embeddedTexts = texts)
            .ReturnsAsync(new List<float[]> { new float[] { 0.5f }, new float[] { 0.7f } });

        var stamped = new List<(int chunkId, string vectorId, string contentHash)>();
        _chunkRepoMock
            .Setup(r => r.UpdateBatchEmbeddingInfoAsync(It.IsAny<IReadOnlyList<(int, string, string)>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<(int chunkId, string vectorId, string contentHash)>, CancellationToken>((l, _) => stamped.AddRange(l));

        var job = CreateJob();
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        // Both pages' distinct texts = 2 ("Far duplicate" + "Solo text").
        Assert.NotNull(embeddedTexts);
        Assert.Equal(2, embeddedTexts!.Count);

        // All 3 rows stamped; the two far-apart duplicates share a VectorId.
        Assert.Equal(3, stamped.Count);
        var far1 = stamped.Single(s => s.chunkId == 1);
        var far2 = stamped.Single(s => s.chunkId == 5000);
        Assert.Equal(far1.vectorId, far2.vectorId);
    }

    [Fact]
    public async Task StartAsync_ConcurrentWorkers_ResolveDistinctScopedRepositoryPerWorker()
    {
        // FIX-EMB-6 regression: parallel workers used to share ONE scoped
        // repository (one AppDbContext). DbContext is not thread-safe —
        // concurrent UpdateBatchEmbeddingInfoAsync calls threw "A second
        // operation was started on this context instance". Each worker must
        // now resolve its OWN scope/repo (own DbContext).
        var chunks = Enumerable.Range(0, 4)
            .Select(i => new ActSectionChunk { ChunkId = i + 1, SectionId = 10, ChunkOrder = (short)(i + 1), ChunkText = $"Unique chunk text {i}" })
            .ToList();

        var servedPage = false;
        var repos = new List<IActSectionChunkRepository>();
        var stampedRows = 0;
        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var repo = new Mock<IActSectionChunkRepository>();
                repo.Setup(r => r.GetUnembeddedCountAsync()).ReturnsAsync(chunks.Count);
                repo.Setup(r => r.GetUnembeddedChunksAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        if (servedPage) return new List<ActSectionChunk>();
                        servedPage = true;
                        return chunks.ToList();
                    });
                repo.Setup(r => r.UpdateBatchEmbeddingInfoAsync(It.IsAny<IReadOnlyList<(int, string, string)>>(), It.IsAny<CancellationToken>()))
                    .Callback<IReadOnlyList<(int chunkId, string vectorId, string contentHash)>, CancellationToken>((l, _) => Interlocked.Add(ref stampedRows, l.Count))
                    .Returns(Task.CompletedTask);

                var provider = new Mock<IServiceProvider>();
                provider.Setup(p => p.GetService(typeof(IActSectionChunkRepository))).Returns(repo.Object);
                var scope = new Mock<IServiceScope>();
                scope.Setup(s => s.ServiceProvider).Returns(provider.Object);

                lock (repos) repos.Add(repo.Object);
                return scope.Object;
            });

        _embeddingServiceMock
            .Setup(e => e.GetBatchEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) => texts.Select(_ => new[] { 0.1f }).ToList());

        var job = CreateJob(BuildConfig(new Dictionary<string, string?> { ["Embedding:MaxConcurrentBatches"] = "2" }));
        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        // 1 dedupe scope + 1 ExecuteAsync scope + 1 per worker (2 workers).
        // The old shared-repo code created only 2 scopes (workers got the repo
        // as a parameter) — this exact count fails if the fix regresses.
        Assert.Equal(4, repos.Count);
        // Every scope resolved its own repository (distinct DbContexts).
        Assert.Equal(4, repos.Distinct().Count());
        // The pipeline still stamped every leader row across the worker repos.
        Assert.Equal(4, stampedRows);
    }

    [Fact]
    public void ComputeSha256_ReturnsDeterministicLowercaseHexHash()
    {
        var text = "Bangladesh Legal Aid 2026";
        var hash1 = EmbeddingBatchJob.ComputeSha256(text);
        var hash2 = EmbeddingBatchJob.ComputeSha256(text);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1.ToLowerInvariant(), hash1);
    }
}
