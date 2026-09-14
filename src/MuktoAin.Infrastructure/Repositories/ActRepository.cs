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

    public async Task<(IReadOnlyList<Act> Items, int TotalCount)> GetPagedAsync(string? keyword, int page, int pageSize)
    {
        var query = _dbSet.AsNoTracking();

        // Same EF.Functions.Like rationale as SearchByTitleAsync: parameterized
        // LIKE, never string concatenation.
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{keyword.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Title, pattern));
        }

        var total = await query.CountAsync();
        var items = await query
            .Include(a => a.Sections) // page-sized; lets the service show SectionCount
            .OrderBy(a => a.Title).ThenBy(a => a.Year)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> ExistsByTitleYearAsync(string title, int year, int? excludeActId = null)
        => await _dbSet.AnyAsync(a =>
            a.Title == title
            && a.Year == year
            && (excludeActId == null || a.ActId != excludeActId));

    public async Task<int> DeleteWithChildrenAsync(int actId)
    {
        // Program.cs configures EnableRetryOnFailure (FIX-DB-1) -- an explicit
        // transaction must execute through the execution strategy or EF throws.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE c FROM [dbo].[ACT_SECTION_CHUNK] AS c
                INNER JOIN [dbo].[ACT_SECTION] AS s ON s.[SectionId] = c.[SectionId]
                WHERE s.[ActId] = {actId}");
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT_FOOTNOTE] WHERE [ActId] = {actId}");
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT_SECTION] WHERE [ActId] = {actId}");
            var deleted = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [dbo].[ACT] WHERE [ActId] = {actId}");

            await transaction.CommitAsync();
            return deleted;
        });
    }
}
