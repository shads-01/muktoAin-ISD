using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActSectionRepository : IRepository<ActSection>
{
    Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds);
    Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults);

    // T-3.1 delete guard: these two FKs are NO ACTION in the schema, so any hit
    // must block the act delete before SQL ever throws.
    Task<int> CountCaseReferencesAsync(int actId);
    Task<int> CountScenarioMappingsAsync(int actId);
}
