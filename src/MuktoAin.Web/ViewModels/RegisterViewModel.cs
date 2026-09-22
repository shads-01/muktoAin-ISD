using System.ComponentModel.DataAnnotations;
using MuktoAin.Web.Resources;

namespace MuktoAin.Web.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Register_FullName_Required")]
    [Display(Name = "পূর্ণ নাম / Full Name")]
    [StringLength(100, ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Register_FullName_MaxLength")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Register_Email_Required")]
    [EmailAddress(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Register_Email_Invalid")]
    [Display(Name = "ইমেইল / Email")]
    public string Email { get; set; } = string.Empty;

    // Same issue as ProfileViewModel.PhoneNumber: [Phone] is too permissive
    // (e.g. it accepts "017"), so this pins the real Bangladesh mobile format
    // instead. Empty is allowed since the field is optional.
    [RegularExpression(@"^$|^(?:\+?880|0)1[3-9]\d{8}$",
        ErrorMessageResourceType = typeof(SharedResource),
        ErrorMessageResourceName = "Register_Phone_Invalid")]
    [Display(Name = "ফোন নম্বর (ঐচ্ছিক) / Phone (Optional)")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessageResourceType = typeof(SharedResource), ErrorMessageResourceName = "Register_Password_Required")]
    [StringLength(100, MinimumLength = 8,
        ErrorMessageResourceType = typeof(SharedResource),
        ErrorMessageResourceName = "Register_Password_Length")]
    // Mirrors the Identity password policy configured in Program.cs
    // (RequireDigit/RequireUppercase/RequireLowercase/RequireNonAlphanumeric,
    // RequiredLength = 8) so users get the same feedback before submitting.
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,100}$",
        ErrorMessageResourceType = typeof(SharedResource),
        ErrorMessageResourceName = "Register_Password_Policy")]
    [DataType(DataType.Password)]
    [Display(Name = "পাসওয়ার্ড / Password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "পাসওয়ার্ড নিশ্চিত করুন / Confirm Password")]
    [Compare("Password",
        ErrorMessageResourceType = typeof(SharedResource),
        ErrorMessageResourceName = "Register_Password_Mismatch")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [Display(Name = "ভূমিকা / Role")]
    public string Role { get; set; } = "Citizen"; // "Citizen" or "Lawyer"

    [Display(Name = "বার রেজিস্ট্রেশন নম্বর / Bar Reg No (Lawyers only)")]
    [StringLength(100, ErrorMessage = "সর্বোচ্চ ১০০ অক্ষর / Maximum 100 characters")]
    public string? BarRegistrationNumber { get; set; }

    [Display(Name = "বিশেষজ্ঞতা / Specialization (Lawyers only)")]
    [StringLength(200, ErrorMessage = "সর্বোচ্চ ২০০ অক্ষর / Maximum 200 characters")]
    public string? Specialization { get; set; }

    [Display(Name = "পছন্দের ভাষা / Preferred Language")]
    public string PreferredLanguage { get; set; } = "bn";
}
