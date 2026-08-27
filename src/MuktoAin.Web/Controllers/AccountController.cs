using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class AccountController : Controller
{
    private readonly ILogger<AccountController> _logger;

    // CP2/CP3: Shads will inject SignInManager / UserManager here
    // private readonly SignInManager<ApplicationUser> _signInManager;
    // private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(ILogger<AccountController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // TODO: [Shads] Replace with SignInManager.PasswordSignInAsync()
        TempData["Success"] = "লগইন সফল হয়েছে (Mock Session Active)";
        
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        if (string.Equals(model.Email, "admin@muktoain.bd", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("Dashboard", "Admin");
        }

        if (string.Equals(model.Email, "lawyer@muktoain.bd", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("Queue", "Lawyer");
        }

        if (string.Equals(model.Email, "citizen@muktoain.bd", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction("Track", "Case");
        }

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // TODO: [Shads] Replace with UserManager.CreateAsync() and role assignment
        if (model.Role == "Lawyer" && string.IsNullOrWhiteSpace(model.BarRegistrationNumber))
        {
            ModelState.AddModelError("BarRegistrationNumber", "আইনজীবীদের জন্য বার রেজিস্ট্রেশন নম্বর আবশ্যক।");
            return View(model);
        }

        TempData["Success"] = "নিবন্ধন সম্পন্ন হয়েছে! আপনার একাউন্টে প্রবেশ করুন।";
        return RedirectToAction("Login");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        // TODO: [Shads] Replace with SignInManager.SignOutAsync()
        TempData["Info"] = "সফলভাবে লগআউট করা হয়েছে।";
        return RedirectToAction("Index", "Home");
    }
}
