using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "পূর্ণ নাম প্রয়োজন / Full Name is required")]
    [Display(Name = "পূর্ণ নাম / Full Name")]
    [StringLength(100, ErrorMessage = "সর্বোচ্চ ১০০ অক্ষর / Maximum 100 characters")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ইমেইল প্রয়োজন / Email is required")]
    [EmailAddress(ErrorMessage = "সঠিক ইমেইল দিন / Please enter a valid email")]
    [Display(Name = "ইমেইল / Email")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "সঠিক ফোন নম্বর দিন / Please enter a valid phone number")]
    [Display(Name = "ফোন নম্বর (ঐচ্ছিক) / Phone (Optional)")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "পাসওয়ার্ড প্রয়োজন / Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "কমপক্ষে ৬ অক্ষর প্রয়োজন / Minimum 6 characters")]
    [DataType(DataType.Password)]
    [Display(Name = "পাসওয়ার্ড / Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "পাসওয়ার্ড নিশ্চিত করুন / Confirm Password")]
    [Compare("Password", ErrorMessage = "পাসওয়ার্ড দুটি মিলছে না / Passwords do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "ভূমিকা / Role")]
    public string Role { get; set; } = "Citizen"; // "Citizen" or "Lawyer"

    [Display(Name = "বার রেজিস্ট্রেশন নম্বর / Bar Reg No (Lawyers only)")]
    public string? BarRegistrationNumber { get; set; }

    [Display(Name = "পছন্দের ভাষা / Preferred Language")]
    public string PreferredLanguage { get; set; } = "bn";
}
