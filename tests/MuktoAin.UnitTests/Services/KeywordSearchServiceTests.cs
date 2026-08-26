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
    public async Task SearchAsync_SingleWordQuery_WrapsAsExactQuotedTerm()
    {
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("labour");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("\"labour\"", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MultiWordQuery_AndsEachTermTogether()
    {
        // Requiring every term to appear (not necessarily adjacent) fixes queries whose
        // terms exist in the same section but aren't literally next to each other.
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("labour \"rights\"");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("\"labour\" AND \"rights\"", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_BengaliDigitQuery_SearchesBothDigitScripts()
    {
        // This corpus is genuinely bilingual: older English-language Acts number
        // sections with Latin digits ("420. Whoever cheats..."), while Bengali-language
        // Acts number them with native Bengali digits ("১৬।  অন্য কোনো...") -- Bengali
        // and Latin digits are different Unicode code points to CONTAINSTABLE, so a
        // Bengali-numeral query must search both forms via OR, not convert away the
        // original and search only the other script (that would silently drop every
        // Bengali-language Act's results).
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("৪২০");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("(\"৪২০\" OR \"420\")", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_LatinDigitQuery_AlsoSearchesBengaliDigitForm()
    {
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("420");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("(\"420\" OR \"৪২০\")", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_NonNumericWord_UnaffectedByDigitScriptHandling()
    {
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(Array.Empty<ActSection>());

        await _service.SearchAsync("labour");

        _sectionRepo.Verify(r => r.FullTextSearchAsync("\"labour\"", 20), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_QueryOfOnlyQuoteCharacters_ReturnsEmpty_WithoutCallingRepository()
    {
        var results = await _service.SearchAsync("\"\"");

        Assert.Empty(results);
        _sectionRepo.Verify(r => r.FullTextSearchAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_NullSectionNumber_WithNoLeadingNumberInText_MapsToEmptyString()
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

    [Fact]
    public async Task SearchAsync_NullSectionNumber_RecoversLeadingNumberFromSectionText()
    {
        // ActSection.SectionNumber is left null by design (ActImportService) -- the
        // number is usually still embedded as the leading token of SectionText itself.
        var act = new Act { ActId = 1, Title = "Penal Code, 1860" };
        var section = new ActSection
        {
            SectionId = 1,
            ActId = 1,
            Act = act,
            SectionNumber = null,
            SectionText = "420. Cheating and dishonestly inducing delivery of property.",
        };
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(new[] { section });

        var results = (await _service.SearchAsync("cheating")).ToList();

        Assert.Equal("420", Assert.Single(results).SectionNumber);
    }

    [Fact]
    public async Task SearchAsync_StoredSectionNumber_TakesPriorityOverLeadingText()
    {
        var act = new Act { ActId = 1, Title = "Labour Act, 2006" };
        var section = new ActSection
        {
            SectionId = 1,
            ActId = 1,
            Act = act,
            SectionNumber = "27",
            SectionText = "99. Text that happens to start with a different number.",
        };
        _sectionRepo.Setup(r => r.FullTextSearchAsync(It.IsAny<string>(), 20))
            .ReturnsAsync(new[] { section });

        var results = (await _service.SearchAsync("text")).ToList();

        Assert.Equal("27", Assert.Single(results).SectionNumber);
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
