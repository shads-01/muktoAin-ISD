using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class SearchServiceTests
{
    private readonly Mock<IKeywordSectionSearch> _keywordSearch = new();
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _service = new SearchService(_keywordSearch.Object, _actRepo.Object);
    }

    private static RetrievedSection Section(int id, string title = "Labour Act, 2006") =>
        new(id, title, id.ToString(), $"Section {id} text.", 0.5f, RetrievalMethod.Keyword);

    [Fact]
    public async Task SearchActsAsync_MapsAndPaginates_Results()
    {
        var results = Enumerable.Range(1, 25).Select(i => Section(i)).ToList();
        _keywordSearch.Setup(s => s.SearchAsync("termination", 100)).ReturnsAsync(results);

        var dto = await _service.SearchActsAsync("termination", page: 2, pageSize: 10);

        Assert.Equal("termination", dto.Query);
        Assert.Equal(25, dto.TotalResults);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.Results.Count);
        Assert.Equal(11, dto.Results[0].SectionId);
    }

    [Fact]
    public async Task SearchActsAsync_FiltersByActId_WhenProvided()
    {
        var results = new List<RetrievedSection>
        {
            Section(1, "Labour Act, 2006"),
            Section(2, "Consumer Rights Act, 2009"),
        };
        _keywordSearch.Setup(s => s.SearchAsync("dispute", 100)).ReturnsAsync(results);

        var actWithSections = new Act
        {
            ActId = 10,
            Sections = new List<ActSection> { new() { SectionId = 1 } },
        };
        _actRepo.Setup(r => r.GetWithSectionsAsync(10)).ReturnsAsync(actWithSections);

        var dto = await _service.SearchActsAsync("dispute", actId: 10);

        Assert.Equal(1, dto.TotalResults);
        Assert.Equal(1, dto.Results[0].SectionId);
    }

    [Fact]
    public async Task SearchActsAsync_UnknownActId_ReturnsEmpty()
    {
        _keywordSearch.Setup(s => s.SearchAsync("dispute", 100))
            .ReturnsAsync(new[] { Section(1) });
        _actRepo.Setup(r => r.GetWithSectionsAsync(999)).ReturnsAsync((Act?)null);

        var dto = await _service.SearchActsAsync("dispute", actId: 999);

        Assert.Equal(0, dto.TotalResults);
        Assert.Empty(dto.Results);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task SearchActsAsync_InvalidPage_FallsBackToPageOne(int page, int expected)
    {
        _keywordSearch.Setup(s => s.SearchAsync("x", 100)).ReturnsAsync(new[] { Section(1) });

        var dto = await _service.SearchActsAsync("x", page: page);

        Assert.Equal(expected, dto.Page);
    }

    [Fact]
    public async Task BrowseActAsync_ListsSections_OrderedByOrdinalPosition()
    {
        var act = new Act
        {
            ActId = 10,
            Title = "Labour Act, 2006",
            ActNumber = "42",
            Year = 2006,
            Sections = new List<ActSection>
            {
                new() { SectionId = 2, SectionNumber = "2", OrdinalPosition = 1, SectionText = "Second." },
                new() { SectionId = 1, SectionNumber = "1", OrdinalPosition = 0, SectionText = "First." },
            },
        };
        _actRepo.Setup(r => r.GetWithSectionsAsync(10)).ReturnsAsync(act);

        var dto = await _service.BrowseActAsync(10);

        Assert.Equal(string.Empty, dto.Query);
        Assert.Equal(2, dto.TotalResults);
        Assert.Equal(1, dto.Results[0].SectionId);
        Assert.Equal(2, dto.Results[1].SectionId);
        Assert.Equal("Labour Act, 2006", dto.Results[0].ActTitle);
        Assert.Equal("42", dto.Results[0].ActNumber);
        Assert.Equal(2006, dto.Results[0].ActYear);
    }

    [Fact]
    public async Task BrowseActAsync_IncludesSections_WithNullStoredSectionNumber()
    {
        // ActImportService leaves ActSection.SectionNumber null for the entire corpus
        // (see SectionNumberResolver) -- browsing must not filter those rows out, and
        // should recover a display number from the section text's leading digit instead.
        var act = new Act
        {
            ActId = 10,
            Sections = new List<ActSection>
            {
                new() { SectionId = 1, SectionNumber = null, OrdinalPosition = 0, SectionText = "1. First section." },
                new() { SectionId = 2, SectionNumber = null, OrdinalPosition = 1, SectionText = "2. Second section." },
            },
        };
        _actRepo.Setup(r => r.GetWithSectionsAsync(10)).ReturnsAsync(act);

        var dto = await _service.BrowseActAsync(10);

        Assert.Equal(2, dto.TotalResults);
        Assert.Equal("1", dto.Results[0].SectionNumber);
        Assert.Equal("2", dto.Results[1].SectionNumber);
    }

    [Fact]
    public async Task BrowseActAsync_UnknownActId_ReturnsEmpty()
    {
        _actRepo.Setup(r => r.GetWithSectionsAsync(999)).ReturnsAsync((Act?)null);

        var dto = await _service.BrowseActAsync(999);

        Assert.Equal(0, dto.TotalResults);
        Assert.Empty(dto.Results);
    }

    [Fact]
    public async Task BrowseActAsync_Paginates_Results()
    {
        var act = new Act
        {
            ActId = 10,
            Sections = Enumerable.Range(1, 15)
                .Select(i => new ActSection { SectionId = i, SectionNumber = i.ToString(), OrdinalPosition = i })
                .ToList(),
        };
        _actRepo.Setup(r => r.GetWithSectionsAsync(10)).ReturnsAsync(act);

        var dto = await _service.BrowseActAsync(10, page: 2, pageSize: 10);

        Assert.Equal(15, dto.TotalResults);
        Assert.Equal(2, dto.Page);
        Assert.Equal(5, dto.Results.Count);
        Assert.Equal(11, dto.Results[0].SectionId);
    }
}
