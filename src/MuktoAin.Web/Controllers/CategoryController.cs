using Microsoft.AspNetCore.Mvc;

namespace MuktoAin.Web.Controllers;

public class CategoryController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View(MockData.CategoriesDetailed);
    }

    [HttpGet]
    public IActionResult Details(int id)
    {
        var category = MockData.CategoriesDetailed.FirstOrDefault(c => c.CategoryId == id)
                       ?? MockData.CategoriesDetailed.First();
        return View(category);
    }
}
