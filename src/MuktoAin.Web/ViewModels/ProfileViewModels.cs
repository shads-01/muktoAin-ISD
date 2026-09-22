using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class ProfileViewModel
{
    public int UserId { get; set; }

    // Read-only system & account fields
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Citizen"; // "Citizen", "Lawyer", "Admin"
    public string AccountStatus { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The actual saved phone number, for the read-only hero-card summary.
    // Kept separate from the editable PhoneNumber below: on a rejected save,
    // PhoneNumber still echoes the rejected submission (so the user's typo
    // isn't lost), but the summary must always show what's really persisted --
    // otherwise a rejected, unsaved attempt looks identical to a saved one.
    public string? CurrentPhoneNumber { get; set; }

    // Editable personal information
    [Required(ErrorMessage = "পূর্ণ নাম আবশ্যক / Full Name is required")]
    [Display(Name = "পূর্ণ নাম / Full Name")]
    [StringLength(100, ErrorMessage = "সর্বোচ্চ ১০০ অক্ষর / Maximum 100 characters")]
    public string FullName { get; set; } = string.Empty;

    // [Phone] is too permissive (e.g. it accepts "017"); this pins the real
    // Bangladesh mobile format instead: 11 digits starting 01[3-9], with an
    // optional +880/880 country code. Empty is allowed since the field is
    // optional (and blank must stay valid so a saved number can be cleared).
    [RegularExpression(@"^$|^(?:\+?880|0)1[3-9]\d{8}$",
        ErrorMessage = "সঠিক বাংলাদেশী মোবাইল নম্বর দিন (যেমন ০১XXXXXXXXX) / Please enter a valid Bangladeshi mobile number (e.g. 01XXXXXXXXX)")]
    [Display(Name = "ফোন নম্বর / Phone Number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "পছন্দের ভাষা / Preferred Language")]
    public string PreferredLanguage { get; set; } = "bn";

    // Lawyer-specific credentials (if Role == "Lawyer")
    public string? BarRegistrationNumber { get; set; } // Read-only official record
    public string? VerificationStatus { get; set; } = "Pending"; // Read-only (Admin approved)
    public DateTime? VerifiedAt { get; set; }

    [Display(Name = "বিশেষজ্ঞতার ক্ষেত্র / Specialization")]
    public string? Specialization { get; set; } // Editable

    [Display(Name = "চেম্বার / অফিসের ঠিকানা / Chamber Address")]
    public string? ChamberAddress { get; set; } // Editable

    // Role-specific activity metrics
    public int TotalCasesSubmitted { get; set; }
    public int TotalReviewsCompleted { get; set; }
}

public class EarningRowViewModel
{
    public int PaymentOrderId { get; set; }
    public int CaseId { get; set; }
    public decimal Gross { get; set; }
    public decimal Commission { get; set; }
    public decimal Net { get; set; }
    public DateTime PaidAt { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "বর্তমান পাসওয়ার্ড দিন / Current password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "বর্তমান পাসওয়ার্ড / Current Password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "নতুন পাসওয়ার্ড দিন / New password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "কমপক্ষে ৮ অক্ষর প্রয়োজন / Minimum 8 characters")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,100}$",
        ErrorMessage = "পাসওয়ার্ডে ছোট হাতের অক্ষর, বড় হাতের অক্ষর, সংখ্যা ও বিশেষ চিহ্ন থাকতে হবে / Password must contain upper, lower, number, and special character")]
    [DataType(DataType.Password)]
    [Display(Name = "নতুন পাসওয়ার্ড / New Password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "নতুন পাসওয়ার্ড নিশ্চিত করুন / Confirm New Password")]
    [Compare("NewPassword", ErrorMessage = "পাসওয়ার্ড দুটি মিলছে না / Passwords do not match")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
