using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Search;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class KeywordSearchServiceTests
{
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly KeywordSearchService _service;

    public KeywordSearchServiceTests()
    {
        _service = new KeywordSearchService(_sectionRepo.Object);
    }

    [Fact]
    public async Task SearchAsync_MapsRepositoryResults_ToRetrievedSections()
    {
        var act = new Act { ActId = 1, Title = "Labour Act, 2006" };
        var section = new ActSection
        {
            SectionId = 42,
            ActId = 1,
            Act = act,
            SectionNumber = "27",
            SectionText = "Termination of employment.",
        };
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(new[] { section });

        var results = (await _service.SearchAsync("termination")).ToList();

        var result = Assert.Single(results);
        Assert.Equal(42, result.SectionId);
        Assert.Equal("Labour Act, 2006", result.ActTitle);
        Assert.Equal("27", result.SectionNumber);
        Assert.Equal("Termination of employment.", result.SectionText);
        Assert.Equal(RetrievalMethod.Keyword, result.Method);
    }

    [Fact]
    public async Task SearchAsync_QuotesAndEscapesQuery_BeforeCallingRepository()
    {
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("labour \"rights\"");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("\"labour rights\"", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_NullSectionNumber_MapsToEmptyString()
    {
        var act = new Act { ActId = 1, Title = "Some Act" };
        var section = new ActSection
        {
            SectionId = 1,
            ActId = 1,
            Act = act,
            SectionNumber = null,
            SectionText = "Chapter header text.",
        };
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(new[] { section });

        var results = (await _service.SearchAsync("chapter")).ToList();

        Assert.Equal(string.Empty, Assert.Single(results).SectionNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchAsync_BlankQuery_ReturnsEmpty_WithoutCallingRepository(string? query)
    {
        var results = await _service.SearchAsync(query!);

        Assert.Empty(results);
        _sectionRepo.Verify(r => r.FullTextSearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
