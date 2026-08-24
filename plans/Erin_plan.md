# Erin — Frontend Plan (Zero Backend Dependencies)

> ponytail: full — every step earns its place. No boilerplate tasks. Options are real choices.

**Role:** Erin owns the entire `MuktoAin.Web` presentation layer — every view, controller, ViewModel, stylesheet, font, and localization file. She builds the complete frontend with hardcoded mock data. Backend teammates (Shads, Tultul, Arpita) connect their services to her existing views later. Erin has **zero dependencies on anyone**.

**Philosophy:** Build views-first. Controllers return ViewModels populated with realistic fake data. When a backend service is ready, the teammate replaces mock data with a real service call. The views stay the same — only the controller body changes.

---

## Setup: The Mock Data Approach

Every controller Erin writes follows this pattern:

```csharp
public class CaseController : Controller
{
    // CP2: Shads/Arpita will inject real services here
    // private readonly CaseService _caseService;
    // private readonly AiOrchestrationService _aiService;

    [HttpGet]
    public IActionResult Submit()
    {
        var vm = new CaseSubmitViewModel
        {
            Categories = MockData.Categories,   // hardcoded list
            Districts = MockData.Districts,     // hardcoded list
        };
        return View(vm);
    }

    [HttpPost]
    public IActionResult Submit(CaseSubmitViewModel vm)
    {
        // TODO: [Arpita] Replace with CaseService.SubmitCaseAsync()
        TempData["Success"] = "Case submitted successfully! Case ID: MKT-2024-0042";
        return RedirectToAction("Track");
    }
}
```

Create a single `MockData.cs` helper in `MuktoAin.Web/` that holds all fake data in one place:

```csharp
public static class MockData
{
    public static List<SelectListItem> Categories => new()
    {
        new("Labour Complaint", "1"),
        new("General Diary (GD)", "2"),
        new("RTI Request", "3"),
        new("Consumer Complaint", "4"),
    };

    public static List<SelectListItem> Districts => new()
    {
        new("Dhaka", "1"), new("Chittagong", "2"), new("Rajshahi", "3"),
        new("Khulna", "4"), new("Sylhet", "5"), new("Barisal", "6"),
        // ... enough for the UI to look populated
    };

    public static CaseDetailViewModel SampleCase => new()
    {
        CaseId = 42,
        Title = "আমার বেতন ৩ মাস দেয়নি",
        Description = "আমি একটি গার্মেন্টস ফ্যাক্টরিতে কাজ করি। গত ৩ মাস ধরে আমার বেতন দেওয়া হয়নি।",
        CategoryName = "Labour Complaint",
        DistrictName = "Dhaka",
        Status = "Submitted",
        CreatedAt = DateTime.Now.AddDays(-3),
        // ... etc
    };

    // Add more mock objects as needed for each view
}
```

When a backend teammate finishes their service, they:
1. Inject the real service into the controller
2. Replace the mock data return with a real service call
3. The view and ViewModel don't change

---

## Checkpoint 1: Foundation Views

### Step 1.1: _Layout.cshtml — Master Layout

> **Depends on**: Nothing.

The master layout wraps every page. Mobile-first, responsive Bootstrap 5.

1. Create `Views/Shared/_Layout.cshtml`.
2. Structure:
   ```html
   <!DOCTYPE html>
   <html lang="bn">
   <head>
       <meta charset="utf-8" />
       <meta name="viewport" content="width=device-width, initial-scale=1.0" />
       <title>@ViewData["Title"] — MuktoAin | মুক্ত আইন</title>
       <link rel="stylesheet" href="~/lib/bootstrap/dist/css/bootstrap.min.css" />
       <link rel="stylesheet" href="~/css/site.css" />
       <link rel="preconnect" href="https://fonts.googleapis.com" />
       <link href="https://fonts.googleapis.com/css2?family=Noto+Sans+Bengali:wght@400;600;700&display=swap" rel="stylesheet" />
   </head>
   <body>
       @await Html.PartialAsync("_DisclaimerBanner")

       <nav class="navbar navbar-expand-lg navbar-dark bg-dark">
           <div class="container">
               <a class="navbar-brand" href="/">মুক্ত আইন <small class="text-muted">MuktoAin</small></a>
               <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#mainNav">
                   <span class="navbar-toggler-icon"></span>
               </button>
               <div class="collapse navbar-collapse" id="mainNav">
                   <ul class="navbar-nav me-auto">
                       <li class="nav-item"><a class="nav-link" href="/Case/Submit">Submit Case</a></li>
                       <li class="nav-item"><a class="nav-link" href="/Case/Track">Track Case</a></li>
                       <li class="nav-item"><a class="nav-link" href="/Search">Search Acts</a></li>
                       <li class="nav-item"><a class="nav-link" href="/Category">Legal Categories</a></li>
                   </ul>
                   <ul class="navbar-nav">
                       @* TODO: [Shads] Replace with Identity-aware login/logout *@
                       <li class="nav-item"><a class="nav-link" href="/Account/Login">Login</a></li>
                       <li class="nav-item"><a class="nav-link" href="/Account/Register">Register</a></li>
                   </ul>
               </div>
           </div>
       </nav>

       @await Html.PartialAsync("_LanguageToggle")

       <main class="container py-4">
           @RenderBody()
       </main>

       <footer class="bg-dark text-light py-3 mt-5">
           <div class="container text-center">
               <small>MuktoAin (মুক্ত আইন) — AI-Augmented Legal Aid for Bangladesh</small>
           </div>
       </footer>

       <script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
       <script src="~/js/site.js"></script>
       @await RenderSectionAsync("Scripts", required: false)
   </body>
   </html>
   ```

3. **Mobile-first rules**: Shads flagged that the target user is on a cheap Android phone. Keep these in mind for every view:
   - Touch targets: minimum 44×44px
   - No horizontal scrolling
   - Font size: 16px minimum (prevents iOS zoom on input focus)
   - Minimal JavaScript (intermittent 4G)
   - No heavy images or animations

### Step 1.2: _DisclaimerBanner.cshtml — Persistent Legal Disclaimer

> **Depends on**: Nothing.

Surface 1 of 3 in the mandatory disclaimer policy. Non-dismissible, shown on every page.

1. Create `Views/Shared/_DisclaimerBanner.cshtml`:
   ```html
   <div class="disclaimer-banner" role="alert">
       <div class="container">
           <small>
               ⚠️ <strong>মুক্ত আইন</strong> সাধারণ আইনি তথ্য প্রদান করে। এটি আনুষ্ঠানিক আইনি পরামর্শ নয়।
               প্রতিটি নথি ব্যবহারের পূর্বে একজন যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা করা আবশ্যক।
               <br />
               ⚠️ <strong>MuktoAin</strong> provides general legal information. This is NOT formal legal advice.
               Every document must be reviewed by a verified lawyer before use.
           </small>
       </div>
   </div>
   ```

2. CSS in `site.css`:
   ```css
   .disclaimer-banner {
       background-color: #fff3cd;
       border-bottom: 2px solid #ffc107;
       padding: 8px 0;
       position: sticky;
       top: 0;
       z-index: 1030; /* above navbar */
   }
   ```

3. **Non-dismissible** — no close button, no `display: none`, no JavaScript to hide it. It must be visible on every page, always. This is a project requirement.

### Step 1.3: _LanguageToggle.cshtml

> **Depends on**: Nothing.

1. Create `Views/Shared/_LanguageToggle.cshtml`:
   ```html
   <div class="language-toggle bg-light border-bottom py-1">
       <div class="container text-end">
           <div class="btn-group btn-group-sm" role="group" aria-label="Language">
               <button type="button" class="btn btn-outline-secondary active" data-lang="bn">বাংলা</button>
               <button type="button" class="btn btn-outline-secondary" data-lang="en">English</button>
           </div>
       </div>
   </div>
   ```

2. Wire it in `site.js`:
   ```javascript
   document.querySelectorAll('[data-lang]').forEach(btn => {
       btn.addEventListener('click', () => {
           document.querySelectorAll('[data-lang]').forEach(b => b.classList.remove('active'));
           btn.classList.add('active');
           document.documentElement.lang = btn.dataset.lang;
           // TODO: [Shads] Persist preference via cookie/API
           // For now, just toggle a CSS class that swaps visible text
           document.querySelectorAll('[data-bn], [data-en]').forEach(el => {
               el.style.display = el.dataset[btn.dataset.lang] !== undefined ? '' : 'none';
           });
       });
   });
   ```
   - ponytail: The toggle is a client-side CSS class swap. Don't build a full i18n framework. Backend teammates will wire `.resx` resource files for server-side localization later. Ceiling: client-side toggle; upgrade path: server-side culture switching via `RequestLocalizationMiddleware`.

### Step 1.4: wwwroot — CSS, JS, Fonts

> **Depends on**: Nothing.

1. **`wwwroot/css/site.css`** — global styles:
   ```css
   /* Base */
   body {
       font-family: 'Noto Sans Bengali', sans-serif;
       font-size: 16px; /* prevents iOS zoom */
   }

   /* Mobile-first responsive utilities */
   .card { margin-bottom: 1rem; }

   /* Case status badges */
   .badge-submitted { background-color: #ffc107; color: #000; }
   .badge-underreview { background-color: #17a2b8; color: #fff; }
   .badge-finalized { background-color: #28a745; color: #fff; }
   .badge-rejected { background-color: #dc3545; color: #fff; }

   /* Disclaimer styles */
   .disclaimer-banner {
       background-color: #fff3cd;
       border-bottom: 2px solid #ffc107;
       padding: 8px 0;
       position: sticky;
       top: 0;
       z-index: 1030;
   }

   /* Document preview */
   .document-preview {
       white-space: pre-wrap;
       font-family: 'Noto Sans Bengali', monospace;
       background: #f8f9fa;
       border: 1px solid #dee2e6;
       padding: 1.5rem;
       border-radius: 4px;
       line-height: 1.8;
   }

   /* Redline diff (lawyer view) */
   .content-draft { background-color: #fff3f3; }
   .content-final { background-color: #f3fff3; }
   ```

2. **`wwwroot/js/site.js`** — vanilla JS utilities:
   ```javascript
   // Language toggle (from Step 1.3)
   // Form validation helpers
   // Fetch wrapper for AJAX calls (if needed)
   // Character counter for description textarea
   ```

3. **`wwwroot/fonts/`** — Bangla font:
   - Download Noto Sans Bengali from Google Fonts
   - Place `NotoSansBengali-Regular.ttf` and `NotoSansBengali-Bold.ttf` here
   - This is also used by Arpita's QuestPDF service for PDF rendering
   - **Option A**: Load from Google Fonts CDN (simpler, requires internet).
   - **Option B**: Self-host the font files (works offline, consistent).
   - **Recommendation**: Both — CDN as primary in `_Layout.cshtml` `<link>`, self-hosted as fallback via `@font-face` in `site.css`. Target users may have intermittent connectivity, so the self-hosted fallback matters.

### Step 1.5: Home — Index.cshtml

> **Depends on**: Steps 1.1-1.4 (layout and styles must exist).

1. Create `Views/Home/Index.cshtml`:
   ```html
   @{ ViewData["Title"] = "Home"; }

   <div class="text-center py-5">
       <h1>মুক্ত আইন <small class="text-muted">MuktoAin</small></h1>
       <p class="lead">AI-Augmented Legal Aid for Bangladesh</p>
       <p class="text-muted">
           আপনার আইনি সমস্যা বর্ণনা করুন — আমরা প্রাসঙ্গিক আইন খুঁজে বের করব,
           আপনার অধিকার ব্যাখ্যা করব, এবং একটি আইনি নথি প্রস্তুত করব।
       </p>
       <div class="d-grid gap-2 d-md-flex justify-content-md-center mt-4">
           <a href="/Case/Submit" class="btn btn-primary btn-lg px-4">Submit a Case</a>
           <a href="/Search" class="btn btn-outline-secondary btn-lg px-4">Search Laws</a>
       </div>
   </div>

   <div class="row mt-5">
       <div class="col-md-4">
           <div class="card h-100">
               <div class="card-body text-center">
                   <h5 class="card-title">📝 Describe Your Problem</h5>
                   <p class="card-text">Tell us what happened in Bangla, English, or Banglish. No legal knowledge needed.</p>
               </div>
           </div>
       </div>
       <div class="col-md-4">
           <div class="card h-100">
               <div class="card-body text-center">
                   <h5 class="card-title">⚖️ Know Your Rights</h5>
                   <p class="card-text">We find the relevant laws and explain your rights in plain language.</p>
               </div>
           </div>
       </div>
       <div class="col-md-4">
           <div class="card h-100">
               <div class="card-body text-center">
                   <h5 class="card-title">👨‍⚖️ Lawyer-Reviewed Documents</h5>
                   <p class="card-text">Get a drafted legal document reviewed by a verified lawyer before you use it.</p>
               </div>
           </div>
       </div>
   </div>
   ```

2. Create `HomeController.cs`:
   ```csharp
   public class HomeController : Controller
   {
       public IActionResult Index() => View();
   }
   ```

### Step 1.6: Account Views — Login + Register

> **Depends on**: Step 1.1 (layout).

1. Create `Views/Account/Login.cshtml`:
   ```html
   @model LoginViewModel
   @{ ViewData["Title"] = "Login"; }

   <div class="row justify-content-center">
       <div class="col-md-5">
           <h2>Login</h2>
           <form method="post" asp-action="Login">
               <div class="mb-3">
                   <label asp-for="Email" class="form-label">Email</label>
                   <input asp-for="Email" class="form-control" type="email" required />
               </div>
               <div class="mb-3">
                   <label asp-for="Password" class="form-label">Password</label>
                   <input asp-for="Password" class="form-control" type="password" required />
               </div>
               <button type="submit" class="btn btn-primary w-100">Login</button>
           </form>
           <p class="mt-3 text-center">Don't have an account? <a href="/Account/Register">Register</a></p>
       </div>
   </div>
   ```

2. Create `Views/Account/Register.cshtml`:
   ```html
   @model RegisterViewModel
   @{ ViewData["Title"] = "Register"; }

   <div class="row justify-content-center">
       <div class="col-md-6">
           <h2>Register</h2>
           <form method="post" asp-action="Register">
               <div class="mb-3">
                   <label asp-for="FullName" class="form-label">Full Name / পূর্ণ নাম</label>
                   <input asp-for="FullName" class="form-control" required />
               </div>
               <div class="mb-3">
                   <label asp-for="Email" class="form-label">Email</label>
                   <input asp-for="Email" class="form-control" type="email" required />
               </div>
               <div class="mb-3">
                   <label asp-for="PhoneNumber" class="form-label">Phone (Optional)</label>
                   <input asp-for="PhoneNumber" class="form-control" type="tel" />
               </div>
               <div class="mb-3">
                   <label asp-for="Password" class="form-label">Password</label>
                   <input asp-for="Password" class="form-control" type="password" required />
               </div>
               <div class="mb-3">
                   <label asp-for="Role" class="form-label">I am a...</label>
                   <select asp-for="Role" class="form-select">
                       <option value="Citizen">Citizen / নাগরিক</option>
                       <option value="Lawyer">Lawyer / আইনজীবী</option>
                   </select>
               </div>
               <div class="mb-3">
                   <label asp-for="PreferredLanguage" class="form-label">Preferred Language</label>
                   <select asp-for="PreferredLanguage" class="form-select">
                       <option value="bn">বাংলা</option>
                       <option value="en">English</option>
                   </select>
               </div>
               <button type="submit" class="btn btn-primary w-100">Register</button>
           </form>
       </div>
   </div>
   ```

3. **ViewModels** — create in `ViewModels/`:
   ```csharp
   public class LoginViewModel
   {
       public string Email { get; set; } = "";
       public string Password { get; set; } = "";
   }

   public class RegisterViewModel
   {
       public string FullName { get; set; } = "";
       public string Email { get; set; } = "";
       public string? PhoneNumber { get; set; }
       public string Password { get; set; } = "";
       public string Role { get; set; } = "Citizen";
       public string PreferredLanguage { get; set; } = "bn";
   }
   ```

4. Create `AccountController.cs` with mock behavior:
   ```csharp
   public class AccountController : Controller
   {
       // TODO: [Shads] Replace with SignInManager/UserManager
       [HttpGet] public IActionResult Login() => View();
       [HttpPost] public IActionResult Login(LoginViewModel vm)
       {
           TempData["Success"] = "Logged in successfully (mock)";
           return RedirectToAction("Index", "Home");
       }
       [HttpGet] public IActionResult Register() => View();
       [HttpPost] public IActionResult Register(RegisterViewModel vm)
       {
           TempData["Success"] = "Registered successfully (mock)";
           return RedirectToAction("Login");
       }
   }
   ```

---

## Checkpoint 2: Core Flow Views

### Step 2.1: Case Submit View (FR-2)

> **Depends on**: Nothing (mock data for categories and districts).

The citizen intake form — the entry point of the entire platform.

1. Create `ViewModels/CaseSubmitViewModel.cs`:
   ```csharp
   public class CaseSubmitViewModel
   {
       public int CategoryId { get; set; }
       public byte DistrictId { get; set; }
       public string Title { get; set; } = "";
       public string Description { get; set; } = "";
       public string Language { get; set; } = "bn";
       public bool IsAnonymous { get; set; }
       public List<SelectListItem> Categories { get; set; } = new();
       public List<SelectListItem> Districts { get; set; } = new();
   }
   ```

2. Create `Views/Case/Submit.cshtml`:
   ```html
   @model CaseSubmitViewModel
   @{ ViewData["Title"] = "Submit a Case"; }

   <h2>Submit Your Legal Problem / আপনার আইনি সমস্যা জমা দিন</h2>

   <form method="post" asp-action="Submit">
       <div class="mb-3">
           <label asp-for="CategoryId" class="form-label">Legal Category / আইনি বিভাগ</label>
           <select asp-for="CategoryId" asp-items="Model.Categories" class="form-select">
               <option value="">— Select —</option>
           </select>
       </div>

       <div class="mb-3">
           <label asp-for="DistrictId" class="form-label">District / জেলা</label>
           <select asp-for="DistrictId" asp-items="Model.Districts" class="form-select">
               <option value="">— Select —</option>
           </select>
       </div>

       <div class="mb-3">
           <label asp-for="Title" class="form-label">Brief Title / সংক্ষিপ্ত শিরোনাম</label>
           <input asp-for="Title" class="form-control" maxlength="250"
                  placeholder="e.g., আমার বেতন দেয়নি ৩ মাস" />
       </div>

       <div class="mb-3">
           <label asp-for="Description" class="form-label">
               Describe Your Problem / আপনার সমস্যা বর্ণনা করুন
           </label>
           <textarea asp-for="Description" class="form-control" rows="6"
                     placeholder="Write in Bangla, English, or Banglish — whatever is comfortable for you"></textarea>
           <div class="form-text">
               <span id="charCount">0</span> / 5000 characters
           </div>
       </div>

       <div class="mb-3 form-check">
           <input asp-for="IsAnonymous" class="form-check-input" type="checkbox" />
           <label asp-for="IsAnonymous" class="form-check-label">
               Submit anonymously / বেনামে জমা দিন
           </label>
       </div>

       <button type="submit" class="btn btn-primary btn-lg">Submit Case</button>
   </form>

   @section Scripts {
       <script>
           const desc = document.getElementById('Description');
           const counter = document.getElementById('charCount');
           desc.addEventListener('input', () => counter.textContent = desc.value.length);
       </script>
   }
   ```

3. Create `CaseController.cs`:
   ```csharp
   public class CaseController : Controller
   {
       [HttpGet]
       public IActionResult Submit()
       {
           return View(new CaseSubmitViewModel
           {
               Categories = MockData.Categories,
               Districts = MockData.Districts,
           });
       }

       [HttpPost]
       public IActionResult Submit(CaseSubmitViewModel vm)
       {
           // TODO: [Arpita] Replace with CaseService.SubmitCaseAsync()
           // TODO: [Shads] Wire AiOrchestrationService for rights explanation
           return RedirectToAction("Result", new { id = 42 });
       }

       [HttpGet]
       public IActionResult Result(int id)
       {
           return View(MockData.SampleCaseResult);
       }
   }
   ```

### Step 2.2: Case Result View — Rights Explanation + Document Preview

> **Depends on**: Step 2.1 (CaseController).

After submission, the citizen sees their rights explanation and draft document.

1. Create `ViewModels/CaseResultViewModel.cs`:
   ```csharp
   public class CaseResultViewModel
   {
       public int CaseId { get; set; }
       public string Title { get; set; } = "";
       public string Status { get; set; } = "Submitted";

       // Rights explanation
       public string RightsExplanation { get; set; } = "";
       public List<CitedSectionViewModel> CitedSections { get; set; } = new();

       // Document draft
       public int? DocumentId { get; set; }
       public string? DocumentContent { get; set; }
       public string? DocumentStatus { get; set; }
       public bool CanDownloadPdf { get; set; }
   }

   public class CitedSectionViewModel
   {
       public string ActTitle { get; set; } = "";
       public string SectionNumber { get; set; } = "";
       public string SectionText { get; set; } = "";
       public string RelevanceScore { get; set; } = "";
   }
   ```

2. Create `Views/Case/Result.cshtml`:
   ```html
   @model CaseResultViewModel
   @{ ViewData["Title"] = "Case Result"; }

   <div class="d-flex justify-content-between align-items-center mb-4">
       <h2>Case #@Model.CaseId</h2>
       <span class="badge badge-@Model.Status.ToLower() fs-6">@Model.Status</span>
   </div>

    <!-- Rights Explanation Section -->
    <div class="card mb-4">
        <div class="card-header"><h5>⚖️ Your Rights / আপনার অধিকার</h5></div>
        <div class="card-body">
            @* SECURITY (eng review): do NOT use Html.Raw on AI output — the explanation
               echoes citizen free text, so raw rendering is an XSS vector if a prompt
               injection makes Gemini emit HTML/script. Encode it like the document preview: *@
            <div class="rights-explanation document-preview">@Model.RightsExplanation</div>
        </div>
    </div>

   <!-- Cited Sections -->
   <div class="card mb-4">
       <div class="card-header"><h5>📜 Cited Legal Provisions</h5></div>
       <div class="card-body">
           @foreach (var section in Model.CitedSections)
           {
               <div class="border-bottom pb-3 mb-3">
                   <strong>@section.ActTitle — Section @section.SectionNumber</strong>
                   <span class="badge bg-info ms-2">@section.RelevanceScore</span>
                   <p class="mt-2 text-muted">@section.SectionText</p>
               </div>
           }
       </div>
   </div>

   <!-- Document Preview -->
   @if (Model.DocumentContent != null)
   {
       <div class="card mb-4">
           <div class="card-header d-flex justify-content-between">
               <h5>📄 Generated Document</h5>
               <span class="badge badge-@Model.DocumentStatus?.ToLower()">@Model.DocumentStatus</span>
           </div>
           <div class="card-body">
               <div class="document-preview">@Model.DocumentContent</div>
           </div>
           <div class="card-footer">
               @if (Model.CanDownloadPdf)
               {
                   <a href="/Document/Download/@Model.DocumentId" class="btn btn-success">
                       📥 Download PDF
                   </a>
               }
               else
               {
                   <div class="alert alert-warning mb-0">
                       PDF download is available only after a verified lawyer approves this document.
                   </div>
               }
           </div>
       </div>
   }
   ```

3. Add mock data for the result view in `MockData.cs`:
   ```csharp
   public static CaseResultViewModel SampleCaseResult => new()
   {
       CaseId = 42,
       Title = "আমার বেতন ৩ মাস দেয়নি",
       Status = "Submitted",
       RightsExplanation = "বাংলাদেশ শ্রম আইন ২০০৬ অনুযায়ী, আপনার নিয়োগকর্তা প্রতি মাসের ৭ কার্যদিবসের মধ্যে আপনার মজুরি পরিশোধ করতে বাধ্য (ধারা ১২৩)। ৩ মাস বেতন না দেওয়া একটি শাস্তিযোগ্য অপরাধ...",
       CitedSections = new()
       {
           new() { ActTitle = "Bangladesh Labour Act, 2006", SectionNumber = "123", SectionText = "Payment of wages: Every employer shall pay wages...", RelevanceScore = "0.94" },
           new() { ActTitle = "Bangladesh Labour Act, 2006", SectionNumber = "124", SectionText = "Deductions which may be made from wages...", RelevanceScore = "0.87" },
       },
       DocumentId = 1,
       DocumentContent = "TO\nThe Inspector General / District Labour Court\nDhaka, Bangladesh\n\nSubject: Complaint Under Section 123 of the Bangladesh Labour Act, 2006\n\n...",
       DocumentStatus = "Draft",
       CanDownloadPdf = false,
   };
   ```

### Step 2.3: Case Tracking Dashboard (FR-8)

> **Depends on**: Nothing (mock data).

1. Create `ViewModels/CaseTrackViewModel.cs`:
   ```csharp
   public class CaseTrackViewModel
   {
       public List<CaseListItemViewModel> Cases { get; set; } = new();
   }

   public class CaseListItemViewModel
   {
       public int CaseId { get; set; }
       public string Title { get; set; } = "";
       public string CategoryName { get; set; } = "";
       public string Status { get; set; } = "";
       public DateTime CreatedAt { get; set; }
   }
   ```

2. Create `Views/Case/Track.cshtml` — a table/card list of the citizen's cases with status badges and links to the result view.

3. Add mock list of 3-4 cases in different statuses (Submitted, UnderReview, Finalized) so the UI demonstrates all states.

### Step 2.4: Search Views (FR-7)

> **Depends on**: Nothing.

1. Create `ViewModels/SearchViewModel.cs`:
   ```csharp
   public class SearchViewModel
   {
       public string Query { get; set; } = "";
       public int Page { get; set; } = 1;
       public int TotalResults { get; set; }
       public List<SearchResultItemViewModel> Results { get; set; } = new();
   }

   public class SearchResultItemViewModel
   {
       public string ActTitle { get; set; } = "";
       public string SectionNumber { get; set; } = "";
       public string SectionTextSnippet { get; set; } = "";  // first 200 chars
   }
   ```

2. Create `Views/Search/Index.cshtml` — search box + results list with highlighted snippets and pagination.

3. Create `SearchController.cs`:
   ```csharp
   public class SearchController : Controller
   {
       [HttpGet]
       public IActionResult Index(string? q, int page = 1)
       {
           if (string.IsNullOrEmpty(q)) return View(new SearchViewModel());

           // TODO: [Tultul] Replace with SearchService.SearchActsAsync()
           return View(MockData.SampleSearchResults(q, page));
       }
   }
   ```

### Step 2.5: Category Views (FR-6)

> **Depends on**: Nothing.

1. Create `Views/Category/Index.cshtml` — card grid of 4 legal categories with descriptions.
2. Create `Views/Category/Details.cshtml` — single category with description + "Submit a case in this category" CTA.
3. Create `CategoryController.cs` with mock data.

### Step 2.6: Document Views — Preview + Download

> **Depends on**: Step 2.2 (integrated into the Case Result view, but also needs standalone views).

1. Create `Views/Document/Preview.cshtml` — standalone document preview page (the same `document-preview` div from the result view but full-page).
2. Create `DocumentController.cs`:
   ```csharp
   public class DocumentController : Controller
   {
       [HttpGet]
       public IActionResult Preview(int id)
       {
           // TODO: [Arpita] Replace with DocumentService.GetDocumentAsync()
           return View(MockData.SampleDocument);
       }

       [HttpGet]
       public IActionResult Download(int id)
       {
           // TODO: [Arpita] Replace with PdfExportService.GeneratePdf()
           // Mock: return a placeholder message
           TempData["Error"] = "PDF download requires lawyer approval.";
           return RedirectToAction("Preview", new { id });
       }
   }
   ```

### Step 2.7: Lawyer Views — Verification + Review Queue + Review Detail

> **Depends on**: Nothing.

**Lawyer Verification Application (FR-15):**

1. Create `Views/Lawyer/Apply.cshtml`:
   ```html
   @model LawyerApplyViewModel
   @{ ViewData["Title"] = "Lawyer Verification"; }

   <h2>Apply for Verification / আইনজীবী যাচাইকরণ আবেদন</h2>
   <p class="text-muted">Submit your bar registration details for admin verification.</p>

   <form method="post">
       <div class="mb-3">
           <label asp-for="BarRegistrationNumber" class="form-label">
               Bar Registration Number / বার রেজিস্ট্রেশন নম্বর
           </label>
           <input asp-for="BarRegistrationNumber" class="form-control" required />
       </div>
       <div class="mb-3">
           <label asp-for="Specialization" class="form-label">Specialization (Optional)</label>
           <input asp-for="Specialization" class="form-control"
                  placeholder="e.g., Labour Law, Family Law" />
       </div>
       <button type="submit" class="btn btn-primary">Submit Application</button>
   </form>
   ```

**Review Queue (FR-13):**

2. Create `Views/Lawyer/Queue.cshtml` — table of documents awaiting review. Each row shows: Case ID, Category, Date, Status, "Review" button.

3. Create mock data: 3-4 documents in `Draft` or `UnderReview` status.

**Review Detail (FR-14):**

4. Create `Views/Lawyer/Review.cshtml`:
   ```html
   @model LawyerReviewViewModel
   @{ ViewData["Title"] = "Review Document"; }

   <h2>Review Document #@Model.DocumentId</h2>

   <!-- Original AI Draft (read-only) -->
   <div class="card mb-4">
       <div class="card-header">📄 AI-Generated Draft (Original)</div>
       <div class="card-body">
           <div class="document-preview content-draft">@Model.ContentDraft</div>
       </div>
   </div>

   <!-- Review Form -->
   <form method="post" asp-action="SubmitReview">
       <input type="hidden" asp-for="DocumentId" />

       <div class="mb-3">
           <label asp-for="Decision" class="form-label">Decision</label>
           <select asp-for="Decision" class="form-select" id="decisionSelect">
               <option value="Approved">✅ Approve (no changes)</option>
               <option value="EditedApproved">✏️ Approve with edits</option>
               <option value="Rejected">❌ Reject</option>
           </select>
       </div>

       <!-- Editable content (shown only for EditedApproved) -->
       <div class="mb-3" id="editSection" style="display:none;">
           <label asp-for="EditedContent" class="form-label">Edited Document</label>
           <textarea asp-for="EditedContent" class="form-control" rows="12">@Model.ContentDraft</textarea>
       </div>

       <div class="mb-3">
           <label asp-for="Comments" class="form-label">Review Comments (Required)</label>
           <textarea asp-for="Comments" class="form-control" rows="3" required></textarea>
       </div>

       <button type="submit" class="btn btn-primary btn-lg">Submit Review</button>
   </form>

   @section Scripts {
       <script>
           document.getElementById('decisionSelect').addEventListener('change', function() {
               document.getElementById('editSection').style.display =
                   this.value === 'EditedApproved' ? '' : 'none';
           });
       </script>
   }
   ```

5. Create `LawyerController.cs` + `ReviewController.cs` with mock data.

### Step 2.8: .resx Localization Resource Files

> **Depends on**: Nothing.

1. Create `Resources/SharedResource.bn.resx` and `Resources/SharedResource.en.resx`.
2. Add key-value pairs for all UI labels used across views:
   ```
   Key: "SubmitCase"    bn: "মামলা জমা দিন"     en: "Submit a Case"
   Key: "TrackCase"     bn: "মামলা ট্র্যাক করুন"  en: "Track Case"
   Key: "SearchLaws"    bn: "আইন অনুসন্ধান"      en: "Search Laws"
   Key: "Login"         bn: "লগইন"               en: "Login"
   Key: "Register"      bn: "নিবন্ধন"             en: "Register"
   Key: "Download"      bn: "ডাউনলোড"            en: "Download"
   // ... etc for all visible text
   ```
3. Create `Resources/SharedResource.cs` (empty class — ASP.NET uses it as a localization key):
   ```csharp
   public class SharedResource { }
   ```

- ponytail: Create the .resx files with all keys now. Don't wire `IStringLocalizer<SharedResource>` injection into every view yet — that's integration work that happens when Shads sets up `RequestLocalizationMiddleware`. The files are ready for him to plug in. Ceiling: static .resx files; upgrade path: inject `IStringLocalizer` and replace hardcoded text with `@Localizer["Key"]`.

---

## Checkpoint 3: Admin Views + Final Polish

### Step 3.1: Admin Views

> **Depends on**: Nothing (mock data).

1. **`Views/Admin/Dashboard.cshtml`** — analytics dashboard showing:
   - Total cases, pending reviews, approved documents (big number cards)
   - Cases by category (bar chart or table)
   - Cases by district (table, or a simple heat-list — top 10 districts)
   - Use mock numbers

2. **`Views/Admin/Users.cshtml`** — user management table (name, email, role, status, actions)

3. **`Views/Admin/Lawyers.cshtml`** — lawyer verification queue (bar number, status, approve/reject buttons)

4. **`Views/Admin/Acts.cshtml`** — Acts corpus management (list of Acts, last imported date, re-index button)

5. **`Views/Admin/ScenarioMappings.cshtml`** — Scenario mapping management (FR-18). Table showing keyword, linked Act Section, notes. Add/delete buttons. Tultul's `ScenarioMappingService` (Step 3.2 of Tultul_plan.md) provides the backend.
   - Show: MappingId, ScenarioKeyword, linked ActTitle + SectionNumber, Notes
   - Actions: Add new mapping (form with SectionId, keyword, notes) + Delete button per row
   - Use mock data with 3-4 sample mappings

6. Create `AdminController.cs`:
   ```csharp
   [Authorize(Roles = "Admin")]  // TODO: [Shads] Enable after Identity is wired
   public class AdminController : Controller
   {
       public IActionResult Dashboard() => View(MockData.SampleAnalytics);
       public IActionResult Users() => View(MockData.SampleUsers);
       public IActionResult Lawyers() => View(MockData.SampleLawyers);
       public IActionResult Acts() => View(MockData.SampleActs);
       public IActionResult ScenarioMappings() => View(MockData.SampleMappings);
       // TODO: [Tultul] Wire ScenarioMappingService for Add/Delete actions
   }
   ```
   - Note: The `[Authorize]` attribute won't work until Shads wires Identity. It's there as a marker so he knows to enable it.

### Step 3.2: docs/api-contracts.md

> **Depends on**: Nothing.

Document the implicit API contracts — what each controller expects and returns:

```markdown
# API Contracts (Controller → Service)

## CaseController
- POST /Case/Submit → expects CaseSubmitViewModel → calls CaseService.SubmitCaseAsync()
  - Anonymous submissions return an **AnonymousTrackingCode** — the Track page accepts it as credentials for that one case (FR-8).
- GET /Case/Result/{id} → calls CaseService.GetCaseDetailAsync() + AiOrchestrationService
  - NOTE (eng review): rights explanation is CACHED in AI_LOG — repeat views must NOT re-call Gemini. Controller reads stored response first.
- GET /Case/Track → calls CaseService.GetUserCasesAsync()

## SearchController
- GET /Search?q={query}&page={n} → calls SearchService.SearchActsAsync()

## DocumentController
- GET /Document/Preview/{id} → calls DocumentService.GetDocumentAsync()
- GET /Document/Download/{id} → calls PdfExportService.GeneratePdf()

## LawyerController
- GET /Lawyer/Apply → view only
- POST /Lawyer/Apply → calls LawyerVerificationService.ApplyAsync()

## ReviewController
- GET /Review/Queue → calls LawyerReviewService.GetReviewQueueAsync()
- GET /Review/{id} → calls LawyerReviewService.GetDocumentForReview()
- POST /Review/Submit → calls LawyerReviewService.SubmitReviewAsync()

## AdminController
- GET /Admin/Dashboard → calls AdminAnalyticsService.GetSummaryAsync()
- GET /Admin/Users → calls UserManagementService.GetAllUsersAsync()
- POST /Admin/Users/{id}/Suspend → calls UserManagementService.SetAccountStatusAsync()
- GET /Admin/Lawyers → calls LawyerVerificationService.GetPendingApplicationsAsync()
- GET /Admin/Acts → calls ActsManagementService
- GET /Admin/ScenarioMappings → calls ScenarioMappingService.GetAllAsync()
- POST /Admin/ScenarioMappings/Add → calls ScenarioMappingService.AddMappingAsync()
- POST /Admin/ScenarioMappings/Delete/{id} → calls ScenarioMappingService.DeleteMappingAsync()
```

This document is the handoff spec. Each backend teammate reads their section and knows exactly which controller action to modify and which service to inject.

---

## Dependency Map

| Erin's Task | Blocked By | Teammate |
|---|---|---|
| — | — | — |

**Erin has zero external dependencies.** Every view uses mock data. Every controller returns hardcoded ViewModels.

### How Backend Teammates Connect to Erin's Views

When a backend teammate finishes their service, they:

1. Open the relevant controller (e.g., `CaseController.cs`)
2. Add their service to the constructor via DI
3. Replace the `MockData.xxx` return with a real service call
4. The view and ViewModel stay the same

**Example — Arpita connects CaseService:**
```csharp
// BEFORE (Erin's mock)
[HttpPost]
public IActionResult Submit(CaseSubmitViewModel vm)
{
    TempData["Success"] = "Case submitted (mock)";
    return RedirectToAction("Track");
}

// AFTER (Arpita wires real service)
[HttpPost]
public async Task<IActionResult> Submit(CaseSubmitViewModel vm)
{
    var caseId = await _caseService.SubmitCaseAsync(new CaseSubmissionDto(...));
    return RedirectToAction("Result", new { id = caseId });
}
```

The view doesn't change. The ViewModel doesn't change. Only the controller body changes.

### What Erin Delivers

When Erin is done, the app has:
- ✅ Every page navigable with realistic mock data
- ✅ Full responsive layout working on mobile
- ✅ Disclaimer banner on every page (Surface 1/3)
- ✅ Language toggle functional (client-side)
- ✅ All forms submitting (to mock endpoints)
- ✅ All ViewModels defined
- ✅ All controllers stubbed with TODO comments naming the teammate who wires each action
- ✅ .resx files ready for server-side localization
- ✅ api-contracts.md documenting every controller → service mapping

Backend teammates never need to create a view from scratch. They only modify controller method bodies.
