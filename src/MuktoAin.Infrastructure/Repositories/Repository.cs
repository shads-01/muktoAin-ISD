using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

// Generic base per T-1.4/T-1.12. Entities with no custom query needs (District,
// CaseCategory, ActFootnote, GeneratedDocument, LawyerProfile, LawyerReview,
// AiLog, CaseActReference) are consumed via IRepository<T>/Repository<T>
// directly -- no dedicated per-entity interface or class required for them.
//
// Update/Delete don't call SaveChangesAsync themselves; callers call it
// explicitly as a separate step, matching how the seeders in Data/Seeding
// already batch changes before saving.
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(object id) => await _dbSet.FindAsync(id);
    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();
    public virtual async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);
    public virtual Task UpdateAsync(T entity) { _dbSet.Update(entity); return Task.CompletedTask; }
    public virtual Task DeleteAsync(T entity) { _dbSet.Remove(entity); return Task.CompletedTask; }
    public virtual async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
