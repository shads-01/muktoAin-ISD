using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Repositories;

namespace MuktoAin.UnitTests.Repositories;

public class RepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_ReturnsAddedEntity()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new Repository<CaseCategory>(context);
        var category = new CaseCategory { Name = "Test Category", Description = "desc" };

        await repo.AddAsync(category);
        await repo.SaveChangesAsync();

        var result = await repo.GetByIdAsync(category.CategoryId);

        Assert.NotNull(result);
        Assert.Equal("Test Category", result!.Name);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllAddedEntities()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new Repository<District>(context);
        await repo.AddAsync(new District { DistrictId = 1, Name = "Dhaka" });
        await repo.AddAsync(new District { DistrictId = 2, Name = "Chattogram" });
        await repo.SaveChangesAsync();

        var result = await repo.GetAllAsync();

        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new Repository<CaseCategory>(context);
        var category = new CaseCategory { Name = "Original", Description = "desc" };
        await repo.AddAsync(category);
        await repo.SaveChangesAsync();

        category.Name = "Updated";
        await repo.UpdateAsync(category);
        await repo.SaveChangesAsync();

        var result = await repo.GetByIdAsync(category.CategoryId);

        Assert.Equal("Updated", result!.Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesEntity()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new Repository<CaseCategory>(context);
        var category = new CaseCategory { Name = "ToDelete", Description = "desc" };
        await repo.AddAsync(category);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(category);
        await repo.SaveChangesAsync();

        var result = await repo.GetByIdAsync(category.CategoryId);

        Assert.Null(result);
    }

    // Regression test for the GetByIdAsync(object) fix (T-1.12/T-1.4 amendment) --
    // it used to be hardcoded to `int`, which didn't fit AiLog.LogId (long).
    [Fact]
    public async Task GetByIdAsync_WorksForLongKeyedEntity()
    {
        using var context = TestDbContextFactory.Create();
        var repo = new Repository<AiLog>(context);
        var log = new AiLog
        {
            RequestType = AiRequestType.LawIdentification,
            PromptText = "p",
            ResponseText = "r",
            ModelUsed = "m",
            CreatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(log);
        await repo.SaveChangesAsync();

        var result = await repo.GetByIdAsync(log.LogId);

        Assert.NotNull(result);
        Assert.Equal("m", result!.ModelUsed);
    }
}
