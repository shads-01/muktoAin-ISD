using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class RagContextBuilderTests
{
    private readonly Mock<IVectorSectionSearch> _vectorSearch = new();
    private readonly Mock<IKeywordSectionSearch> _keywordSearch = new();
    private readonly Mock<IScenarioMappingRepository> _scenarioMappingRepo = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly RagContextBuilder _builder;

    public RagContextBuilderTests()
    {
        // Default: no curated mappings exist — every existing behavior test
        // runs with the prior-merge as a pure no-op.
        _scenarioMappingRepo
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ScenarioMapping>());
        _actRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Act>());

        _builder = new RagContextBuilder(
            _vectorSearch.Object, _keywordSearch.Object,
            _scenarioMappingRepo.Object, _sectionRepo.Object, _actRepo.Object,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<RagContextBuilder>>());
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

    // ---------- FR-18 scenario-prior merge (redesign) ----------

    [Fact]
    public async Task RetrieveContextAsync_MergesCuratedMapping_WhenQueryContainsKeyword()
    {
        var vectorResults = new[] { Section(1, RetrievalMethod.Vector) };
        _vectorSearch.Setup(s => s.SearchAsync("গার্মেন্টসে ৩ মাস বেতন দেয়নি", 8))
            .ReturnsAsync(vectorResults);
        _scenarioMappingRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ScenarioMapping>
            {
                new() { MappingId = 1, SectionId = 20306, ScenarioKeyword = "বেতন" },
                new() { MappingId = 2, SectionId = 999, ScenarioKeyword = "জমির দলিল" }
            });
        _sectionRepo.Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(
                ids => ids.SequenceEqual(new[] { 20306 }))))
            .ReturnsAsync(new List<ActSection>
            {
                new() { SectionId = 20306, ActId = 990, SectionNumber = "123", SectionText = "wage text" }
            });
        _actRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<Act> { new() { ActId = 990, Title = "বাংলাদেশ শ্রম আইন, ২০০৬", ActNumber = "XLV", Year = 2006 } });

        var result = (await _builder.RetrieveContextAsync("গার্মেন্টসে ৩ মাস বেতন দেয়নি")).ToList();

        Assert.Equal(2, result.Count);
        var merged = result.Single(r => r.SectionId == 20306);
        Assert.Equal("বাংলাদেশ শ্রম আইন, ২০০৬", merged.ActTitle);
        Assert.Equal(RetrievalMethod.Keyword, merged.Method);
        Assert.Equal(1.0f, merged.RelevanceScore);
    }

    [Fact]
    public async Task RetrieveContextAsync_DoesNotDuplicate_AlreadyRetrievedMappedSection()
    {
        var vectorResults = new[] { Section(20306, RetrievalMethod.Vector) };
        _vectorSearch.Setup(s => s.SearchAsync("beton dey nai", 8)).ReturnsAsync(vectorResults);
        _scenarioMappingRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ScenarioMapping>
            {
                new() { MappingId = 1, SectionId = 20306, ScenarioKeyword = "beton dey nai" }
            });

        var result = (await _builder.RetrieveContextAsync("beton dey nai")).ToList();

        Assert.Single(result); // merged prior skipped — already present
    }

    [Fact]
    public async Task RetrieveContextAsync_KeywordCaseInsensitive_AndNoHitsLeavesResultUntouched()
    {
        var vectorResults = new[] { Section(1, RetrievalMethod.Vector) };
        _vectorSearch.Setup(s => s.SearchAsync("my landlord problem", 8)).ReturnsAsync(vectorResults);
        // mappings exist but none match the query
        _scenarioMappingRepo.Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<ScenarioMapping>
            {
                new() { MappingId = 1, SectionId = 20306, ScenarioKeyword = "বেতন" }
            });

        var result = (await _builder.RetrieveContextAsync("my landlord problem")).ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].SectionId);
        _sectionRepo.Verify(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }
}
