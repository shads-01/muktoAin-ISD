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
                _ => RedirectToAction("Index", "Home")
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

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction(nameof(Login));
        }

        var vm = new ProfileViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            AccountStatus = user.AccountStatus.ToString(),
            PreferredLanguage = user.PreferredLanguage ?? "bn",
            CreatedAt = user.CreatedAt
        };

        if (user.Role == UserRole.Lawyer)
        {
            var profiles = await _lawyerProfileRepo.FindAsync(p => p.UserId == user.Id);
            var lawyerProfile = profiles.FirstOrDefault();
            if (lawyerProfile != null)
            {
                vm.BarRegistrationNumber = lawyerProfile.BarRegistrationNumber;
                vm.Specialization = lawyerProfile.Specialization;
                vm.VerificationStatus = lawyerProfile.VerificationStatus.ToString();
                vm.VerifiedAt = lawyerProfile.VerifiedAt;
                vm.TotalReviewsCompleted = lawyerProfile.Reviews?.Count ?? 0;
            }
        }

        return View(vm);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            model.Email = user.Email ?? string.Empty;
            model.Role = user.Role.ToString();
            model.AccountStatus = user.AccountStatus.ToString();
            model.CreatedAt = user.CreatedAt;
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.PhoneNumber = model.PhoneNumber?.Trim();
        user.PreferredLanguage = model.PreferredLanguage ?? "bn";

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var (field, msg) = IdentityErrorMapper.Map(error);
                ModelState.AddModelError(field ?? string.Empty, msg);
            }
            return View(model);
        }

        if (user.Role == UserRole.Lawyer)
        {
            var profiles = await _lawyerProfileRepo.FindAsync(p => p.UserId == user.Id);
            var lawyerProfile = profiles.FirstOrDefault();
            if (lawyerProfile != null)
            {
                lawyerProfile.Specialization = model.Specialization?.Trim();
                await _lawyerProfileRepo.SaveChangesAsync();
            }
        }

        TempData["Success"] = "প্রোফাইল তথ্য সফলভাবে আপডেট করা হয়েছে!";
        TempData["SuccessEn"] = "Profile updated successfully!";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "পাসওয়ার্ড পরিবর্তনের তথ্য সঠিক নয়। অনুগ্রহ করে শর্তাবলী মেনে আবার চেষ্টা করুন।";
            TempData["ErrorEn"] = "Invalid password data. Please check requirements and try again.";
            return RedirectToAction(nameof(Profile));
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData["Success"] = "পাসওয়ার্ড সফলভাবে পরিবর্তন করা হয়েছে!";
            TempData["SuccessEn"] = "Password changed successfully!";
        }
        else
        {
            var firstErr = result.Errors.FirstOrDefault()?.Description ?? "পাসওয়ার্ড পরিবর্তন ব্যর্থ হয়েছে।";
            TempData["Error"] = $"পাসওয়ার্ড পরিবর্তন ব্যর্থ হয়েছে: {firstErr}";
            TempData["ErrorEn"] = $"Password change failed: {firstErr}";
        }

        return RedirectToAction(nameof(Profile));
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
