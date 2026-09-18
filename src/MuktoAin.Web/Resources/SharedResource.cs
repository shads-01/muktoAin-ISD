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

    private static string? GetStringSafe(string name)
    {
        try
        {
            return ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
                ?? ResourceManager.GetString(name, CultureInfo.InvariantCulture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    public static string? Register_FullName_Required =>
        GetStringSafe(nameof(Register_FullName_Required));

    public static string? Register_FullName_MaxLength =>
        GetStringSafe(nameof(Register_FullName_MaxLength));

    public static string? Register_Email_Required =>
        GetStringSafe(nameof(Register_Email_Required));

    public static string? Register_Email_Invalid =>
        GetStringSafe(nameof(Register_Email_Invalid));

    public static string? Register_Phone_Invalid =>
        GetStringSafe(nameof(Register_Phone_Invalid));

    public static string? Register_Password_Required =>
        GetStringSafe(nameof(Register_Password_Required));

    public static string? Register_Password_Length =>
        GetStringSafe(nameof(Register_Password_Length));

    public static string? Register_Password_Policy =>
        GetStringSafe(nameof(Register_Password_Policy));

    public static string? Register_Password_Mismatch =>
        GetStringSafe(nameof(Register_Password_Mismatch));
}
