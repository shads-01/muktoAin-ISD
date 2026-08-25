using Microsoft.AspNetCore.Mvc;

namespace MuktoAin.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public IActionResult About()
    {
        return View();
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View("AccessDenied");
    }

    [HttpGet]
    [Route("Home/NotFound")]
    public new IActionResult NotFound()
    {
        Response.StatusCode = 404;
        return View("NotFound");
    }

    [HttpGet]
    public IActionResult ServerError()
    {
        Response.StatusCode = 500;
        return View("ServerError");
    }

    [HttpGet]
    public IActionResult Error(int? statusCode = null)
    {
        if (statusCode == 403) return RedirectToAction("AccessDenied");
        if (statusCode == 404) return RedirectToAction("NotFound");
        if (statusCode >= 500) return RedirectToAction("ServerError");

        return View("ServerError");
    }
}
