using MuktoAin.Domain.Entities;

namespace MuktoAin.Domain.Interfaces.Repositories;

public interface IActRepository : IRepository<Act>
{
    Task<Act?> GetWithSectionsAsync(int actId);
    Task<IEnumerable<Act>> SearchByTitleAsync(string query);

    // T-3.1: admin paging for the /Admin/Acts list. LIKE filter on Title (same
    // rationale as SearchByTitleAsync -- no FTS index exists on ACT.Title), with
    // the total count so the view can render a pager. Page/pageSize are clamped
    // by the service, not here. Sections are Included so the page can show
    // SectionCount without a second query.
    Task<(IReadOnlyList<Act> Items, int TotalCount)> GetPagedAsync(string? keyword, int page, int pageSize);

    // ActImportService's natural key is (Title, Year); create/update must preserve it.
    Task<bool> ExistsByTitleYearAsync(string title, int year, int? excludeActId = null);

    // T-3.1 / FR-17: full cascade delete ACT -> ACT_FOOTNOTE / ACT_SECTION ->
    // ACT_SECTION_CHUNK. The schema FKs are NO ACTION (no ON DELETE CASCADE in
    // scripts/02_schema.sql), so children are deleted explicitly, chunk-first,
    // in one transaction. Returns 1 when the ACT row was deleted, 0 when absent.
    // Raw SQL -- not unit-testable on InMemory; T-3.3 integration coverage.
    Task<int> DeleteWithChildrenAsync(int actId);
}
