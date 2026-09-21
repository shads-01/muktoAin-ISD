using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

// AUD-4: RowVersion tokens on GENERATED_DOCUMENT and CASE. InMemory can't
// generate real rowversion values, so these check the model metadata plus
// the repository's translation of EF's conflict into the domain exception.
public class RowVersionConcurrencyTests
{
    [Fact]
    public void GeneratedDocument_RowVersion_IsConcurrencyToken()
    {
        using var ctx = TestDbContextFactory.Create();
        var prop = ctx.Model.FindEntityType(typeof(GeneratedDocument))!.FindProperty("RowVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
    }

    [Fact]
    public void Case_RowVersion_IsConcurrencyToken()
    {
        using var ctx = TestDbContextFactory.Create();
        var prop = ctx.Model.FindEntityType(typeof(Case))!.FindProperty("RowVersion");
        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
    }

    [Fact]
    public async Task SaveChangesAsync_StaleRowVersion_ThrowsConcurrencyConflictException()
    {
        var dbName = "rv-conflict-" + Guid.NewGuid();
        using (var seed = TestDbContextFactory.Create(dbName))
        {
            seed.GeneratedDocuments.Add(new GeneratedDocument
            {
                DocumentId = 1, CaseId = 1, Status = DocumentStatus.UnderReview,
                RowVersion = new byte[] { 1 }
            });
            await seed.SaveChangesAsync();
        }

        using var first = TestDbContextFactory.Create(dbName);
        using var second = TestDbContextFactory.Create(dbName);
        var firstRepo = new Repository<GeneratedDocument>(first);
        var secondRepo = new Repository<GeneratedDocument>(second);

        var a = (await firstRepo.GetByIdAsync(1))!;
        var b = (await secondRepo.GetByIdAsync(1))!;

        // First writer wins and bumps the token (SQL Server does this itself).
        a.AssignedLawyerProfileId = 10;
        a.RowVersion = new byte[] { 2 };
        await firstRepo.SaveChangesAsync();

        b.AssignedLawyerProfileId = 20;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondRepo.SaveChangesAsync());
    }
}
