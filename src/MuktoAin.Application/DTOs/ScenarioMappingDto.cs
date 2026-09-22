namespace MuktoAin.Application.DTOs;

// T-3.2 read/save models for the /Admin/Scenarios keyword-boost console (E-3.2).
// The SCENARIO_MAPPING schema (scripts/02_schema.sql) is keyword -> section:
// ScenarioKeyword NVARCHAR(200), Notes NVARCHAR(500), SectionId FK. There are no
// category/boost columns -- the FR-18 "boost" is realized by RagContextBuilder,
// which merges every mapped section whose ScenarioKeyword appears in the citizen
// query into the retrieved context at 1.0f curated-prior relevance
// (src/MuktoAin.Application/Services/RagContextBuilder.cs, MergeScenarioPriorsAsync).
// Choosing a section via this admin surface IS the section hint; the keyword IS
// the boost trigger.

public record ScenarioMappingDto(
    int MappingId,
    int SectionId,
    string ActTitle,
    string? SectionNumber,
    string ScenarioKeyword,
    string? Notes);

public record ScenarioMappingSaveDto(
    int? MappingId,
    int SectionId,
    string ScenarioKeyword,
    string? Notes);

public record ScenarioMappingSaveResult(bool Success, string? Error, ScenarioMappingDto? Mapping)
{
    public static ScenarioMappingSaveResult Ok(ScenarioMappingDto mapping) => new(true, null, mapping);
    public static ScenarioMappingSaveResult Fail(string error) => new(false, error, null);
}
