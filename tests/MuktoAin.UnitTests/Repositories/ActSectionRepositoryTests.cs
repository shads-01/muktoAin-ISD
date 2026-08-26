namespace MuktoAin.UnitTests.Repositories;

// Intentionally no test methods here. Both of ActSectionRepository's methods
// (GetBySectionIdsAsync via FromSqlRaw/STRING_SPLIT, FullTextSearchAsync via
// CONTAINSTABLE) execute raw SQL against SQL-Server-specific features the
// InMemory provider can't translate or execute at all. Per Tultul_plan.md
// Step 3.3's own note, these are covered in the T-3.3 integration tests
// against a real SQL Server instance instead.
public class ActSectionRepositoryTests
{
}
