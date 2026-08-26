using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.Infrastructure.Repositories;

public class ActSectionRepository : Repository<ActSection>, IActSectionRepository
{
    public ActSectionRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds)
    {
        // Manual parameterized query -- the joined id list is passed as a single
        // SQL parameter to STRING_SPLIT, not concatenated into the query text.
        var idsList = string.Join(",", sectionIds);
        return await _dbSet
            .FromSqlRaw("SELECT * FROM [dbo].[ACT_SECTION] WHERE SectionId IN (SELECT value FROM STRING_SPLIT({0}, ','))", idsList)
            .Include(s => s.Act)
            .ToListAsync();
    }

    public async Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults)
    {
        // Manual MSSQL FTS query using CONTAINSTABLE ranking against the
        // MuktoAinCatalog index (scripts/03_fulltext.sql). That index covers
        // SectionText only -- ACT_SECTION has no ActTitle column -- so [dbo].[ACT]
        // is joined in here to expose the title for filtering/display.
        return await _dbSet.FromSqlInterpolated($@"
            SELECT TOP({maxResults}) s.*
            FROM [dbo].[ACT_SECTION] s
            INNER JOIN CONTAINSTABLE([dbo].[ACT_SECTION], SectionText, {query}) AS ft
                ON s.SectionId = ft.[KEY]
            ORDER BY ft.[RANK] DESC")
            .Include(s => s.Act)
            .ToListAsync();
    }
}
