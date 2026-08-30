using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
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

        var job = new EmbeddingBatchJob(
            _scopeFactoryMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            config);

        await job.StartAsync(CancellationToken.None);

        _chunkRepoMock.Verify(r => r.GetUnembeddedChunksAsync(It.IsAny<int>()), Times.Never);
        _embeddingServiceMock.Verify(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _vectorStoreMock.Verify(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<Dictionary<string, string>>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenRunOnStartupIsTrue_ProcessesUnembeddedChunks()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:RunOnStartup"] = "true",
                ["Embedding:StartupDelayMs"] = "0"
            })
            .Build();

        var act = new Act { Title = "Bangladesh Labour Act, 2006" };
        var section = new ActSection { SectionNumber = "33", Act = act };
        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Chunk 1 text", Section = section },
            new() { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "Chunk 2 text", Section = section }
        };

        _chunkRepoMock
            .SetupSequence(r => r.GetUnembeddedChunksAsync(It.IsAny<int>()))
            .ReturnsAsync(chunks)
            .ReturnsAsync(new List<ActSectionChunk>());

        _embeddingServiceMock
            .Setup(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var job = new EmbeddingBatchJob(
            _scopeFactoryMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            config);

        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _embeddingServiceMock.Verify(e => e.GetEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _vectorStoreMock.Verify(v => v.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<float[]>(),
            It.Is<Dictionary<string, string>>(p => p["ChunkId"] == "1" && p["ActTitle"] == "Bangladesh Labour Act, 2006")),
            Times.Once);
        _vectorStoreMock.Verify(v => v.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<float[]>(),
            It.Is<Dictionary<string, string>>(p => p["ChunkId"] == "2")),
            Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateEmbeddingInfoAsync(1, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateEmbeddingInfoAsync(2, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenSingleChunkFails_ContinuesProcessingRemainingChunks()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embedding:RunOnStartup"] = "true",
                ["Embedding:StartupDelayMs"] = "0"
            })
            .Build();

        var chunks = new List<ActSectionChunk>
        {
            new() { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "Failing chunk" },
            new() { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "Successful chunk" }
        };

        _chunkRepoMock
            .SetupSequence(r => r.GetUnembeddedChunksAsync(It.IsAny<int>()))
            .ReturnsAsync(chunks)
            .ReturnsAsync(new List<ActSectionChunk>());

        _embeddingServiceMock
            .Setup(e => e.GetEmbeddingAsync("Failing chunk", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API timeout"));

        _embeddingServiceMock
            .Setup(e => e.GetEmbeddingAsync("Successful chunk", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.5f });

        var job = new EmbeddingBatchJob(
            _scopeFactoryMock.Object,
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _loggerMock.Object,
            config);

        await job.StartAsync(CancellationToken.None);
        if (job.ExecuteTask != null) await job.ExecuteTask;

        _vectorStoreMock.Verify(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.Is<Dictionary<string, string>>(p => p["ChunkId"] == "2")), Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateEmbeddingInfoAsync(2, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _chunkRepoMock.Verify(r => r.UpdateEmbeddingInfoAsync(1, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
