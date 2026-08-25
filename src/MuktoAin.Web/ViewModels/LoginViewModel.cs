using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "ইমেইল প্রয়োজন / Email is required")]
    [EmailAddress(ErrorMessage = "সঠিক ইমেইল দিন / Please enter a valid email")]
    [Display(Name = "ইমেইল / Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "পাসওয়ার্ড প্রয়োজন / Password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "পাসওয়ার্ড / Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "মনে রাখুন / Remember me")]
    public bool RememberMe { get; set; }
}
