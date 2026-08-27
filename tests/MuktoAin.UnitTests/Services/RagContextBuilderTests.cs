using MuktoAin.Application.Services;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class RagContextBuilderTests
{
    private readonly Mock<IVectorSectionSearch> _vectorSearch = new();
    private readonly Mock<IKeywordSectionSearch> _keywordSearch = new();
    private readonly RagContextBuilder _builder;

    public RagContextBuilderTests()
    {
        _builder = new RagContextBuilder(_vectorSearch.Object, _keywordSearch.Object);
    }

    private static RetrievedSection Section(int id, RetrievalMethod method, float score = 0.5f) =>
        new(id, "Labour Act, 2006", id.ToString(), $"Section {id} text.", score, method);

    [Fact]
    public async Task RetrieveContextAsync_ReturnsVectorResults_WhenVectorSearchFindsSomething()
    {
        var vectorResults = new[] { Section(1, RetrievalMethod.Vector) };
        _vectorSearch.Setup(s => s.SearchAsync("termination", 8)).ReturnsAsync(vectorResults);

        var result = (await _builder.RetrieveContextAsync("termination")).ToList();

        Assert.Single(result);
        Assert.Equal(RetrievalMethod.Vector, result[0].Method);
        _keywordSearch.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RetrieveContextAsync_FallsBackToKeyword_WhenVectorSearchReturnsEmpty()
    {
        _vectorSearch.Setup(s => s.SearchAsync("termination", 8))
            .ReturnsAsync(Enumerable.Empty<RetrievedSection>());
        var keywordResults = new[] { Section(2, RetrievalMethod.Keyword) };
        _keywordSearch.Setup(s => s.SearchAsync("termination", 8)).ReturnsAsync(keywordResults);

        var result = (await _builder.RetrieveContextAsync("termination")).ToList();

        Assert.Single(result);
        Assert.Equal(RetrievalMethod.Keyword, result[0].Method);
    }

    [Fact]
    public async Task RetrieveContextAsync_FallsBackToKeyword_WhenVectorSearchThrows()
    {
        _vectorSearch.Setup(s => s.SearchAsync("termination", 8))
            .ThrowsAsync(new InvalidOperationException("Qdrant unavailable"));
        var keywordResults = new[] { Section(3, RetrievalMethod.Keyword) };
        _keywordSearch.Setup(s => s.SearchAsync("termination", 8)).ReturnsAsync(keywordResults);

        var result = (await _builder.RetrieveContextAsync("termination")).ToList();

        Assert.Single(result);
        Assert.Equal(3, result[0].SectionId);
    }

    [Fact]
    public async Task RetrieveContextAsync_PassesTopK_ToBothPaths()
    {
        _vectorSearch.Setup(s => s.SearchAsync("dispute", 5))
            .ReturnsAsync(Enumerable.Empty<RetrievedSection>());
        _keywordSearch.Setup(s => s.SearchAsync("dispute", 5))
            .ReturnsAsync(Enumerable.Empty<RetrievedSection>());

        await _builder.RetrieveContextAsync("dispute", topK: 5);

        _vectorSearch.Verify(s => s.SearchAsync("dispute", 5), Times.Once);
        _keywordSearch.Verify(s => s.SearchAsync("dispute", 5), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RetrieveContextAsync_BlankQuery_ReturnsEmpty_WithoutCallingEitherSearch(string? query)
    {
        var result = await _builder.RetrieveContextAsync(query!);

        Assert.Empty(result);
        _vectorSearch.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _keywordSearch.Verify(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
