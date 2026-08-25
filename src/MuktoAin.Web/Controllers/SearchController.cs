using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class SearchController : Controller
{
    [HttpGet]
    public IActionResult Index(string? q, int page = 1)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return View(new SearchViewModel());
        }

        // TODO: [Tultul] Replace with SearchService.SearchActsAsync()
        var results = MockData.SampleSearchResults(q, page);
        return View(results);
    }
}
