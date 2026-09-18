using System.Globalization;
using System.Resources;

namespace MuktoAin.Web.Resources;

// S-3.5: Resource class for shared localizations.
// Used as marker type for IStringLocalizer<SharedResource>, and provides
// static property accessors for System.ComponentModel.DataAnnotations
// ErrorMessageResourceType reflection lookup.
public class SharedResource
{
    private static readonly ResourceManager ResourceManager =
        new("MuktoAin.Web.Resources.SharedResource", typeof(SharedResource).Assembly);

    public static string? Register_FullName_Required =>
        ResourceManager.GetString(nameof(Register_FullName_Required), CultureInfo.CurrentUICulture);

    public static string? Register_FullName_MaxLength =>
        ResourceManager.GetString(nameof(Register_FullName_MaxLength), CultureInfo.CurrentUICulture);

    public static string? Register_Email_Required =>
        ResourceManager.GetString(nameof(Register_Email_Required), CultureInfo.CurrentUICulture);

    public static string? Register_Email_Invalid =>
        ResourceManager.GetString(nameof(Register_Email_Invalid), CultureInfo.CurrentUICulture);

    public static string? Register_Phone_Invalid =>
        ResourceManager.GetString(nameof(Register_Phone_Invalid), CultureInfo.CurrentUICulture);

    public static string? Register_Password_Required =>
        ResourceManager.GetString(nameof(Register_Password_Required), CultureInfo.CurrentUICulture);

    public static string? Register_Password_Length =>
        ResourceManager.GetString(nameof(Register_Password_Length), CultureInfo.CurrentUICulture);

    public static string? Register_Password_Policy =>
        ResourceManager.GetString(nameof(Register_Password_Policy), CultureInfo.CurrentUICulture);

    public static string? Register_Password_Mismatch =>
        ResourceManager.GetString(nameof(Register_Password_Mismatch), CultureInfo.CurrentUICulture);
}
