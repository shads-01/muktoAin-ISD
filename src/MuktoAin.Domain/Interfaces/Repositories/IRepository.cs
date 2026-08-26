namespace MuktoAin.Domain.Interfaces.Repositories;

// Generic base. Entities with no custom query needs (District, CaseCategory,
// ActFootnote, ScenarioMapping's simple lookups, etc.) are consumed via this
// interface directly -- no dedicated per-entity interface required.
//
// GetByIdAsync takes `object` rather than `int` because not every entity's PK
// is an int (e.g. AiLog.LogId is a long, for its high-volume audit table).
// `object` matches EF Core's own DbSet<T>.FindAsync(params object?[] keyValues)
// shape and lets FindAsync resolve the real key type correctly; an `int`-typed
// parameter would silently mismatch for any non-int PK.
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
}
