namespace MuktoAin.Domain.Interfaces.Repositories;

// Generic base. Entities with no custom query needs (District, CaseCategory,
// ActFootnote, ScenarioMapping's simple lookups, etc.) are consumed via this
// interface directly -- no dedicated per-entity interface required.
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
}
