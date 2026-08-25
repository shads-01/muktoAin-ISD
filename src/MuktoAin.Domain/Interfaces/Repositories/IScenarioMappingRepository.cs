using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

// Consumed by the AI orchestration layer's prompt assembly step.
public interface IScenarioMappingRepository : IRepository<ScenarioMapping>
{
    Task<IEnumerable<ScenarioMapping>> SearchByKeywordAsync(string keywordFragment);
}
