using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Infrastructure.VectorStore;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.IntegrationTests.AiPipeline;

public class RagRetrievalSmokeTests
{
    private readonly Mock<IEmbeddingService> _embeddingServiceMock = new();
    private readonly Mock<IVectorStore> _vectorStoreMock = new();
    private readonly Mock<IActSectionRepository> _sectionRepoMock = new();
    private readonly Mock<IKeywordSectionSearch> _keywordSearchMock = new();

    [Fact]
    public async Task Labour_Query_Returns_Labour_Act_Sections_Via_Vector_Pipeline()
    {
        // 1. Arrange realistic Bangla legal query
        var query = "আমার বেতন ৩ মাস দেয়নি"; // "Haven't been paid for 3 months"
        var queryEmbedding = new float[] { 0.05f, -0.12f, 0.88f };

        _embeddingServiceMock
            .Setup(e => e.GetEmbeddingAsync(query, default))
            .ReturnsAsync(queryEmbedding);

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryEmbedding, 5))
            .ReturnsAsync(new[]
            {
                new VectorSearchResult(
                    "sec_123_chk_1",
                    0.92f,
                    new Dictionary<string, string>
                    {
                        ["SectionId"] = "123",
                        ["ChunkId"] = "456",
                        ["ActTitle"] = "Bangladesh Labour Act, 2006",
                        ["SectionNumber"] = "123",
                    })
            });

        var act = new Act
        {
            ActId = 1,
            Title = "Bangladesh Labour Act, 2006",
            ActNumber = "XLII",
            Year = 2006,
        };

        var section = new ActSection
        {
            SectionId = 123,
            ActId = 1,
            SectionNumber = "123",
            SectionText = "The wages of every worker shall be paid before the expiry of the seventh working day...",
            Act = act,
        };

        _sectionRepoMock
            .Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(ids => ids.Contains(123))))
            .ReturnsAsync(new[] { section });

        var similaritySearch = new SimilaritySearchService(
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _sectionRepoMock.Object);

        var ragContextBuilder = new RagContextBuilder(similaritySearch, _keywordSearchMock.Object);

        // 2. Act: Execute RAG retrieval
        var retrievedSections = (await ragContextBuilder.RetrieveContextAsync(query, topK: 5)).ToList();

        // 3. Assert: Verify retrieved section is Labour Act s.123 with high relevance score
        Assert.NotEmpty(retrievedSections);
        var first = retrievedSections.First();
        Assert.Equal("Bangladesh Labour Act, 2006", first.ActTitle);
        Assert.Equal("123", first.SectionNumber);
        Assert.Equal(0.92f, first.RelevanceScore, precision: 2);
        Assert.Equal(RetrievalMethod.Vector, first.Method);
        Assert.Contains("The wages of every worker shall be paid", first.SectionText);

        _embeddingServiceMock.Verify(e => e.GetEmbeddingAsync(query, default), Times.Once);
        _vectorStoreMock.Verify(v => v.SearchAsync(queryEmbedding, 5), Times.Once);
        _keywordSearchMock.Verify(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Labour_Query_Falls_Back_To_FTS_When_VectorStore_Returns_Empty()
    {
        var query = "কর্মক্ষেত্রে দুর্ঘটনা ক্ষতিপূরণ"; // "Workplace accident compensation"
        var queryEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        _embeddingServiceMock
            .Setup(e => e.GetEmbeddingAsync(query, default))
            .ReturnsAsync(queryEmbedding);

        _vectorStoreMock
            .Setup(v => v.SearchAsync(queryEmbedding, 5))
            .ReturnsAsync(Array.Empty<VectorSearchResult>()); // Vector returns empty

        _keywordSearchMock
            .Setup(k => k.SearchAsync(query, 5))
            .ReturnsAsync(new[]
            {
                new RetrievedSection(
                    SectionId: 150,
                    ActTitle: "Bangladesh Labour Act, 2006",
                    SectionNumber: "150",
                    SectionText: "If personal injury is caused to a worker by accident arising out of and in the course of his employment...",
                    RelevanceScore: 0.75f,
                    Method: RetrievalMethod.Keyword,
                    ActNumber: "XLII",
                    ActYear: 2006)
            });

        var similaritySearch = new SimilaritySearchService(
            _embeddingServiceMock.Object,
            _vectorStoreMock.Object,
            _sectionRepoMock.Object);

        var ragContextBuilder = new RagContextBuilder(similaritySearch, _keywordSearchMock.Object);

        var results = (await ragContextBuilder.RetrieveContextAsync(query, topK: 5)).ToList();

        var result = Assert.Single(results);
        Assert.Equal("150", result.SectionNumber);
        Assert.Equal("Bangladesh Labour Act, 2006", result.ActTitle);
        Assert.Equal(RetrievalMethod.Keyword, result.Method);
        _keywordSearchMock.Verify(k => k.SearchAsync(query, 5), Times.Once);
    }
}
