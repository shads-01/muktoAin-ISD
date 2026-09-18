using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using MuktoAin.Web.Resources;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Auth;

// Maps ASP.NET Core Identity's default IdentityResult error codes onto the
// RegisterViewModel field that produced them. Since S-3.5 the message TEXT
// lives in Resources/SharedResource.{bn,en}.resx and is resolved per request
// via IStringLocalizer (RequestLocalization sets the culture from the same
// mkt-lang cookie the client-side toggle uses). Codes that don't map to a
// specific field — or have no resx entry — fall back to a model-level error
// carrying Identity's own English description (or bilingual fallback). Nothing is swallowed.
public static class IdentityErrorMapper
{
    private static readonly IReadOnlyDictionary<string, string> CodeToField =
        new Dictionary<string, string>
        {
            ["PasswordTooShort"] = nameof(RegisterViewModel.Password),
            ["PasswordRequiresUpper"] = nameof(RegisterViewModel.Password),
            ["PasswordRequiresLower"] = nameof(RegisterViewModel.Password),
            ["PasswordRequiresDigit"] = nameof(RegisterViewModel.Password),
            ["PasswordRequiresNonAlphanumeric"] = nameof(RegisterViewModel.Password),
            ["PasswordRequiresUniqueChars"] = nameof(RegisterViewModel.Password),
            ["UserAlreadyHasPassword"] = nameof(RegisterViewModel.Password),
            ["DuplicateUserName"] = nameof(RegisterViewModel.Email),
            ["DuplicateEmail"] = nameof(RegisterViewModel.Email),
            ["InvalidEmail"] = nameof(RegisterViewModel.Email),
        };

    private static readonly IReadOnlyDictionary<string, string> FallbackBilingualErrors =
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
            ["DuplicateUserName"] =
                "এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে। / An account with this email is already registered.",
            ["DuplicateEmail"] =
                "এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে। / An account with this email is already registered.",
            ["InvalidEmail"] =
                "সঠিক ইমেইল ঠিকানা দিন। / Please enter a valid email address.",
        };

    public static (string? Field, string Message) Map(IdentityError error)
        => Map(error, null);

    public static (string? Field, string Message) Map(
        IdentityError error, IStringLocalizer<SharedResource>? localizer)
    {
        if (CodeToField.TryGetValue(error.Code, out var field))
        {
            if (localizer != null)
            {
                var localized = localizer["IdentityError_" + error.Code];

                if (!localized.ResourceNotFound)
                {
                    return (field, localized.Value);
                }
            }

            if (FallbackBilingualErrors.TryGetValue(error.Code, out var fallback))
            {
                return (field, fallback);
            }

            return (field, error.Description);
        }

        // Unknown/infra codes keep Identity's original (English) description at
        // model level so the error is never lost, just localized-by-fallback.
        return (null, error.Description);
    }
}
