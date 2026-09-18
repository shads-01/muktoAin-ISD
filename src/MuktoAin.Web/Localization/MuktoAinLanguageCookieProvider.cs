using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace MuktoAin.Web.Localization;

// S-3.5: reads the "mkt-lang" cookie that the EXISTING client-side language
// toggle already writes on every switch (wwwroot/assets/js/main.js, the
// document.cookie assignment) — main.js values are "bn"/"en". Mapped onto the
// full cultures so RequestLocalization drives server-rendered strings
// (validation messages, identity errors) in the SAME language the user picked
// client-side. The data-bn/data-en swapping in main.js remains the primary UI
// translation mechanism — this provider only serves the server-side subset.
public sealed class MuktoAinLanguageCookieProvider : RequestCultureProvider
{
    public const string CookieName = "mkt-lang";

    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(
        HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var lang))
        {
            // Anything other than an explicit "en" (including absent-but-present
            // garbage) falls back to the platform default: Bangla (bn-BD).
            var culture = lang == "en" ? "en" : "bn-BD";

            return Task.FromResult<ProviderCultureResult?>(
                new ProviderCultureResult(culture, culture));
        }

        // No cookie yet (first visit): let the default culture (bn-BD) apply.
        return Task.FromResult<ProviderCultureResult?>(null);
    }
}
