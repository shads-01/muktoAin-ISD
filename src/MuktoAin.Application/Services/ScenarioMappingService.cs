using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// T-3.2 / FR-18: admin CRUD over the hand-curated SCENARIO_MAPPING keyword-boost
// table. The enrichment pattern (load all mappings — ~26 rows, load the mapped
// sections, load acts) mirrors RagContextBuilder.MergeScenarioPriorsAsync and
// AdminController.Scenarios, which already treat the table as small by design.
public class ScenarioMappingService : IScenarioMappingService
{
    private readonly IScenarioMappingRepository _mappingRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActRepository _actRepo;

    private const int MaxKeywordLength = 200;   // SCENARIO_MAPPING.ScenarioKeyword NVARCHAR(200)
    private const int MaxNotesLength = 500;     // SCENARIO_MAPPING.Notes NVARCHAR(500)

    public ScenarioMappingService(
        IScenarioMappingRepository mappingRepo,
        IActSectionRepository sectionRepo,
        IActRepository actRepo)
    {
        _mappingRepo = mappingRepo;
        _sectionRepo = sectionRepo;
        _actRepo = actRepo;
    }

    public async Task<IReadOnlyList<ScenarioMappingDto>> GetAllAsync()
    {
        var mappings = (await _mappingRepo.GetAllAsync())
            .OrderBy(m => m.MappingId)
            .ToList();
        if (mappings.Count == 0) return [];

        var sections = (await _sectionRepo.GetBySectionIdsAsync(mappings.Select(m => m.SectionId).Distinct()))
            .ToDictionary(s => s.SectionId);

        // 1,484 acts in memory is the established pattern here (RagContextBuilder
        // does exactly this on every retrieval); the table is small and static.
        var acts = (await _actRepo.GetAllAsync()).ToDictionary(a => a.ActId);

        return mappings.Select(m =>
        {
            sections.TryGetValue(m.SectionId, out var section);
            var act = section is null ? null : acts.GetValueOrDefault(section.ActId);
            return ToDto(m, section, act);
        }).ToList();
    }

    public async Task<ScenarioMappingDto?> GetByIdAsync(int mappingId)
    {
        var mapping = await _mappingRepo.GetByIdAsync(mappingId);
        if (mapping is null) return null;

        var section = await _sectionRepo.GetByIdAsync(mapping.SectionId);
        var act = section is null ? null : await _actRepo.GetByIdAsync(section.ActId);
        return ToDto(mapping, section, act);
    }

    public async Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto)
    {
        var (error, section, act) = await ValidateAndResolveAsync(dto, excludeMappingId: null);
        if (error is not null) return ScenarioMappingSaveResult.Fail(error);

        var mapping = new ScenarioMapping
        {
            SectionId = dto.SectionId,
            ScenarioKeyword = dto.ScenarioKeyword.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        };

        await _mappingRepo.AddAsync(mapping);
        await _mappingRepo.SaveChangesAsync();
        return ScenarioMappingSaveResult.Ok(ToDto(mapping, section, act));
    }

    public async Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto)
    {
        var existing = await _mappingRepo.GetByIdAsync(mappingId);
        if (existing is null)
        {
            return ScenarioMappingSaveResult.Fail($"Mapping {mappingId} was not found.");
        }

        var (error, section, act) = await ValidateAndResolveAsync(dto, excludeMappingId: mappingId);
        if (error is not null) return ScenarioMappingSaveResult.Fail(error);

        existing.SectionId = dto.SectionId;
        existing.ScenarioKeyword = dto.ScenarioKeyword.Trim();
        existing.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await _mappingRepo.UpdateAsync(existing);
        await _mappingRepo.SaveChangesAsync();
        return ScenarioMappingSaveResult.Ok(ToDto(existing, section, act));
    }

    public async Task<bool> DeleteAsync(int mappingId)
    {
        var mapping = await _mappingRepo.GetByIdAsync(mappingId);
        if (mapping is null) return false;

        await _mappingRepo.DeleteAsync(mapping);
        await _mappingRepo.SaveChangesAsync();
        return true;
    }

    // Single validation pass shared by Create/Update. Returns the resolved
    // section + act so the caller can build the enriched DTO without a re-read.
    // excludeMappingId lets Update keep its own (SectionId, Keyword) pair.
    private async Task<(string? Error, ActSection? Section, Act? Act)> ValidateAndResolveAsync(
        ScenarioMappingSaveDto dto, int? excludeMappingId)
    {
        if (dto.SectionId <= 0) return ("Section is required.", null, null);
        if (string.IsNullOrWhiteSpace(dto.ScenarioKeyword)) return ("Keyword is required.", null, null);
        if (dto.ScenarioKeyword.Trim().Length > MaxKeywordLength)
        {
            return ($"Keyword must be {MaxKeywordLength} characters or fewer (SCENARIO_MAPPING.ScenarioKeyword NVARCHAR(200)).", null, null);
        }
        if (dto.Notes is not null && dto.Notes.Trim().Length > MaxNotesLength)
        {
            return ($"Notes must be {MaxNotesLength} characters or fewer (SCENARIO_MAPPING.Notes NVARCHAR(500)).", null, null);
        }

        var section = await _sectionRepo.GetByIdAsync(dto.SectionId);
        if (section is null)
        {
            return ($"Section {dto.SectionId} was not found. SCENARIO_MAPPING.SectionId is a real FK to ACT_SECTION.", null, null);
        }

        var keyword = dto.ScenarioKeyword.Trim();
        var duplicate = (await _mappingRepo.GetAllAsync()).Any(m =>
            m.MappingId != excludeMappingId
            && m.SectionId == dto.SectionId
            && string.Equals(m.ScenarioKeyword, keyword, StringComparison.OrdinalIgnoreCase));
        if (duplicate)
        {
            return ($"A mapping for keyword '{keyword}' already exists on section {dto.SectionId}.", null, null);
        }

        var act = await _actRepo.GetByIdAsync(section.ActId);
        return (null, section, act);
    }

    private static ScenarioMappingDto ToDto(ScenarioMapping m, ActSection? section, Act? act)
        => new(
            m.MappingId,
            m.SectionId,
            act?.Title ?? string.Empty,
            section?.SectionNumber,
            m.ScenarioKeyword,
            m.Notes);
}
