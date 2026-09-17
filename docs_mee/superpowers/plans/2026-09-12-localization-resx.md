# RequestLocalizationMiddleware & .resx Resource Files (S-3.5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire server-side localization — `RequestLocalizationMiddleware` (default `bn-BD`, plus `en`) driven by the language cookie the existing client-side toggle already writes — and migrate a REPRESENTATIVE SUBSET of server-rendered strings (ViewModel validation messages + AccountController identity/status error messages via the existing `IdentityErrorMapper.cs`) to `.resx` resources.

**Architecture:** A tiny custom `RequestCultureProvider` (`MuktoAinLanguageCookieProvider`) reads the `mkt-lang` cookie (values `bn`/`en`) that `wwwroot/assets/js/main.js` already sets on every toggle — no new cookie, no new toggle, no server endpoint. `Program.cs` gains `AddLocalization()` + `Configure<RequestLocalizationOptions>` + `UseRequestLocalization()` (first middleware in the pipeline). `IStringLocalizer<SharedResource>` (marker type + `.bn.resx`/`.en.resx` already created by E-2.8, currently unwired) gets new keys; `IdentityErrorMapper` switches from hardcoded `"bn / en"` combined strings to localizer lookups with graceful fallback to Identity's own description for unknown codes; `RegisterViewModel` validation attributes switch to `ErrorMessageResourceType`/`ErrorMessageResourceName` (so DataAnnotations resolve per-request culture).

**Tech Stack:** ASP.NET Core MVC (.NET 8) `Microsoft.Extensions.Localization` (`IStringLocalizer<T>`, `ResourceManagerStringLocalizerFactory`), `RequestLocalizationMiddleware`, DataAnnotations localization, xUnit + Moq.

**Spec:** `plans/Dependency_plan.md` `[S-3.5]` (blocked by `[E-2.8]` — DONE: `SharedResource.bn.resx` / `SharedResource.en.resx` populated with UI strings, `docs/api-contracts.md`) · `AGENTS.md` §2 (stack) · H-9 handoff row (Localization Middleware delivered by Shads on Erin's `.resx` work).

## Global Constraints

- **Language-system boundary (explicit):** The client-side `data-bn`/`data-en` swapping in `wwwroot/assets/js/main.js` (localStorage `mkt-lang` + `mkt-lang` cookie) remains the **PRIMARY UI translation mechanism** for ALL Razor view text. Do NOT convert views to `@localizer[...]`, do NOT remove any `data-bn`/`data-en` attributes, do NOT rewrite `_Layout.cshtml` content. `.resx` covers **server-side messages only**: DataAnnotations validation errors, Identity error mapping, and controller-added `ModelState.AddModelError` strings.
- **Cookie contract (frozen):** The provider reads ONLY the existing `mkt-lang` cookie (values `bn` / `en`, written by `main.js:403` as `document.cookie = "mkt-lang=" + currentLang + ";path=/;max-age=31536000;SameSite=Lax"`). Do NOT add a second culture cookie (`.AspNetCore.Culture`), a `SetLanguage` controller endpoint, or a second toggle button.
- Supported cultures: `bn-BD` (default) and `en` — nothing else; `RequestCultureProviders` list is REPLACED with only `MuktoAinLanguageCookieProvider` (no querystring/header providers, keeping server behavior predictable).
- Bilingual text for new/modified messages must carry over the EXISTING Bangla/English wording verbatim (splitting the current `"bn / en"` combined strings into per-file values) — no re-translation.
- **NO git commit steps** — per `AGENTS.md` §6 Shads is the sole committer. The final task records completion in `plans/Dependency_plan.md` instead; all changes stay in the working tree.
- Tests follow existing conventions: xUnit under `tests/MuktoAin.UnitTests/`, Moq for doubles, no new packages needed.

---

### Task 1: `MuktoAinLanguageCookieProvider` + `Program.cs` wiring

**Files:**
- Create: `src/MuktoAin.Web/Localization/MuktoAinLanguageCookieProvider.cs`
- Modify: `src/MuktoAin.Web/Program.cs` (two edits: service registration block, middleware pipeline)
- Test: `tests/MuktoAin.UnitTests/Localization/MuktoAinLanguageCookieProviderTests.cs`

**Interfaces:**
- Consumes: the `mkt-lang` cookie contract from `main.js:403` (`bn` / `en`).
- Produces: `MuktoAinLanguageCookieProvider` (public, `RequestCultureProvider` subclass, const `string CookieName = "mkt-lang"`), `IStringLocalizer` service registration — Task 2 and Task 3 consume the localized `CultureInfo.CurrentUICulture` this middleware sets per request.

- [ ] **Step 1: Write the failing provider test**

Create `tests/MuktoAin.UnitTests/Localization/MuktoAinLanguageCookieProviderTests.cs`:

```csharp
using System.Linq;
using Microsoft.AspNetCore.Http;
using MuktoAin.Web.Localization;

namespace MuktoAin.UnitTests.Localization;

// S-3.5: the provider bridges the EXISTING client-side toggle (main.js sets the
// "mkt-lang" cookie on every switch) to server-side RequestLocalization. The
// mapping must stay in lockstep with main.js's "bn"/"en" values.
public class MuktoAinLanguageCookieProviderTests
{
    private static ProviderCultureResult? Resolve(string? cookieValue)
    {
        var provider = new MuktoAinLanguageCookieProvider();
        var context = new DefaultHttpContext();

        if (cookieValue is not null)
        {
            context.Request.Headers.Cookie =
                $"{MuktoAinLanguageCookieProvider.CookieName}={cookieValue}";
        }

        return provider
            .DetermineProviderCultureResult(context)
            .GetAwaiter()
            .GetResult();
    }

    [Theory]
    [InlineData("bn")]
    [InlineData("en")]
    public void KnownCookieValue_YieldsProviderResult(string lang)
    {
        Assert.NotNull(Resolve(lang));
    }

    [Fact]
    public void BnCookie_MapsToBnBDCulture()
    {
        var result = Resolve("bn")!;

        Assert.Equal("bn-BD", result.Cultures[0].ToString());
        Assert.Equal("bn-BD", result.UICultures[0].ToString());
    }

    [Fact]
    public void EnCookie_MapsToEnCulture()
    {
        var result = Resolve("en")!;

        Assert.Equal("en", result.Cultures[0].ToString());
        Assert.Equal("en", result.UICultures[0].ToString());
    }

    [Fact]
    public void MissingCookie_YieldsNull_SoDefaultCultureApplies()
    {
        Assert.Null(Resolve(null));
    }

    [Fact]
    public void UnknownCookieValue_FallsBackToBnBD()
    {
        // main.js only writes "bn"/"en", but garbage must never crash the pipeline.
        var result = Resolve("fr")!;

        Assert.Equal("bn-BD", result.Cultures[0].ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~MuktoAinLanguageCookieProviderTests"`
Expected: COMPILE FAIL with `type or namespace name 'MuktoAinLanguageCookieProvider' could not be found`.

- [ ] **Step 3: Create the provider**

Create `src/MuktoAin.Web/Localization/MuktoAinLanguageCookieProvider.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~MuktoAinLanguageCookieProviderTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Register localization services in `Program.cs`**

In `src/MuktoAin.Web/Program.cs`, add these usings at the top (merge into the existing using block alphabetically):

```csharp
using Microsoft.AspNetCore.Localization;
using MuktoAin.Web.Localization;
using MuktoAin.Web.Resources;
```

Then, immediately after the `AddSession(...)` block (before the `AppDbContext` registration), add:

```csharp
// S-3.5: server-side localization. The client-side data-bn/data-en toggle in
// main.js REMAINS the primary UI translation mechanism — this only drives
// SERVER-rendered strings (validation errors, identity errors) via .resx.
builder.Services.AddLocalization();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultureNames = new[] { "bn-BD", "en" };

    options.SetDefaultCulture("bn-BD")
        .AddSupportedCultures(supportedCultureNames)
        .AddSupportedUICultures(supportedCultureNames);

    // Replace the defaults (querystring/header/AspNetCore.Culture cookie) with
    // ONLY our provider: the mkt-lang cookie main.js already maintains. No new
    // cookie, no SetLanguage endpoint — one language state, one toggle.
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new MuktoAinLanguageCookieProvider());
});
```

- [ ] **Step 6: Add the middleware to the pipeline**

In the pipeline configuration section, add `UseRequestLocalization()` as the FIRST middleware — immediately after the exception-handler `if/else` block and BEFORE `app.UseStatusCodePagesWithReExecute(...)`:

```csharp
// S-3.5: must run before anything that renders or resolves culture (static
// files, routing, controllers) so CultureInfo.CurrentUICulture is correct
// whenever a server-rendered string is produced.
app.UseRequestLocalization();

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");
```

- [ ] **Step 7: Run the full unit-test suite**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj`
Expected: all existing tests still PASS (wiring only; nothing consumes the localizer yet).

- [ ] **Step 8: Record completion in plans/Dependency_plan.md**

(Deferred to Task 4, Step 4 — a single `[S-3.5]` flip happens once the whole plan is verified.)

---

### Task 2: `IdentityErrorMapper` → `IStringLocalizer` + AccountController wiring

**Files:**
- Modify: `src/MuktoAin.Web/Resources/SharedResource.en.resx`
- Modify: `src/MuktoAin.Web/Resources/SharedResource.bn.resx`
- Modify: `src/MuktoAin.Web/Auth/IdentityErrorMapper.cs` (full rewrite)
- Modify: `src/MuktoAin.Web/Controllers/AccountController.cs` (ctor + 6 string sites)
- Modify: `tests/MuktoAin.UnitTests/Auth/IdentityErrorMapperTests.cs`
- Create: `tests/MuktoAin.UnitTests/Localization/TestStringLocalizer.cs`
- Modify: `tests/MuktoAin.UnitTests/Controllers/AccountControllerTests.cs`

**Interfaces:**
- Consumes: `MuktoAinLanguageCookieProvider` + `UseRequestLocalization` from Task 1 (they set `CultureInfo.CurrentUICulture` per request, which `IStringLocalizer` reads).
- Produces: `IdentityErrorMapper.Map(IdentityError error, IStringLocalizer<SharedResource> localizer)` — new 2-arg signature; `SharedResource` resx keys `IdentityError_*` and `Account_*` used by both controllers and tests.

- [ ] **Step 1: Add the English keys to `SharedResource.en.resx`**

Insert these `<data>` entries immediately before the closing `</root>` tag (values split from the current combined `"bn / en"` strings — English side only):

```xml
  <data name="IdentityError_PasswordTooShort" xml:space="preserve">
    <value>Password must be at least 8 characters.</value>
  </data>
  <data name="IdentityError_PasswordRequiresUpper" xml:space="preserve">
    <value>Password must have at least one uppercase letter (A-Z).</value>
  </data>
  <data name="IdentityError_PasswordRequiresLower" xml:space="preserve">
    <value>Password must have at least one lowercase letter (a-z).</value>
  </data>
  <data name="IdentityError_PasswordRequiresDigit" xml:space="preserve">
    <value>Password must have at least one digit (0-9).</value>
  </data>
  <data name="IdentityError_PasswordRequiresNonAlphanumeric" xml:space="preserve">
    <value>Password must have at least one special character (e.g. @, #, !).</value>
  </data>
  <data name="IdentityError_PasswordRequiresUniqueChars" xml:space="preserve">
    <value>Password must use more unique characters.</value>
  </data>
  <data name="IdentityError_UserAlreadyHasPassword" xml:space="preserve">
    <value>This account already has a password set.</value>
  </data>
  <data name="IdentityError_DuplicateUserName" xml:space="preserve">
    <value>An account with this email is already registered.</value>
  </data>
  <data name="IdentityError_DuplicateEmail" xml:space="preserve">
    <value>An account with this email is already registered.</value>
  </data>
  <data name="IdentityError_InvalidEmail" xml:space="preserve">
    <value>Please enter a valid email address.</value>
  </data>
  <data name="Account_InvalidCredentials" xml:space="preserve">
    <value>The email or password is incorrect.</value>
  </data>
  <data name="Account_Suspended" xml:space="preserve">
    <value>Your account has been suspended. Please contact support.</value>
  </data>
  <data name="Account_Lockout" xml:space="preserve">
    <value>Account temporarily locked due to too many failed attempts. Please try again later.</value>
  </data>
  <data name="Account_BarRegistrationRequired" xml:space="preserve">
    <value>Bar Registration Number is required for lawyer accounts.</value>
  </data>
```

- [ ] **Step 2: Add the Bangla keys to `SharedResource.bn.resx`**

Insert immediately before the closing `</root>` (Bangla side of the same strings, carried over verbatim from `IdentityErrorMapper.cs` and `AccountController.cs`):

```xml
  <data name="IdentityError_PasswordTooShort" xml:space="preserve">
    <value>পাসওয়ার্ড কমপক্ষে ৮ অক্ষরের হতে হবে।</value>
  </data>
  <data name="IdentityError_PasswordRequiresUpper" xml:space="preserve">
    <value>পাসওয়ার্ডে কমপক্ষে একটি বড় হাতের অক্ষর (A-Z) থাকতে হবে।</value>
  </data>
  <data name="IdentityError_PasswordRequiresLower" xml:space="preserve">
    <value>পাসওয়ার্ডে কমপক্ষে একটি ছোট হাতের অক্ষর (a-z) থাকতে হবে।</value>
  </data>
  <data name="IdentityError_PasswordRequiresDigit" xml:space="preserve">
    <value>পাসওয়ার্ডে কমপক্ষে একটি সংখ্যা (0-9) থাকতে হবে।</value>
  </data>
  <data name="IdentityError_PasswordRequiresNonAlphanumeric" xml:space="preserve">
    <value>পাসওয়ার্ডে কমপক্ষে একটি বিশেষ চিহ্ন (যেমন @, #, !) থাকতে হবে।</value>
  </data>
  <data name="IdentityError_PasswordRequiresUniqueChars" xml:space="preserve">
    <value>পাসওয়ার্ডে আরও আলাদা ধরণের অক্ষর ব্যবহার করুন।</value>
  </data>
  <data name="IdentityError_UserAlreadyHasPassword" xml:space="preserve">
    <value>এই একাউন্টে ইতিমধ্যে একটি পাসওয়ার্ড সেট করা আছে।</value>
  </data>
  <data name="IdentityError_DuplicateUserName" xml:space="preserve">
    <value>এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে।</value>
  </data>
  <data name="IdentityError_DuplicateEmail" xml:space="preserve">
    <value>এই ইমেইল দিয়ে ইতিমধ্যে একটি একাউন্ট নিবন্ধিত হয়েছে।</value>
  </data>
  <data name="IdentityError_InvalidEmail" xml:space="preserve">
    <value>সঠিক ইমেইল ঠিকানা দিন।</value>
  </data>
  <data name="Account_InvalidCredentials" xml:space="preserve">
    <value>ইমেইল অথবা পাসওয়ার্ড সঠিক নয়।</value>
  </data>
  <data name="Account_Suspended" xml:space="preserve">
    <value>আপনার একাউন্টটি স্থগিত করা হয়েছে। সহায়তার জন্য যোগাযোগ করুন।</value>
  </data>
  <data name="Account_Lockout" xml:space="preserve">
    <value>অনেকবার ভুল চেষ্টার কারণে একাউন্টটি সাময়িকভাবে লক হয়েছে। কিছুক্ষণ পর আবার চেষ্টা করুন।</value>
  </data>
  <data name="Account_BarRegistrationRequired" xml:space="preserve">
    <value>আইনজীবীদের জন্য বার রেজিস্ট্রেশন সনদ নম্বর আবশ্যক।</value>
  </data>
```

- [ ] **Step 3: Rewrite `IdentityErrorMapper.cs` to use the localizer**

Full replacement content (codes→field mapping preserved; the bilingual message dictionaries become resx keys, and Bangla is carried by the resx instead of C# literals):

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using MuktoAin.Web.Resources;

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
```

- [ ] **Step 4: Wire `AccountController`**

In `src/MuktoAin.Web/Controllers/AccountController.cs`:

1. Add usings (merge alphabetically):

```csharp
using Microsoft.Extensions.Localization;
using MuktoAin.Web.Resources;
```

2. Add the field + constructor parameter (keep the existing parameter order and append the localizer last, so any other construction sites only need one new arg):

```csharp
    private readonly IStringLocalizer<SharedResource> _localizer;

    public AccountController(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IRepository<LawyerProfile> lawyerProfileRepo,
        ILogger<AccountController> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _lawyerProfileRepo = lawyerProfileRepo;
        _logger = logger;
        _localizer = localizer;
    }
```

3. Replace the hardcoded model-level strings (exact lines in the current file):
   - line 51 (wrong email/password, pre-lookup) → `ModelState.AddModelError(string.Empty, _localizer["Account_InvalidCredentials"].Value);`
   - line 57 (suspended) → `ModelState.AddModelError(string.Empty, _localizer["Account_Suspended"].Value);`
   - line 83 (lockout) → `ModelState.AddModelError(string.Empty, _localizer["Account_Lockout"].Value);`
   - line 87 (wrong email/password after sign-in check) → `ModelState.AddModelError(string.Empty, _localizer["Account_InvalidCredentials"].Value);`
   - line 109 (lawyer bar number) → `ModelState.AddModelError("BarRegistrationNumber", _localizer["Account_BarRegistrationRequired"].Value);`
   - lines 130 and 225 (the two `IdentityErrorMapper.Map` calls) → `var (field, message) = IdentityErrorMapper.Map(error, _localizer);` and `var (field, msg) = IdentityErrorMapper.Map(error, _localizer);`

- [ ] **Step 5: Add the shared test localizer helper**

Create `tests/MuktoAin.UnitTests/Localization/TestStringLocalizer.cs`:

```csharp
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using MuktoAin.Web.Resources;

namespace MuktoAin.UnitTests.Localization;

// Real IStringLocalizer<SharedResource> over the Web project's embedded .resx
// (the E-2.8 files), so tests assert actual resource lookups instead of stubs.
// Set CultureInfo.CurrentUICulture in the test to pick the expected language.
public static class TestStringLocalizer
{
    public static IStringLocalizer<SharedResource> Create()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        return factory.Create(typeof(SharedResource));
    }
}
```

- [ ] **Step 6: Update `IdentityErrorMapperTests` for the new signature**

In `tests/MuktoAin.UnitTests/Auth/IdentityErrorMapperTests.cs`:
1. Add usings:

```csharp
using System.Globalization;
using MuktoAin.UnitTests.Localization;
```

2. In the test class, add a ctor that pins the UI culture so assertions are deterministic on every dev machine (Bangla-locale machines included):

```csharp
    public IdentityErrorMapperTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }
```

3. Replace EVERY `IdentityErrorMapper.Map(error)` call with:

```csharp
        var (field, message) =
            IdentityErrorMapper.Map(error, TestStringLocalizer.Create());
```

(The existing assertions — field name, message contains "Password"/"Email", not containing the raw description — remain valid against the English resx values. Add one fallback test:)

```csharp
    [Fact]
    public void Map_UnknownCode_FallsBackToIdentityDescription()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var error = new IdentityError { Code = "SomeUnknownCode", Description = "original" };

        var (field, message) =
            IdentityErrorMapper.Map(error, TestStringLocalizer.Create());

        Assert.Null(field);
        Assert.Equal("original", message);
    }
```

- [ ] **Step 7: Update `AccountControllerTests` construction**

In `tests/MuktoAin.UnitTests/Controllers/AccountControllerTests.cs`, the single `new AccountController(...)` (line 36) gains the new last argument:

```csharp
        _controller = new AccountController(
            signInManagerMock.Object,
            userManagerMock.Object,
            lawyerProfileRepoMock.Object,
            loggerMock.Object,
            TestStringLocalizer.Create());
```

(match the existing local variable names in the file — the point is: append `TestStringLocalizer.Create()` as the 5th argument). Add `using MuktoAin.UnitTests.Localization;` at the top. If any assertion compares against the old combined `"bn / en"` strings, update the expected value to the English `.en.resx` text and add `CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");` to the test's ctor like Step 6.

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~IdentityErrorMapperTests|FullyQualifiedName~AccountControllerTests"`
Expected: PASS.

---

### Task 3: Migrate `RegisterViewModel` validation messages to `.resx`

**Files:**
- Modify: `src/MuktoAin.Web/ViewModels/RegisterViewModel.cs`
- Modify: `src/MuktoAin.Web/Resources/SharedResource.en.resx` (append)
- Modify: `src/MuktoAin.Web/Resources/SharedResource.bn.resx` (append)

**Interfaces:**
- Consumes: `SharedResource` marker + the middleware wiring from Task 1 (RequestLocalization sets `CurrentUICulture` before model binding/validation runs, so DataAnnotations `ErrorMessageResourceType` resolve per request).
- Produces: `Register_*` resx keys. This is the REPRESENTATIVE-SUBSET migration — other ViewModels (LoginViewModel, ProfileViewModels, etc.) intentionally keep their combined `"bn / en"` literals for now (boundary stated in Global Constraints).

- [ ] **Step 1: Add the validation keys to `SharedResource.en.resx`**

Append before `</root>`:

```xml
  <data name="Register_FullName_Required" xml:space="preserve">
    <value>Full Name is required</value>
  </data>
  <data name="Register_FullName_MaxLength" xml:space="preserve">
    <value>Maximum 100 characters</value>
  </data>
  <data name="Register_Email_Required" xml:space="preserve">
    <value>Email is required</value>
  </data>
  <data name="Register_Email_Invalid" xml:space="preserve">
    <value>Please enter a valid email</value>
  </data>
  <data name="Register_Phone_Invalid" xml:space="preserve">
    <value>Please enter a valid Bangladeshi mobile number (e.g. 01XXXXXXXXX)</value>
  </data>
  <data name="Register_Password_Required" xml:space="preserve">
    <value>Password is required</value>
  </data>
  <data name="Register_Password_Length" xml:space="preserve">
    <value>Minimum 8 characters (max 100)</value>
  </data>
  <data name="Register_Password_Policy" xml:space="preserve">
    <value>Password needs a lowercase, an uppercase, a digit and a special character (e.g. @, #, !)</value>
  </data>
  <data name="Register_Password_Mismatch" xml:space="preserve">
    <value>Passwords do not match</value>
  </data>
```

- [ ] **Step 2: Add the same keys to `SharedResource.bn.resx`**

Append before `</root>`:

```xml
  <data name="Register_FullName_Required" xml:space="preserve">
    <value>পূর্ণ নাম প্রয়োজন</value>
  </data>
  <data name="Register_FullName_MaxLength" xml:space="preserve">
    <value>সর্বোচ্চ ১০০ অক্ষর</value>
  </data>
  <data name="Register_Email_Required" xml:space="preserve">
    <value>ইমেইল প্রয়োজন</value>
  </data>
  <data name="Register_Email_Invalid" xml:space="preserve">
    <value>সঠিক ইমেইল দিন</value>
  </data>
  <data name="Register_Phone_Invalid" xml:space="preserve">
    <value>সঠিক বাংলাদেশী মোবাইল নম্বর দিন (যেমন ০১XXXXXXXXX)</value>
  </data>
  <data name="Register_Password_Required" xml:space="preserve">
    <value>পাসওয়ার্ড প্রয়োজন</value>
  </data>
  <data name="Register_Password_Length" xml:space="preserve">
    <value>কমপক্ষে ৮ অক্ষর প্রয়োজন (সর্বোচ্চ ১০০)</value>
  </data>
  <data name="Register_Password_Policy" xml:space="preserve">
    <value>পাসওয়ার্ডে ছোট হাতের অক্ষর, বড় হাতের অক্ষর, সংখ্যা ও বিশেষ চিহ্ন থাকতে হবে (যেমন @, #, !)</value>
  </data>
  <data name="Register_Password_Mismatch" xml:space="preserve">
    <value>পাসওয়ার্ড দুটি মিলছে না</value>
  </data>
```

- [ ] **Step 3: Migrate the attributes in `RegisterViewModel.cs`**

Replace the file content with (Display names and the phone regex are unchanged — only `ErrorMessage` becomes resource lookups):

```csharp
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
    public string? BarRegistrationNumber { get; set; }

    [Display(Name = "বিশেষজ্ঞতা / Specialization (Lawyers only)")]
    public string? Specialization { get; set; }

    [Display(Name = "পছন্দের ভাষা / Preferred Language")]
    public string PreferredLanguage { get; set; } = "bn";
}
```

- [ ] **Step 4: Run the ViewModel + controller tests**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj --filter "FullyQualifiedName~RegisterViewModelTests|FullyQualifiedName~AccountControllerTests"`
Expected: PASS (these tests assert ModelState validity, not message text; if any message-text assertion exists, pin `CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en")` in that test class's ctor and assert the English resx value).

---

### Task 4: Full verification & plan bookkeeping

**Files:**
- Modify: `plans/Dependency_plan.md` (one line)

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: the recorded `[S-3.5]` completion state.

- [ ] **Step 1: Build the full solution**

Run: `dotnet build MuktoAin.sln -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full unit-test suite**

Run: `dotnet test tests/MuktoAin.UnitTests/MuktoAin.UnitTests.csproj -c Release`
Expected: all tests pass (previous count + 6 new: 5 provider + 1 mapper fallback).

- [ ] **Step 3: Manual bilingual smoke (the actual acceptance test)**

1. `dotnet run --project src/MuktoAin.Web` (Development, existing DB).
2. Open `http://localhost:5250/Account/Register` — leave everything blank, submit. Expected: the old combined `"bn / en"` strings are GONE; with the default state (no toggle clicked yet, `bn-BD`) messages render in Bangla only.
3. Click the language toggle's `EN` button (this writes the `mkt-lang=en` cookie via main.js), reload, submit empty again. Expected: the SAME messages now render in English only (server-side, proof the cookie provider works).
4. Toggle back to `বাং` and reload — Bangla returns.
5. Register with a weak password (e.g. `password`) — the identity `PasswordRequires*` error appears next to the password field in the active language.

- [ ] **Step 4: Record completion in plans/Dependency_plan.md**

In `plans/Dependency_plan.md`, Checkpoint 3 section 4, change:

```
- [ ] **[S-3.5]** `RequestLocalizationMiddleware.cs` & Resource Files Wiring — *Shads* `[Blocked by: E-2.8]`
```

to:

```
- [x] ~~**[S-3.5]** `RequestLocalizationMiddleware.cs` & Resource Files Wiring — *Shads* `[Blocked by: E-2.8]`~~ — `UseRequestLocalization` wired in Program.cs (bn-BD default + en; providers replaced with MuktoAinLanguageCookieProvider reading the EXISTING mkt-lang cookie main.js already writes — no new cookie/toggle/endpoint); IdentityErrorMapper + AccountController error messages and RegisterViewModel validation messages migrated to SharedResource.{bn,en}.resx via IStringLocalizer (representative subset — data-bn/data-en client toggle remains the primary UI mechanism per the S-3.5 boundary); verified by new cookie-provider unit tests + full suite passing + manual bilingual smoke on /Account/Register
```

(Also update the H-9 handoff row's "Blocked By" column only if Shads asks — the row refers to delivery, not completion.) Do NOT commit — Shads commits manually per `AGENTS.md` §6.

- [ ] **Step 5: Report**

Summarize to Shads: test count delta, the manual smoke result, and the explicit statement of what was deliberately NOT migrated (remaining ViewModels, view-rendered strings — all still on the data-bn/data-en system).

---

## Self-Review Notes

- Spec coverage: exploration of current localization state done during planning (findings recorded in Architecture: `.resx` exists from E-2.8 but is unwired; toggle already writes `mkt-lang` cookie at `main.js:403`; `IdentityErrorMapper` hardcodes combined strings). RequestLocalizationMiddleware (Task 1), .resx resource files (Tasks 2–3), IStringLocalizer wiring in Program.cs (Task 1), language-switcher integration via cookie (Task 1, no JS changes needed), representative-subset migration of ViewModel validation + AccountController/IdentityErrorMapper strings (Tasks 2–3).
- Type consistency: `IdentityErrorMapper.Map(IdentityError, IStringLocalizer<SharedResource>)` is defined in Task 2 Step 3 and consumed identically in Task 2 Steps 4/6/7. `TestStringLocalizer.Create()` (Task 2 Step 5) is used in Task 2 Steps 6/7.
- Open question for Shads: `AccountController` line 348 loops `result.Errors` straight into ModelState with `e.Description` (raw Identity text) — deliberately left as-is (admin/password-reset path, English-only fallback is the documented mapper behavior); flag if it should also localize in a follow-up.
