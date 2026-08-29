using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Auth;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly IRepository<LawyerProfile> _lawyerProfileRepo;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IRepository<LawyerProfile> lawyerProfileRepo,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _lawyerProfileRepo = lawyerProfileRepo;
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
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "ইমেইল অথবা পাসওয়ার্ড সঠিক নয়। / The email or password is incorrect.");
            return View(model);
        }

        if (user.AccountStatus == AccountStatus.Suspended)
        {
            ModelState.AddModelError(string.Empty, "আপনার একাউন্টটি স্থগিত করা হয়েছে। সহায়তার জন্য যোগাযোগ করুন। / Your account has been suspended. Please contact support.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            TempData["Success"] = "লগইন সফল হয়েছে!";
            TempData["SuccessEn"] = "Login successful!";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return user.Role switch
            {
                UserRole.Admin => RedirectToAction("Dashboard", "Admin"),
                UserRole.Lawyer => RedirectToAction("Queue", "Lawyer"),
                _ => RedirectToAction("Track", "Case")
            };
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "অনেকবার ভুল চেষ্টার কারণে একাউন্টটি সাময়িকভাবে লক হয়েছে। কিছুক্ষণ পর আবার চেষ্টা করুন। / Account temporarily locked due to too many failed attempts. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "ইমেইল অথবা পাসওয়ার্ড সঠিক নয়। / The email or password is incorrect.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var isLawyer = string.Equals(model.Role, "Lawyer", StringComparison.OrdinalIgnoreCase);
        if (isLawyer && string.IsNullOrWhiteSpace(model.BarRegistrationNumber))
        {
            ModelState.AddModelError("BarRegistrationNumber", "আইনজীবীদের জন্য বার রেজিস্ট্রেশন সনদ নম্বর আবশ্যক। / Bar Registration Number is required for lawyer accounts.");
            return View(model);
        }

        var user = new User
        {
            FullName = model.FullName,
            Email = model.Email,
            UserName = model.Email,
            PhoneNumber = model.PhoneNumber,
            Role = isLawyer ? UserRole.Lawyer : UserRole.Citizen,
            AccountStatus = AccountStatus.Active,
            PreferredLanguage = "bn",
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var (field, message) = IdentityErrorMapper.Map(error);
                ModelState.AddModelError(field ?? string.Empty, message);
            }
            return View(model);
        }

        if (isLawyer)
        {
            var profile = new LawyerProfile
            {
                UserId = user.Id,
                BarRegistrationNumber = model.BarRegistrationNumber!.Trim(),
                Specialization = model.Specialization,
                VerificationStatus = VerificationStatus.Pending
            };

            await _lawyerProfileRepo.AddAsync(profile);
            await _lawyerProfileRepo.SaveChangesAsync();
        }

        TempData["Success"] = "নিবন্ধন সম্পন্ন হয়েছে! আপনার একাউন্টে প্রবেশ করুন।";
        TempData["SuccessEn"] = "Registration complete! Please log in to your account.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        TempData["Info"] = "সফলভাবে লগআউট করা হয়েছে।";
        TempData["InfoEn"] = "You have been logged out successfully.";
        return RedirectToAction("Index", "Home");
    }
}
