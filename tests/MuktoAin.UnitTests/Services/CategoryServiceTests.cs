using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class CategoryServiceTests
{
    private readonly Mock<IRepository<CaseCategory>> _categoryRepo = new();
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _service = new CategoryService(_categoryRepo.Object);
    }

    [Fact]
    public async Task GetAllCategoriesAsync_ReturnsAllFromRepository()
    {
        var categories = new List<CaseCategory>
        {
            new() { CategoryId = 1, Name = "Labour", Description = "Labour disputes" },
            new() { CategoryId = 2, Name = "Consumer", Description = "Consumer rights" },
        };
        _categoryRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(categories);

        var result = await _service.GetAllCategoriesAsync();

        Assert.Equal(categories, result);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMatchingCategory()
    {
        var category = new CaseCategory { CategoryId = 5, Name = "RTI", Description = "Right to Information" };
        _categoryRepo.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(category);

        var result = await _service.GetByIdAsync(5);

        Assert.Equal(category, result);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        _categoryRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((CaseCategory?)null);

        var result = await _service.GetByIdAsync(999);

        Assert.Null(result);
    }
}
