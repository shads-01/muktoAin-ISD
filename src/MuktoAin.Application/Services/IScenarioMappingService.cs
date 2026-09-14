using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.2 / FR-18: admin CRUD over the hand-curated SCENARIO_MAPPING table that
// RagContextBuilder (T-2.3) consumes as FR-18 keyword priors. Validation must
// keep the table clean because every row here directly shapes RAG grounding:
// a bogus SectionId would silently no-op, a duplicate keyword would merge the
// same section twice.
public interface IScenarioMappingService
{
    Task<IReadOnlyList<ScenarioMappingDto>> GetAllAsync();
    Task<ScenarioMappingDto?> GetByIdAsync(int mappingId);
    Task<ScenarioMappingSaveResult> CreateAsync(ScenarioMappingSaveDto dto);
    Task<ScenarioMappingSaveResult> UpdateAsync(int mappingId, ScenarioMappingSaveDto dto);
    Task<bool> DeleteAsync(int mappingId);
}
