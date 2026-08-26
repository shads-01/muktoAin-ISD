using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class ActRepository : Repository<Act>, IActRepository
{
    public ActRepository(AppDbContext context) : base(context) { }

    public async Task<Act?> GetWithSectionsAsync(int actId)
        => await _dbSet.Include(a => a.Sections).FirstOrDefaultAsync(a => a.ActId == actId);

    // No full-text index exists on ACT.Title -- scripts/03_fulltext.sql only
    // indexes ACT_SECTION.SectionText -- so this is a plain parameterized LIKE,
    // not CONTAINSTABLE. EF.Functions.Like translates to a safe, parameterized
    // SQL LIKE rather than string concatenation.
    public async Task<IEnumerable<Act>> SearchByTitleAsync(string query)
        => await _dbSet.Where(a => EF.Functions.Like(a.Title, $"%{query}%")).ToListAsync();
}
