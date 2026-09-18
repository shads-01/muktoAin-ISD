using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class CreateAdminViewModel
{
    [Required(ErrorMessage = "পূর্ণ নাম প্রয়োজন / Full Name is required")]
    [Display(Name = "পূর্ণ নাম / Full Name")]
    [StringLength(150, ErrorMessage = "সর্বোচ্চ ১৫০ অক্ষর / Maximum 150 characters")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "ইমেইল প্রয়োজন / Email is required")]
    [EmailAddress(ErrorMessage = "সঠিক ইমেইল দিন / Please enter a valid email")]
    [Display(Name = "ইমেইল / Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "সুপার অ্যাডমিন / SuperAdmin")]
    public bool AsSuperAdmin { get; set; }
}
