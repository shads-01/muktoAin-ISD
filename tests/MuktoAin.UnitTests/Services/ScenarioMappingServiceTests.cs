using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class ScenarioMappingServiceTests
{
    private readonly Mock<IScenarioMappingRepository> _mappingRepo = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly ScenarioMappingService _service;

    public ScenarioMappingServiceTests()
    {
        _service = new ScenarioMappingService(_mappingRepo.Object, _sectionRepo.Object, _actRepo.Object);
    }

    private static (Act Act, ActSection Section) NewSection(int sectionId = 10, int actId = 1)
    {
        var act = new Act { ActId = actId, Title = "The Labour Act, 2006", Year = 2006 };
        var section = new ActSection
        {
            SectionId = sectionId,
            ActId = actId,
            Act = act,
            OrdinalPosition = 1,
            SectionText = "wages shall be paid"
        };
        return (act, section);
    }

    // ---------- GetAllAsync ----------

    [Fact]
    public async Task GetAllAsync_EnrichesMappingsWithActTitleAndSectionNumber()
    {
        var (act, section) = NewSection();
        var mappings = new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = section.SectionId, ScenarioKeyword = "বেতন বাকি", Notes = "staple scenario" },
        };
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(mappings);
        _sectionRepo
            .Setup(r => r.GetBySectionIdsAsync(It.Is<IEnumerable<int>>(ids => ids.Single() == section.SectionId)))
            .ReturnsAsync(new[] { section });
        _actRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { act });

        var result = await _service.GetAllAsync();

        var dto = Assert.Single(result);
        Assert.Equal(1, dto.MappingId);
        Assert.Equal(10, dto.SectionId);
        Assert.Equal("The Labour Act, 2006", dto.ActTitle);
        Assert.Equal("বেতন বাকি", dto.ScenarioKeyword);
        Assert.Equal("staple scenario", dto.Notes);
    }

    [Fact]
    public async Task GetAllAsync_OrphanedSectionId_RendersWithEmptyActTitle()
    {
        var mappings = new List<ScenarioMapping>
        {
            new() { MappingId = 2, SectionId = 999, ScenarioKeyword = "ghost" },
        };
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(mappings);
        _sectionRepo
            .Setup(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(Array.Empty<ActSection>());
        _actRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<Act>());

        var result = await _service.GetAllAsync();

        var dto = Assert.Single(result);
        Assert.Equal(string.Empty, dto.ActTitle);
        // SectionNumber is null because the whole corpus is imported with null
        // SectionNumbers (ActImportService design).
        Assert.Null(dto.SectionNumber);
    }

    [Fact]
    public async Task GetAllAsync_EmptyTable_ReturnsEmptyList()
    {
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>());

        var result = await _service.GetAllAsync();

        Assert.Empty(result);
        // Short-circuits: no section/act fetch for an empty table.
        _sectionRepo.Verify(r => r.GetBySectionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
        _actRepo.Verify(r => r.GetAllAsync(), Times.Never);
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.GetByIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingMapping_ReturnsEnrichedDto()
    {
        var (act, section) = NewSection();
        _mappingRepo
            .Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" });
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);

        var result = await _service.GetByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("The Labour Act, 2006", result!.ActTitle);
        Assert.Equal("wages unpaid", result.ScenarioKeyword);
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_BlankKeyword_Fails()
    {
        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, "   ", null));

        Assert.False(result.Success);
        Assert.Equal("Keyword is required.", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_KeywordOver200Chars_Fails()
    {
        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, new string('k', 201), null));

        Assert.False(result.Success);
        Assert.Contains("200 characters or fewer", result.Error);
    }

    [Fact]
    public async Task CreateAsync_SectionNotFound_Fails()
    {
        _sectionRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ActSection?)null);

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 999, "বেতন বাকি", null));

        Assert.False(result.Success);
        Assert.Contains("999", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DuplicateKeywordOnSameSection_Fails()
    {
        var (act, section) = NewSection();
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = 10, ScenarioKeyword = "বেতন বাকি" },
        });

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 10, "বেতন বাকি", null));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _mappingRepo.Verify(r => r.AddAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_SameKeywordOnDifferentSection_Succeeds()
    {
        var (act, section) = NewSection(sectionId: 10);
        var other = new ActSection { SectionId = 20, ActId = 1, Act = act, OrdinalPosition = 2, SectionText = "other" };
        _sectionRepo.Setup(r => r.GetByIdAsync(20)).ReturnsAsync(other);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>
        {
            new() { MappingId = 1, SectionId = 10, ScenarioKeyword = "বেতন বাকি" },
        });

        var result = await _service.CreateAsync(new ScenarioMappingSaveDto(null, 20, "বেতন বাকি", null));

        Assert.True(result.Success);
        _mappingRepo.Verify(r => r.AddAsync(It.Is<ScenarioMapping>(m =>
            m.SectionId == 20 && m.ScenarioKeyword == "বেতন বাকি")), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_Valid_TrimsKeywordAndNotes_AndReturnsEnrichedDto()
    {
        var (act, section) = NewSection();
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping>());

        var result = await _service.CreateAsync(
            new ScenarioMappingSaveDto(null, 10, "  বেতন বাকি  ", "  staple garment scenario  "));

        Assert.True(result.Success);
        Assert.NotNull(result.Mapping);
        Assert.Equal("বেতন বাকি", result.Mapping!.ScenarioKeyword);
        Assert.Equal("The Labour Act, 2006", result.Mapping.ActTitle);
        _mappingRepo.Verify(r => r.AddAsync(It.Is<ScenarioMapping>(m =>
            m.SectionId == 10
            && m.ScenarioKeyword == "বেতন বাকি"
            && m.Notes == "staple garment scenario")), Times.Once);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_UnknownMapping_Fails()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.UpdateAsync(999, new ScenarioMappingSaveDto(999, 10, "k", null));

        Assert.False(result.Success);
        Assert.Contains("999", result.Error);
    }

    [Fact]
    public async Task UpdateAsync_KeptKeywordOnSameMapping_IsNotADuplicate()
    {
        var (act, section) = NewSection();
        var existing = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid", Notes = null };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping> { existing });

        var result = await _service.UpdateAsync(1, new ScenarioMappingSaveDto(1, 10, "wages unpaid", "updated notes"));

        Assert.True(result.Success);
        Assert.Equal("updated notes", existing.Notes);
        _mappingRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateKeywordOnAnotherMapping_Fails()
    {
        var (act, section) = NewSection();
        var existing = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" };
        var other = new ScenarioMapping { MappingId = 2, SectionId = 10, ScenarioKeyword = "overtime pay" };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(existing);
        _sectionRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(section);
        _mappingRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<ScenarioMapping> { existing, other });

        var result = await _service.UpdateAsync(1, new ScenarioMappingSaveDto(1, 10, "Overtime Pay", null));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _mappingRepo.Verify(r => r.UpdateAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_UnknownMapping_ReturnsFalse()
    {
        _mappingRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((ScenarioMapping?)null);

        var result = await _service.DeleteAsync(999);

        Assert.False(result);
        _mappingRepo.Verify(r => r.DeleteAsync(It.IsAny<ScenarioMapping>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ExistingMapping_DeletesAndSaves_ReturnsTrue()
    {
        var mapping = new ScenarioMapping { MappingId = 1, SectionId = 10, ScenarioKeyword = "wages unpaid" };
        _mappingRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(mapping);

        var result = await _service.DeleteAsync(1);

        Assert.True(result);
        _mappingRepo.Verify(r => r.DeleteAsync(mapping), Times.Once);
        _mappingRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }
}
