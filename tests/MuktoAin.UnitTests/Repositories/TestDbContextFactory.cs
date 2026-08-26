using Microsoft.EntityFrameworkCore;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.UnitTests.Repositories;

// Each call gets its own isolated InMemory database (unique name per call) so
// tests don't see each other's data, matching Tultul_plan.md Step 3.3's pattern.
internal static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("TestDb_" + Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }
}
