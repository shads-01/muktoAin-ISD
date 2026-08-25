using Microsoft.AspNetCore.Mvc;

namespace MuktoAin.Web.Controllers;

// [Authorize(Roles = "Admin")] // TODO: [Shads] Enable after Identity role claims configured
public class AdminController : Controller
{
    private readonly ILogger<AdminController> _logger;

    public AdminController(ILogger<AdminController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        ViewData["IsAdminPage"] = true;
        return View(MockData.SampleAnalytics);
    }
}
