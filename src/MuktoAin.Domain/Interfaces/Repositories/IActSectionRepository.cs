using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActSectionRepository : IRepository<ActSection>
{
    Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds);
    Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults);
}
