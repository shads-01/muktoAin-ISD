using Microsoft.AspNetCore.Identity;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Auth;

// Maps ASP.NET Core Identity's default IdentityResult error codes onto the
// RegisterViewModel field that produced them, with bilingual (bn/en) text, so
// server-side failures (like the password policy) surface next to the right
// field instead of only in the generic summary. Codes that don't map to a
// specific field fall back to a model-level error carrying Identity's own
// English description -- nothing is swallowed.
public static class IdentityErrorMapper
{
    private static readonly IReadOnlyDictionary<string, string> PasswordFieldErrors =
        new Dictionary<string, string>
        {
            ["PasswordTooShort"] =
                "পাসওয়ার্ড কমপক্ষে ৮ অক্ষরের হতে হবে। / Password must be at least 8 characters.",
            ["PasswordRequiresUpper"] =
                "পাসওয়ার্ডে কমপক্ষে একটি বড় হাতের অক্ষর (A-Z) থাকতে হবে। / Password must have at least one uppercase letter (A-Z).",
            ["PasswordRequiresLower"] =
                "পাসওয়ার্ডে কমপক্ষে একটি ছোট হাতের অক্ষর (a-z) থাকতে হবে। / Password must have at least one lowercase letter (a-z).",
            ["PasswordRequiresDigit"] =
                "পাসওয়ার্ডে কমপক্ষে একটি সংখ্যা (0-9) থাকতে হবে। / Password must have at least one digit (0-9).",
            ["PasswordRequiresNonAlphanumeric"] =
                "পাসওয়ার্ডে কমপক্ষে একটি বিশেষ চিহ্ন (যেমন @, #, !) থাকতে হবে। / Password must have at least one special character (e.g. @, #, !).",
            ["PasswordRequiresUniqueChars"] =
                "পাসওয়ার্ডে আরও আলাদা ধরণের অক্ষর ব্যবহার করুন। / Password must use more unique characters.",
            ["UserAlreadyHasPassword"] =
                "এই একাউন্টে ইতিমধ্যে একটি পাসওয়ার্ড সেট করা আছে। / This account already has a password set.",
        };

    private static readonly IReadOnlyDictionary<string, string> EmailFieldErrors =
        new Dictionary<string, string>
        {
            ["DuplicateUserName"] =
                "এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে। / An account with this email is already registered.",
            ["DuplicateEmail"] =
                "এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে। / An account with this email is already registered.",
            ["InvalidEmail"] =
                "সঠিক ইমেইল ঠিকানা দিন। / Please enter a valid email address.",
        };

    public static (string? Field, string Message) Map(IdentityError error)
    {
        if (PasswordFieldErrors.TryGetValue(error.Code, out var passwordMessage))
        {
            return (nameof(RegisterViewModel.Password), passwordMessage);
        }

        if (EmailFieldErrors.TryGetValue(error.Code, out var emailMessage))
        {
            return (nameof(RegisterViewModel.Email), emailMessage);
        }

        // Unknown/infra codes keep Identity's original (English) description at
        // model level so the error is never lost, just localized-by-fallback.
        return (null, error.Description);
    }
}