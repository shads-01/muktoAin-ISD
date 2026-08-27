using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Infrastructure.VectorStore;
using Moq;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.UnitTests.Services;

public class SimilaritySearchServiceTests
{
    private readonly Mock<IEmbeddingService> _embeddingService = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly SimilaritySearchService _service;

    public SimilaritySearchServiceTests()
    {
        _service = new SimilaritySearchService(
            _embeddingService.Object, _vectorStore.Object, _sectionRepo.Object);
    }

    private static ActSection MakeSection(int id, string actTitle, string sectionNumber, string text) =>
        new()
        {
            SectionId = id,
            ActId = 1,
            Act = new Act { ActId = 1, Title = actTitle, ActNumber = "XLV", Year = 1860 },
            SectionNumber = sectionNumber,
            SectionText = text,
        };

    [Fact]
    public async Task SearchAsync_EmbedsQuery_SearchesVectorStore_AndMapsResults()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("termination", default))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.9f,
                    new Dictionary<string, string> { ["SectionId"] = "42" }),
            });
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(ids => ids.Contains(42))))
            .ReturnsAsync(new[] { MakeSection(42, "Labour Act, 2006", "27", "Termination of employment.") });

        var results = (await _service.SearchAsync("termination")).ToList();

        var result = Assert.Single(results);
        Assert.Equal(42, result.SectionId);
        Assert.Equal("Labour Act, 2006", result.ActTitle);
        Assert.Equal("27", result.SectionNumber);
        Assert.Equal("Termination of employment.", result.SectionText);
        Assert.Equal(0.9f, result.RelevanceScore);
        Assert.Equal(RetrievalMethod.Vector, result.Method);
        Assert.Equal("XLV", result.ActNumber);
        Assert.Equal(1860, result.ActYear);
    }

    [Fact]
    public async Task SearchAsync_MultipleChunksOfSameSection_KeepsOnlyHighestScoringHit()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("labour", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.6f,
                    new Dictionary<string, string> { ["SectionId"] = "42" }),
                new VectorSearchResult("vec-2", 0.95f,
                    new Dictionary<string, string> { ["SectionId"] = "42" }),
            });
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new[] { MakeSection(42, "Labour Act, 2006", "27", "Termination of employment.") });

        var results = (await _service.SearchAsync("labour")).ToList();

        var result = Assert.Single(results);
        Assert.Equal(0.95f, result.RelevanceScore);
    }

    [Fact]
    public async Task SearchAsync_ResultsOrderedByScoreDescending()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("query", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.4f,
                    new Dictionary<string, string> { ["SectionId"] = "1" }),
                new VectorSearchResult("vec-2", 0.9f,
                    new Dictionary<string, string> { ["SectionId"] = "2" }),
            });
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new[]
            {
                MakeSection(1, "Act A", "1", "Text A"),
                MakeSection(2, "Act B", "2", "Text B"),
            });

        var results = (await _service.SearchAsync("query")).ToList();

        Assert.Equal(new[] { 2, 1 }, results.Select(r => r.SectionId));
    }

    [Fact]
    public async Task SearchAsync_VectorResultMissingSectionIdPayload_IsSkipped()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("query", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.9f, new Dictionary<string, string>()),
            });

        var results = await _service.SearchAsync("query");

        Assert.Empty(results);
        _sectionRepo.Verify(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_SectionNoLongerInRepository_IsExcludedFromResults()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("query", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.9f,
                    new Dictionary<string, string> { ["SectionId"] = "42" }),
            });
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(Array.Empty<ActSection>());

        var results = await _service.SearchAsync("query");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_NoVectorResults_ReturnsEmpty_WithoutQueryingRepository()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("query", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(Array.Empty<VectorSearchResult>());

        var results = await _service.SearchAsync("query");

        Assert.Empty(results);
        _sectionRepo.Verify(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_NullSectionNumber_RecoversLeadingNumberFromSectionText()
    {
        _embeddingService.Setup(e => e.GetEmbeddingAsync("cheating", default))
            .ReturnsAsync(new float[] { 0.1f });
        _vectorStore.Setup(v => v.SearchAsync(It.IsAny<float[]>(), 8))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult("vec-1", 0.9f,
                    new Dictionary<string, string> { ["SectionId"] = "1" }),
            });
        var section = MakeSection(1, "Penal Code, 1860", "", "420. Cheating and dishonestly inducing delivery.");
        section.SectionNumber = null;
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new[] { section });

        var results = (await _service.SearchAsync("cheating")).ToList();

        Assert.Equal("420", Assert.Single(results).SectionNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_BlankQuery_ReturnsEmpty_WithoutEmbeddingOrSearching(string? query)
    {
        var results = await _service.SearchAsync(query!);

        Assert.Empty(results);
        _embeddingService.Verify(e => e.GetEmbeddingAsync(It.IsAny<string>(), default), Times.Never);
        _vectorStore.Verify(v => v.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>()), Times.Never);
    }
}
