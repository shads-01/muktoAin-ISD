using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class ScenarioMappingRepository : Repository<ScenarioMapping>, IScenarioMappingRepository
{
    public ScenarioMappingRepository(AppDbContext context) : base(context) { }

    // Small, hand-curated table (~26 rows) -- a plain parameterized LIKE is
    // appropriate; no need for a full-text index here.
    public async Task<IEnumerable<ScenarioMapping>> SearchByKeywordAsync(string keywordFragment)
        => await _dbSet.Where(m => EF.Functions.Like(m.ScenarioKeyword, $"%{keywordFragment}%")).ToListAsync();
}
