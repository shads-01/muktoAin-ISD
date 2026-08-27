using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class CategoryController : Controller
{
    // Lucide icon per seeded CategoryId (data/categories.json is fixed, 4 rows) --
    // CaseCategory itself has no Icon column, so this is presentation-only mapping.
    // Falls back to "folder" for any category outside the seeded 4.
    private static readonly Dictionary<int, string> IconsByCategoryId = new()
    {
        [1] = "briefcase",     // Labour Complaint
        [2] = "shield",        // General Diary (GD)
        [3] = "file-text",     // RTI Request
        [4] = "shopping-bag",  // Consumer Complaint
    };

    private readonly CategoryService _categoryService;

    public CategoryController(CategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var categories = await _categoryService.GetAllCategoriesAsync();
        return View(categories.OrderBy(c => c.CategoryId).Select(ToViewModel).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var category = await _categoryService.GetByIdAsync(id);
        if (category == null) return NotFound();

        return View(ToViewModel(category));
    }

    private static CategoryViewModel ToViewModel(CaseCategory category)
    {
        return new CategoryViewModel
        {
            CategoryId = category.CategoryId,
            NameBn = category.NameBn,
            NameEn = category.Name,
            DescriptionBn = category.DescriptionBn,
            DescriptionEn = category.Description,
            Icon = IconsByCategoryId.GetValueOrDefault(category.CategoryId, "folder"),
            CommonActions = category.CommonActions
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .ToList(),
            CommonActionsEn = category.CommonActionsEn
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .ToList(),
        };
    }
}
