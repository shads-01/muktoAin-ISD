using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class CaseRepository : Repository<Case>, ICaseRepository
{
    public CaseRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<Case>> GetByUserIdAsync(int userId)
        => await _dbSet.Where(c => c.UserId == userId).ToListAsync();

    public async Task<Case?> GetWithDocumentsAsync(int caseId)
        => await _dbSet.Include(c => c.Documents).FirstOrDefaultAsync(c => c.CaseId == caseId);
}
