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
// carrying Identity's own English description. Nothing is swallowed.
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

    public static (string? Field, string Message) Map(
        IdentityError error, IStringLocalizer<SharedResource> localizer)
    {
        if (CodeToField.TryGetValue(error.Code, out var field))
        {
            var localized = localizer["IdentityError_" + error.Code];

            if (!localized.ResourceNotFound)
            {
                return (field, localized.Value);
            }
        }

        // Unknown/infra codes keep Identity's original (English) description at
        // model level so the error is never lost, just localized-by-fallback.
        return (null, error.Description);
    }
}