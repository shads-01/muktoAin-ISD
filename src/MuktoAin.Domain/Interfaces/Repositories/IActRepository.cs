using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActRepository : IRepository<Act>
{
    Task<Act?> GetWithSectionsAsync(int actId);
    Task<IEnumerable<Act>> SearchByTitleAsync(string query);
}
