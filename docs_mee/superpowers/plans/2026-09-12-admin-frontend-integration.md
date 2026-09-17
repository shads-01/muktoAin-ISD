# Admin Frontend & Final Integration (Erin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Erin's Checkpoint 3 frontend tasks — E-3.2 (Admin Acts Management & Scenario views at `/Admin/Acts` wired to Hrittika's T-3.1/T-3.2 services), E-3.3 (Admin User Management views verified against the real `UserManagementService`), and E-3.4 (final integration: every remaining controller/view off mock data).

**Architecture:** Extend the existing `[Authorize(Roles="Admin")]` `AdminController` with Acts management actions (`Acts`, `ActDetail`, `EditAct`) that consume `IActsManagementService`, rewire the existing `Scenarios`/`AddScenario`/`DeleteScenario` actions from raw repositories to `IScenarioMappingService`, and replace the mock-era hardcoded KPIs in `Views/Admin/Analytics.cshtml` with the already-computed real `AdminDashboardViewModel` fields. All views follow the established conventions: Bootstrap-free custom CSS classes (`card`, `chip`, `badge-*`, `acc`), `data-bn`/`data-en` bilingual attribute pairs swapped by `main.js`, `TempData["Success"/"Error"]` + matching `*En` keys surfaced as toasts by `_Layout.cshtml`.

**Tech Stack:** ASP.NET Core MVC (.NET 8), Razor Views, xUnit + Moq (test project already references `Moq 4.20.72`), EF Core InMemory for controller tests (`TestDbContextFactory`), no new NuGet packages.

**Spec:** `.agent/spec/requirements.md` (FR-17 corpus management, FR-18 scenario mappings, FR-16 analytics; `.agent/spec/design.md` §2 for schema boundaries)

## Global Constraints

- **Bootstrap 5 + vanilla JS only.** No SPA frameworks (React/Vue/Angular), no new JS libraries. The Admin pages use the site's own custom CSS (`card`, `badge`, `chip`, `acc`, `input`, `btn` classes from `main.css`) — match them exactly; do not add Bootstrap grid classes to these views.
- **Bilingual on every new string.** Every user-visible string added in a Razor view carries `data-bn="..." data-en="..."` (Bangla as the default text content). Attribute pairs are swapped by the generic `document.querySelectorAll("[data-bn][data-en]")` sweep in `main.js` (~line 432) — no per-page JS needed.
- **Toasts, not inline messages.** Controller feedback uses `TempData["Error"]`/`TempData["ErrorEn"]` and `TempData["Success"]`/`TempData["SuccessEn"]`; `_Layout.cshtml` (~line 290) renders them as toasts. Never set only the Bangla key.
- **Anti-fabrication rule:** never render a number the controller did not compute. Where the backend does not track a metric (e.g., "avg lawyer review time", "PDF downloads"), that KPI card is **removed or replaced with a real metric** — no hardcoded stand-ins.
- **No git commits, ever** (AGENTS.md §6 — Shads is the sole committer). Every task ends with a "Record completion in plans/Dependency_plan.md" step instead.
- **Dependency gate (AGENTS.md §5 rule 1):** before starting, verify prerequisites in `plans/Dependency_plan.md`. E-3.2 wires to T-3.1/T-3.2 (Hrittika) — Task 1 Step 1 gates on them.
- **Concurrency contract:** T-3.1 (`ActsManagementService`) and T-3.2 (`ScenarioMappingService`) are being built concurrently by Hrittika. The **Consumed Interfaces** section below is the binding contract. If Hrittika's landed code deviates, her signatures win — adapt the controller code in Tasks 2–3 to her real signatures (names/types only; the behavior under test stays identical, so only the Moq setups change).
- **Authorization:** `AdminController` is class-level `[Authorize(Roles = "Admin")]` — new actions inherit it. Never add `[AllowAnonymous]` (existing `AdminControllerTests` asserts this for telemetry endpoints).
- **POST actions** always get `[HttpPost]` + `[ValidateAntiForgeryToken]`; delete/suspend forms carry `data-confirm="..."` (handled by `main.js` ~line 1686).

---

## Consumed Interfaces (contract with T-3.1 / T-3.2)

These signatures are consumed by Tasks 2–4. If `docs/superpowers/plans/2026-09-12-acts-scenario-management.md` exists when you start, read it first and reconcile — **her landed signatures supersede these**. If nothing exists yet, create these files exactly as specified in Task 1 so the frontend can build and be tested (Moq) against the contract.

```csharp
// src/MuktoAin.Application/Services/IActsManagementService.cs (T-3.1)
public interface IActsManagementService
{
    /// <summary>Paged, filtered Acts list. query matches Title/ActNumber (case-insensitive); year filters exactly.</summary>
    Task<ActListPageDto> GetActsPagedAsync(string? query, int? year, int page, int pageSize);

    /// <summary>Act with sections ordered by OrdinalPosition, incl. per-section chunk + scenario-mapping counts. null if not found.</summary>
    Task<ActDetailDto?> GetActDetailAsync(int actId);

    /// <summary>Updates admin-editable metadata; recomputes the SHA-256 content hash and schedules vector re-indexing.
    /// Returns false when the Act does not exist or validation fails.</summary>
    Task<bool> UpdateActMetadataAsync(ActMetadataUpdateDto update);
}

// src/MuktoAin.Application/Services/IScenarioMappingService.cs (T-3.2)
public interface IScenarioMappingService
{
    Task<IReadOnlyList<ScenarioMappingDto>> GetAllMappingsAsync();

    /// <summary>Adds a FR-18 keyword boost. Returns null when keyword is empty/whitespace or sectionId is unknown.</summary>
    Task<ScenarioMappingDto?> AddMappingAsync(string keyword, int sectionId, string? notes);

    /// <summary>Deletes a mapping. Returns false when mappingId does not exist.</summary>
    Task<bool> DeleteMappingAsync(int mappingId);
}
```

DTOs (namespace `MuktoAin.Application.DTOs`):

```csharp
public record ActListPageDto(IReadOnlyList<ActListItemDto> Items, int Page, int PageSize, int TotalCount);

public record ActListItemDto(
    int ActId, string Title, string ActNumber, int Year,
    string Language, bool IsRepealed,
    int SectionCount, int ChunkCount, int EmbeddedCount);

public record ActSectionDto(
    int SectionId, int OrdinalPosition, string? SectionNumber,
    string? SectionTitle, string SectionText,
    int ChunkCount, int MappingCount);

public record ActDetailDto(
    int ActId, string Title, string ActNumber, int Year, string PublicationDate,
    string Language, bool IsRepealed, string SourceUrl, DateTime ImportedAt,
    IReadOnlyList<ActSectionDto> Sections);

// Admin-editable fields only. PublicationDate, Language, TokenCount, ImportedAt
// are ingestion-owned and NOT editable through the admin UI.
public record ActMetadataUpdateDto(
    int ActId, string Title, string ActNumber, int Year, bool IsRepealed, string SourceUrl);

public record ScenarioMappingDto(
    int MappingId, string Keyword, int SectionId,
    string ActTitle, string SectionNumber, string? Notes);
```

Existing service consumed by E-3.3 (already implemented, S-3.6 — do **not** change):

```csharp
// src/MuktoAin.Application/Services/IUserManagementService.cs
public interface IUserManagementService
{
    Task<IEnumerable<UserListDto>> GetAllUsersAsync();
    // Guards: admins can never be suspended; an admin cannot flip their own status.
    Task<bool> SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId);
}
// UserListDto(int UserId, string FullName, string Email, string Role, string Status)
```

---

### Task 1: Service contract — create `IActsManagementService` / `IScenarioMappingService` interfaces + DTOs

**Files:**
- Create: `src/MuktoAin.Application/Services/IActsManagementService.cs`
- Create: `src/MuktoAin.Application/Services/IScenarioMappingService.cs`
- Create: `src/MuktoAin.Application/DTOs/ActsManagementDtos.cs`
- Create: `src/MuktoAin.Application/DTOs/ScenarioMappingDtos.cs`

**Interfaces:**
- Produces: the exact interface signatures in the **Consumed Interfaces** section above, consumed by Tasks 2–3.

- [ ] **Step 1: Dependency gate**

Run: `rg -n "T-3.1|T-3.2" plans/Dependency_plan.md`
- If `docs/superpowers/plans/2026-09-12-acts-scenario-management.md` exists, read it and use **its** interface/DTO definitions for the rest of this plan (her code wins). Skip to Step 2 only to verify the contract matches.
- If T-3.1/T-3.2 are already `- [x]` in `plans/Dependency_plan.md`, their code exists: verify the files below don't already exist (`glob src/MuktoAin.Application/Services/IActsManagementService.cs`) and consume the real ones instead of creating.
- If neither the plan nor the implementations exist yet, create the contract files exactly as specified here (the frontend and its tests compile and run against interfaces + Moq; only the **browser E2E** steps in Task 4 need Hrittika's implementations registered in DI).

- [ ] **Step 2: Create the DTO files**

`src/MuktoAin.Application/DTOs/ActsManagementDtos.cs`:

```csharp
namespace MuktoAin.Application.DTOs;

// T-3.1 contract: one page of the admin Acts list (FR-17).
public record ActListPageDto(
    IReadOnlyList<ActListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public record ActListItemDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string Language,
    bool IsRepealed,
    int SectionCount,
    int ChunkCount,
    int EmbeddedCount);

public record ActSectionDto(
    int SectionId,
    int OrdinalPosition,
    string? SectionNumber,
    string? SectionTitle,
    string SectionText,
    int ChunkCount,
    int MappingCount);

// Full act detail incl. sections (FR-17 admin view).
public record ActDetailDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    string PublicationDate,
    string Language,
    bool IsRepealed,
    string SourceUrl,
    DateTime ImportedAt,
    IReadOnlyList<ActSectionDto> Sections);

// Editable metadata only. PublicationDate / Language / TokenCount / ImportedAt
// are ingestion-owned (design.md 2.3) and not admin-editable.
public record ActMetadataUpdateDto(
    int ActId,
    string Title,
    string ActNumber,
    int Year,
    bool IsRepealed,
    string SourceUrl);
```

`src/MuktoAin.Application/DTOs/ScenarioMappingDtos.cs`:

```csharp
namespace MuktoAin.Application.DTOs;

// T-3.2 contract: one FR-18 scenario keyword -> section boost row.
public record ScenarioMappingDto(
    int MappingId,
    string Keyword,
    int SectionId,
    string ActTitle,
    string SectionNumber,
    string? Notes);
```

- [ ] **Step 3: Create the interface files**

`src/MuktoAin.Application/Services/IActsManagementService.cs`:

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.1: admin acts management (FR-17). Consumed by AdminController's
// /Admin/Acts views (E-3.2); implemented by ActsManagementService.
public interface IActsManagementService
{
    /// <summary>Paged, filtered Acts list. query matches Title/ActNumber; year filters exactly.</summary>
    Task<ActListPageDto> GetActsPagedAsync(string? query, int? year, int page, int pageSize);

    /// <summary>Act with its sections (ordered by OrdinalPosition) incl. per-section
    /// chunk and scenario-mapping counts. null if not found.</summary>
    Task<ActDetailDto?> GetActDetailAsync(int actId);

    /// <summary>Updates admin-editable metadata and schedules SHA256 re-indexing.
    /// Returns false if the Act does not exist or validation failed.</summary>
    Task<bool> UpdateActMetadataAsync(ActMetadataUpdateDto update);
}
```

`src/MuktoAin.Application/Services/IScenarioMappingService.cs`:

```csharp
using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

// T-3.2: admin CRUD for FR-18 scenario keyword boosts. Consumed by
// AdminController's /Admin/Scenarios actions (E-3.2); implemented by
// ScenarioMappingService.
public interface IScenarioMappingService
{
    Task<IReadOnlyList<ScenarioMappingDto>> GetAllMappingsAsync();

    /// <summary>Adds a keyword boost. Returns null when keyword is empty/whitespace
    /// or sectionId is unknown.</summary>
    Task<ScenarioMappingDto?> AddMappingAsync(string keyword, int sectionId, string? notes);

    /// <returns>false when mappingId does not exist.</returns>
    Task<bool> DeleteMappingAsync(int mappingId);
}
```

- [ ] **Step 4: Build to verify the contract compiles**

Run: `dotnet build src/MuktoAin.Application/MuktoAin.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Record completion in plans/Dependency_plan.md**

Do NOT commit (AGENTS.md §6). In `plans/Dependency_plan.md`, note under the `[T-3.1]`/`[T-3.2]` lines (line ~133–134) that the consumed interface contract has been defined in this plan file — leave both checkboxes untouched (they belong to Hrittika).

---

### Task 2: AdminController Acts actions (`Acts`, `ActDetail`, `EditAct`) — TDD

**Files:**
- Modify: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs` (append 3 view models)
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (ctor + 3 new actions)
- Create: `tests/MuktoAin.UnitTests/Controllers/AdminActsTests.cs`

**Interfaces:**
- Consumes: `IActsManagementService`, `ActListPageDto`, `ActDetailDto`, `ActMetadataUpdateDto` (Task 1).
- Produces: routes `GET /Admin/Acts?q&year&page`, `GET /Admin/ActDetail/{id}`, `POST /Admin/EditAct` (form fields: `actId, title, actNumber, year, isRepealed, sourceUrl`); view models `AdminActsViewModel`, `AdminActDetailViewModel` (consumed by Task 4's Razor views).

- [ ] **Step 1: Add the view models**

Append to `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`:

```csharp
public class AdminActsViewModel
{
    public List<AdminActRowViewModel> Acts { get; set; } = new();
    public string? Query { get; set; }
    public int? Year { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public class AdminActDetailViewModel
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ActNumber { get; set; } = string.Empty;
    public int Year { get; set; }
    public string PublicationDate { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsRepealed { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public int ChunkCount { get; set; }
    public List<AdminActSectionRowViewModel> Sections { get; set; } = new();
}

public class AdminActSectionRowViewModel
{
    public int SectionId { get; set; }
    public string SectionNumber { get; set; } = string.Empty;
    public string? SectionTitle { get; set; }
    public string SectionTextPreview { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public int MappingCount { get; set; }
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MuktoAin.UnitTests/Controllers/AdminActsTests.cs`. The controller's 15 existing ctor deps are mocked exactly like `PaymentControllerTests` does for its concrete deps; `GeminiClient` is constructed for real (its ctor throws without API keys — `Mock.Of<GeminiClient>()` would throw), following the pattern from `GeminiClientTests`:

```csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.UnitTests.Controllers;

// E-3.2: controller-side logic of /Admin/Acts (validation, not-found handling,
// TempData contracts, service call shapes). IActsManagementService /
// IScenarioMappingService are Moq'd -- the controller is the unit under test.
public class AdminActsTests
{
    private readonly Mock<IActsManagementService> _acts = new();
    private readonly Mock<IScenarioMappingService> _scenarios = new();

    private AdminController BuildController()
    {
        var userManager = new UserManager<User>(
            new Mock<IUserStore<User>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var geminiClient = new GeminiClient(
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions
            {
                ApiKeys = new[] { "test-key-1" },
                GenerationModel = "gemini-2.5-flash",
                EmbeddingModel = "gemini-embedding-001"
            }),
            Mock.Of<IHttpClientFactory>(),
            new Polly.ResiliencePipelineBuilder<HttpResponseMessage>().Build());

        return new AdminController(
            Mock.Of<ILogger<AdminController>>(),
            Mock.Of<AppDbContext>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IUserManagementService>(),
            Mock.Of<LawyerVerificationService>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            userManager,
            Mock.Of<IActRepository>(),
            Mock.Of<IActSectionRepository>(),
            Mock.Of<IActSectionChunkRepository>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IRepository<CaseCategory>>(),
            Mock.Of<IRepository<AiLog>>(),
            Mock.Of<PaymentService>(),
            geminiClient,
            _acts.Object,
            _scenarios.Object);
    }

    [Fact]
    public async Task Acts_PassesFilterAndPagingToService_AndMapsRows()
    {
        _acts.Setup(s => s.GetActsPagedAsync("labour", 2006, 2, 25))
            .ReturnsAsync(new ActListPageDto(
                new[] { new ActListItemDto(7, "The Bangladesh Labour Act, 2006", "Act XLII of 2006", 2006, "bangla", false, 400, 1200, 1100) },
                2, 25, 51));

        var result = await BuildController().Acts("labour", 2006, 2);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<AdminActsViewModel>(view.Model);
        Assert.Equal(51, vm.TotalCount);
        Assert.Equal(3, vm.TotalPages);
        var row = Assert.Single(vm.Acts);
        Assert.Equal(7, row.ActId);
        Assert.Equal(1200, row.ChunkCount);
        Assert.Equal(1100, row.EmbeddedCount);
    }

    [Fact]
    public async Task Acts_PageBelowOne_ClampsToPage1()
    {
        _acts.Setup(s => s.GetActsPagedAsync(null, null, 1, 25))
            .ReturnsAsync(new ActListPageDto(Array.Empty<ActListItemDto>(), 1, 25, 0));

        await BuildController().Acts(null, null, page: 0);

        _acts.Verify(s => s.GetActsPagedAsync(null, null, 1, 25), Times.Once);
    }

    [Fact]
    public async Task ActDetail_UnknownAct_ReturnsNotFound()
    {
        _acts.Setup(s => s.GetActDetailAsync(999)).ReturnsAsync((ActDetailDto?)null);

        var result = await BuildController().ActDetail(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task EditAct_BlankTitle_RedirectsWithoutCallingService()
    {
        var controller = BuildController();
        var result = await controller.EditAct(5, title: "   ", actNumber: "Act 1", year: 2006, isRepealed: false, sourceUrl: null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ActDetail", redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues!["id"]);
        Assert.NotNull(controller.TempData["Error"]);
        _acts.Verify(s => s.UpdateActMetadataAsync(It.IsAny<ActMetadataUpdateDto>()), Times.Never);
    }

    [Fact]
    public async Task EditAct_YearOutOfRange_RedirectsWithoutCallingService()
    {
        var controller = BuildController();
        var result = await controller.EditAct(5, title: "T", actNumber: "", year: 2099, isRepealed: false, sourceUrl: null);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.NotNull(controller.TempData["ErrorEn"]);
        _acts.Verify(s => s.UpdateActMetadataAsync(It.IsAny<ActMetadataUpdateDto>()), Times.Never);
    }

    [Fact]
    public async Task EditAct_ValidInput_CallsServiceAndSetsSuccess()
    {
        _acts.Setup(s => s.UpdateActMetadataAsync(It.Is<ActMetadataUpdateDto>(d =>
            d.ActId == 5 && d.Title == "New Title" && d.Year == 2006 && d.IsRepealed)))
            .ReturnsAsync(true);

        var controller = BuildController();
        var result = await controller.EditAct(5, "  New Title  ", " Act 1 ", 2006, isRepealed: true, sourceUrl: " http://bdlaws.example/act ");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Success"]);
        _acts.Verify(s => s.UpdateActMetadataAsync(It.Is<ActMetadataUpdateDto>(d =>
            d.ActId == 5 && d.Title == "New Title" && d.ActNumber == "Act 1" && d.SourceUrl == "http://bdlaws.example/act")), Times.Once);
    }
}
```

Note: `AdminActsTests.Acts_...` asserts against the mapped `AdminActsViewModel`; `Mock.Of<AppDbContext>()` is safe here because the tested actions never touch `_dbContext`.

- [ ] **Step 3: Run the tests to verify they fail (RED)**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminActsTests"`
Expected: **compile error** — `AdminController` has no `IActsManagementService` constructor parameter and no `Acts`/`ActDetail`/`EditAct` methods. (If you must see runtime failures instead, skip the compile check — either way this is RED.)

- [ ] **Step 4: Implement the controller changes**

In `src/MuktoAin.Web/Controllers/AdminController.cs`:

1. Add a field + ctor parameter (after `GeminiClient _geminiClient;` and its ctor param):

```csharp
    private readonly IActsManagementService _actsManagement;
    private readonly IScenarioMappingService _scenarioMapping;
```

Ctor signature additions (append as the last two parameters, in this order):

```csharp
        IActsManagementService actsManagement,
        IScenarioMappingService scenarioMapping)
    {
        // ... existing assignments ...
        _actsManagement = actsManagement;
        _scenarioMapping = scenarioMapping;
    }
```

2. Add a new region after the `// ---------- FR-17: Corpus ----------` block:

```csharp
    // ---------- FR-17: Acts management (E-3.2, wires to T-3.1) ----------

    [HttpGet]
    public async Task<IActionResult> Acts(string? q, int? year, int page = 1)
    {
        ViewData["IsAdminPage"] = true;
        const int pageSize = 25;
        var result = await _actsManagement.GetActsPagedAsync(q, year, page < 1 ? 1 : page, pageSize);
        var vm = new AdminActsViewModel
        {
            Query = q,
            Year = year,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Acts = result.Items.Select(a => new AdminActRowViewModel
            {
                ActId = a.ActId,
                Title = a.Title,
                ActNumber = a.ActNumber,
                Year = a.Year,
                Language = a.Language,
                IsRepealed = a.IsRepealed,
                SectionCount = a.SectionCount,
                ChunkCount = a.ChunkCount,
                EmbeddedCount = a.EmbeddedCount
            }).ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ActDetail(int id)
    {
        ViewData["IsAdminPage"] = true;
        var act = await _actsManagement.GetActDetailAsync(id);
        if (act is null)
        {
            return NotFound();
        }
        var vm = new AdminActDetailViewModel
        {
            ActId = act.ActId,
            Title = act.Title,
            ActNumber = act.ActNumber,
            Year = act.Year,
            PublicationDate = act.PublicationDate,
            Language = act.Language,
            IsRepealed = act.IsRepealed,
            SourceUrl = act.SourceUrl,
            ImportedAt = act.ImportedAt,
            ChunkCount = act.Sections.Sum(s => s.ChunkCount),
            Sections = act.Sections
                .OrderBy(s => s.OrdinalPosition)
                .Select(s => new AdminActSectionRowViewModel
                {
                    SectionId = s.SectionId,
                    SectionNumber = s.SectionNumber ?? "",
                    SectionTitle = s.SectionTitle,
                    SectionTextPreview = s.SectionText.Length > 240 ? s.SectionText[..240] + "…" : s.SectionText,
                    ChunkCount = s.ChunkCount,
                    MappingCount = s.MappingCount
                })
                .ToList()
        };
        return View(vm);
    }

    // Admin-editable metadata only. PublicationDate / Language / TokenCount /
    // ImportedAt stay ingestion-owned (design.md 2.3). UpdateActMetadataAsync
    // recomputes SHA-256 and schedules re-indexing (T-3.1).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAct(int actId, string title, string actNumber, int year, bool isRepealed, string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(title) || year < 1700 || year > DateTime.UtcNow.Year)
        {
            TempData["Error"] = "শিরোনাম আবশ্যক এবং সাল অবশ্যই ১৭০০ থেকে বর্তমান বছরের মধ্যে হতে হবে।";
            TempData["ErrorEn"] = "Title is required and year must be between 1700 and the current year.";
            return RedirectToAction(nameof(ActDetail), new { id = actId });
        }
        if (title.Length > 500)
        {
            TempData["Error"] = "শিরোনাম ৫০০ অক্ষরের কম হতে হবে।";
            TempData["ErrorEn"] = "Title must be 500 characters or fewer.";
            return RedirectToAction(nameof(ActDetail), new { id = actId });
        }

        var ok = await _actsManagement.UpdateActMetadataAsync(new ActMetadataUpdateDto(
            actId, title.Trim(), (actNumber ?? "").Trim(), year, isRepealed, (sourceUrl ?? "").Trim()));

        if (ok)
        {
            TempData["Success"] = "আইনের মেটাডেটা হালনাগাদ হয়েছে — পুনঃ-ইনডেক্সিং সময়সূচিভুক্ত।";
            TempData["SuccessEn"] = "Act metadata updated — re-indexing scheduled.";
        }
        else
        {
            TempData["Error"] = "আইনটি খুঁজে পাওয়া যায়নি বা সংরক্ষণ ব্যর্থ হয়েছে।";
            TempData["ErrorEn"] = "Act not found or the update failed.";
        }
        return RedirectToAction(nameof(ActDetail), new { id = actId });
    }
```

- [ ] **Step 5: Run the tests to verify they pass (GREEN)**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminActsTests"`
Expected: PASS — 6/6 tests.
Note: if `Mock.Of<LawyerVerificationService>()` / `Mock.Of<PaymentService>()` fails to construct (ctor null-check), construct them for real with mocked repositories exactly as `PaymentControllerTests` does — never weaken an assertion to make it pass.

- [ ] **Step 6: Register the services in DI**

Modify `src/MuktoAin.Web/Program.cs` — add directly after the `IUserManagementService` registration (~line 197):

```csharp
builder.Services.AddScoped<IActsManagementService, ActsManagementService>();
builder.Services.AddScoped<IScenarioMappingService, ScenarioMappingService>();
```

Gate: this step compiles only once Hrittika's `ActsManagementService` / `ScenarioMappingService` concrete classes exist (T-3.1/T-3.2). If they don't exist yet, **skip this step and Task 4's browser E2E**; the build and unit tests from Steps 4–5 remain valid. Re-run `dotnet build src/MuktoAin.Web/MuktoAin.Web.csproj` after registering — Expected: Build succeeded.

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Do NOT commit. Note the controller actions + tests under the E-3.2 line (Task 5 of this plan flips the checkbox once views land).

---

### Task 3: Rewire scenario-mapping CRUD to `IScenarioMappingService`

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (Scenarios / AddScenario / DeleteScenario actions; drop 3 unused repo deps)
- Modify: `tests/MuktoAin.UnitTests/Controllers/AdminActsTests.cs` (helper + new tests)

**Interfaces:**
- Consumes: `IScenarioMappingService` (Task 1 contract).
- Produces: unchanged routes `GET /Admin/Scenarios`, `POST /Admin/AddScenario`, `POST /Admin/DeleteScenario` — but now service-backed and bilingual-toasting.

- [ ] **Step 1: Write the failing tests**

Add to `tests/MuktoAin.UnitTests/Controllers/AdminActsTests.cs`:

```csharp
    [Fact]
    public async Task AddScenario_BlankKeyword_ShowsBilingualError_WithoutCallingServiceSuccessPath()
    {
        _scenarios.Setup(s => s.AddMappingAsync("", 0, null)).ReturnsAsync((ScenarioMappingDto?)null);

        var controller = BuildController();
        var result = await controller.AddScenario(sectionId: 0, keyword: "  ", notes: null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Scenarios", redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.NotNull(controller.TempData["ErrorEn"]);
    }

    [Fact]
    public async Task AddScenario_ValidInput_CallsServiceAndShowsSuccess()
    {
        _scenarios.Setup(s => s.AddMappingAsync("বেতন বাকি", 12345, "unpaid wages"))
            .ReturnsAsync(new ScenarioMappingDto(9, "বেতন বাকি", 12345, "শ্রম আইন ২০০৬", "১২৩", "unpaid wages"));

        var controller = BuildController();
        var result = await controller.AddScenario(12345, "বেতন বাকি", "unpaid wages");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Success"]);
        _scenarios.Verify(s => s.AddMappingAsync("বেতন বাকি", 12345, "unpaid wages"), Times.Once);
    }

    [Fact]
    public async Task DeleteScenario_UnknownMapping_ShowsError()
    {
        _scenarios.Setup(s => s.DeleteMappingAsync(999)).ReturnsAsync(false);

        var controller = BuildController();
        var result = await controller.DeleteScenario(999);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Error"]);
        _scenarios.Verify(s => s.DeleteMappingAsync(999), Times.Once);
    }
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminActsTests"`
Expected: the 3 new tests FAIL — the current `AddScenario` writes via `IScenarioMappingRepository` directly and only sets an English-only `TempData["Success"]` (no `*En` key, no error path), so `TempData["Error"]`/bilingual assertions fail.

- [ ] **Step 3: Rewire the three actions**

Replace the bodies of `Scenarios`, `AddScenario`, `DeleteScenario` in `AdminController.cs` (`// ---------- FR-18: Scenario mappings ----------` region):

```csharp
    [HttpGet]
    public async Task<IActionResult> Scenarios()
    {
        var mappings = await _scenarioMapping.GetAllMappingsAsync();
        var vm = new AdminScenariosViewModel
        {
            Mappings = mappings.Select(m => new AdminScenarioRowViewModel
            {
                MappingId = m.MappingId,
                Keyword = m.Keyword,
                ActTitle = m.ActTitle,
                SectionNumber = m.SectionNumber,
                Notes = m.Notes
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScenario(int sectionId, string keyword, string? notes)
    {
        var added = await _scenarioMapping.AddMappingAsync(keyword ?? "", sectionId, notes);
        if (added is null)
        {
            TempData["Error"] = "কীওয়ার্ড এবং বৈধ Section ID আবশ্যক।";
            TempData["ErrorEn"] = "Keyword and a valid Section ID are required.";
            return RedirectToAction(nameof(Scenarios));
        }
        TempData["Success"] = "ম্যাপিং যোগ করা হয়েছে।";
        TempData["SuccessEn"] = "Mapping added.";
        return RedirectToAction(nameof(Scenarios));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScenario(int mappingId)
    {
        var ok = await _scenarioMapping.DeleteMappingAsync(mappingId);
        if (!ok)
        {
            TempData["Error"] = "ম্যাপিংটি খুঁজে পাওয়া যায়নি।";
            TempData["ErrorEn"] = "Mapping not found.";
            return RedirectToAction(nameof(Scenarios));
        }
        TempData["Success"] = "ম্যাপিং মুছে ফেলা হয়েছে।";
        TempData["SuccessEn"] = "Mapping deleted.";
        return RedirectToAction(nameof(Scenarios));
    }
```

- [ ] **Step 4: Drop the now-unused repository dependencies**

In `AdminController.cs` remove the fields, ctor parameters, and assignments for `IActRepository _actRepo`, `IActSectionRepository _sectionRepo`, and `IScenarioMappingRepository _scenarioRepo` (verify first with `rg -n "_actRepo|_sectionRepo|_scenarioRepo" src/MuktoAin.Web/Controllers/AdminController.cs` that only the old Scenarios code referenced them — `Corpus`/`Categories` use `_dbContext`/`_categoryRepo`, which stay). Also remove `IActRepository`/`IActSectionRepository`/`IScenarioMappingRepository` ctor args from `BuildController()` in `AdminActsTests.cs`. Keep `IActSectionChunkRepository _chunkRepo` untouched.

- [ ] **Step 5: Run to verify GREEN**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS — full unit-test suite green (new tests + no regressions).

- [ ] **Step 6: Verify no repository leakage remains**

Run: `rg -n "IScenarioMappingRepository|IActRepository|IActSectionRepository" src/MuktoAin.Web/Controllers/AdminController.cs`
Expected: no matches (the registrations in `Program.cs` stay — `ActsManagementService`/`ScenarioMappingService` consume them).

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Do NOT commit. Note the rewire under the E-3.2 line.

---

### Task 4: Razor views — `Acts.cshtml`, `ActDetail.cshtml`, bilingual `Scenarios.cshtml`, nav links

**Files:**
- Create: `src/MuktoAin.Web/Views/Admin/Acts.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/ActDetail.cshtml`
- Modify: `src/MuktoAin.Web/Views/Admin/Scenarios.cshtml` (full bilingual rewrite)
- Modify: `src/MuktoAin.Web/Views/Shared/_Layout.cshtml` (~lines 77-91 desktop nav, ~lines 177-184 drawer nav)

**Interfaces:**
- Consumes: `AdminActsViewModel`, `AdminActDetailViewModel`, `AdminScenariosViewModel`; routes from Task 2/3.

- [ ] **Step 1: Create `Views/Admin/Acts.cshtml`**

```cshtml
@model AdminActsViewModel
@{
    ViewData["Title"] = "Acts — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard" data-bn="ড্যাশবোর্ড" data-en="Dashboard">ড্যাশবোর্ড</a>
        <span class="sep">/</span>
        <span data-bn="আইনসমূহ" data-en="Acts">আইনসমূহ</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="library"></i> FR-17</span>
        <h1 class="page-title" data-bn="আইন ব্যবস্থাপনা" data-en="Acts Management">আইন ব্যবস্থাপনা</h1>
        <p class="page-sub"
           data-bn="মোট @Model.TotalCount টি আইন — মেটাডেটা সম্পাদনা, ধারা পরিদর্শন ও ভেক্টর-ইনডেক্স অবস্থা।"
           data-en="@Model.TotalCount acts — edit metadata, inspect sections, and vector-index status.">মোট @Model.TotalCount টি আইন।</p>
    </div>

    <div class="card" style="margin-bottom: 16px">
        <form asp-action="Acts" method="get" class="row wrap" style="gap: 10px; align-items: flex-end">
            <div>
                <label class="form-label" data-bn="শিরোনাম / আইন নম্বর" data-en="Title / Act number">Title / Act number</label>
                <input class="input" name="q" value="@Model.Query" style="min-width: 240px"
                       placeholder="labour / শ্রম / XLII" />
            </div>
            <div>
                <label class="form-label" data-bn="সাল" data-en="Year">Year</label>
                <input class="input" name="year" type="number" min="1700" max="2100" value="@Model.Year" style="width: 120px" />
            </div>
            <button class="btn btn-outline btn-sm" type="submit" data-bn="খুঁজুন" data-en="Apply">খুঁজুন</button>
            <a class="btn btn-outline btn-sm" asp-action="Acts" data-bn="রিসেট" data-en="Reset">রিসেট</a>
        </form>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr>
                    <th data-bn="আইন" data-en="Act">আইন</th>
                    <th data-bn="সাল" data-en="Year">সাল</th>
                    <th data-bn="ভাষা" data-en="Lang">ভাষা</th>
                    <th data-bn="ধারা" data-en="Sections">ধারা</th>
                    <th data-bn="চাংক" data-en="Chunks">চাংক</th>
                    <th data-bn="এমবেডেড" data-en="Embedded">এমবেডেড</th>
                    <th data-bn="অবস্থা" data-en="Status">অবস্থা</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var a in Model.Acts)
                {
                    <tr>
                        <td>
                            <a asp-action="ActDetail" asp-route-id="@a.ActId">@a.Title</a><br />
                            <small class="muted mono">@(string.IsNullOrEmpty(a.ActNumber) ? "" : a.ActNumber + " · ")@a.ActId</small>
                        </td>
                        <td>@a.Year</td>
                        <td>@a.Language</td>
                        <td>@a.SectionCount</td>
                        <td>@a.ChunkCount</td>
                        <td>@a.EmbeddedCount</td>
                        <td>
                            @if (a.IsRepealed)
                            {
                                <span class="badge badge-rejected" data-bn="বাতিলকৃত" data-en="Repealed">বাতিলকৃত</span>
                            }
                            else if (a.EmbeddedCount == 0 && a.ChunkCount > 0)
                            {
                                <span class="badge badge-review" data-bn="এমবেডিং অপেক্ষমাণ" data-en="Awaiting embed">এমবেডিং অপেক্ষমাণ</span>
                            }
                            else if (a.EmbeddedCount < a.ChunkCount)
                            {
                                <span class="badge badge-draft" data-bn="আংশিক" data-en="Partial">আংশিক</span>
                            }
                            else
                            {
                                <span class="badge badge-final" data-bn="সিংকড" data-en="Synced">সিংকড</span>
                            }
                        </td>
                    </tr>
                }
                @if (!Model.Acts.Any())
                {
                    <tr>
                        <td colspan="7" style="text-align:center; padding: 24px" class="muted"
                            data-bn="কোনো আইন পাওয়া যায়নি" data-en="No acts found">কোনো আইন পাওয়া যায়নি</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>

    @if (Model.TotalPages > 1)
    {
        <div class="row" style="gap: 10px; align-items: center; margin-top: 14px">
            @if (Model.Page > 1)
            {
                <a class="btn btn-outline btn-sm" asp-action="Acts" asp-route-q="@Model.Query" asp-route-year="@Model.Year" asp-route-page="@(Model.Page - 1)"
                   data-bn="← পূর্ববর্তী" data-en="← Prev">← পূর্ববর্তী</a>
            }
            <span class="muted tiny"
                  data-bn="পৃষ্ঠা @Model.Page / @Model.TotalPages" data-en="Page @Model.Page of @Model.TotalPages">পৃষ্ঠা @Model.Page / @Model.TotalPages</span>
            @if (Model.Page < Model.TotalPages)
            {
                <a class="btn btn-outline btn-sm" asp-action="Acts" asp-route-q="@Model.Query" asp-route-year="@Model.Year" asp-route-page="@(Model.Page + 1)"
                   data-bn="পরবর্তী →" data-en="Next →">পরবর্তী →</a>
            }
        </div>
    }
</main>
```

- [ ] **Step 2: Create `Views/Admin/ActDetail.cshtml`**

```cshtml
@model AdminActDetailViewModel
@{
    ViewData["Title"] = Model.Title + " — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard" data-bn="ড্যাশবোর্ড" data-en="Dashboard">ড্যাশবোর্ড</a>
        <span class="sep">/</span>
        <a asp-controller="Admin" asp-action="Acts" data-bn="আইনসমূহ" data-en="Acts">আইনসমূহ</a>
        <span class="sep">/</span>
        <span>#@Model.ActId</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="library"></i> FR-17 · #@Model.ActId</span>
        <h1 class="page-title">@Model.Title</h1>
        <p class="page-sub">
            @(string.IsNullOrEmpty(Model.ActNumber) ? "" : Model.ActNumber + " · ")@Model.Year ·
            <span data-bn="আমদানি:" data-en="Imported:">আমদানি:</span> @Model.ImportedAt.ToString("d MMM yyyy")
            @if (Model.IsRepealed)
            {
                <span class="badge badge-rejected" data-bn="বাতিলকৃত" data-en="Repealed">বাতিলকৃত</span>
            }
        </p>
    </div>

    <details class="acc card" style="margin-bottom: 16px">
        <summary><b data-bn="মেটাডেটা সম্পাদনা" data-en="Edit metadata">মেটাডেটা সম্পাদনা</b></summary>
        <form asp-action="EditAct" method="post" class="row wrap" style="gap: 10px; align-items: flex-end; margin-top: 12px">
            @Html.AntiForgeryToken()
            <input type="hidden" name="actId" value="@Model.ActId" />
            <div style="flex: 2; min-width: 240px">
                <label class="form-label" data-bn="শিরোনাম" data-en="Title">শিরোনাম</label>
                <input class="input" name="title" value="@Model.Title" maxlength="500" required />
            </div>
            <div>
                <label class="form-label" data-bn="আইন নম্বর" data-en="Act number">আইন নম্বর</label>
                <input class="input" name="actNumber" value="@Model.ActNumber" />
            </div>
            <div>
                <label class="form-label" data-bn="সাল" data-en="Year">সাল</label>
                <input class="input" name="year" type="number" min="1700" max="@DateTime.UtcNow.Year" value="@Model.Year" required />
            </div>
            <div>
                <label class="form-label" data-bn="সূত্র URL" data-en="Source URL">সূত্র URL</label>
                <input class="input" name="sourceUrl" value="@Model.SourceUrl" style="min-width: 220px" />
            </div>
            <label style="display: flex; gap: 6px; align-items: center">
                <input type="checkbox" name="isRepealed" value="true" checked="@Model.IsRepealed" />
                <span data-bn="বাতিলকৃত" data-en="Repealed">বাতিলকৃত</span>
            </label>
            <button class="btn btn-primary btn-sm" type="submit" data-bn="সংরক্ষণ করুন" data-en="Save">সংরক্ষণ করুন</button>
            <p class="muted tiny" style="flex-basis: 100%"
               data-bn="সংরক্ষণে SHA-256 চেকসাম পুনঃগণনা ও ভেক্টর পুনঃ-ইনডেক্সিং সময়সূচিভুক্ত হয়। প্রকাশকাল, ভাষা ও টোকেন সংখ্যা ইনজেশন-নিয়ন্ত্রিত — এখানে সম্পাদনযোগ্য নয়।"
               data-en="Saving recomputes the SHA-256 checksum and schedules vector re-indexing. Publication date, language, and token count are ingestion-owned and not editable here.">…</p>
        </form>
    </details>

    <div class="card">
        <table>
            <thead>
                <tr>
                    <th data-bn="ধারা" data-en="Section">ধারা</th>
                    <th data-bn="শিরোনাম" data-en="Title">শিরোনাম</th>
                    <th data-bn="টেক্সট (প্রিভিউ)" data-en="Text (preview)">টেক্সট (প্রিভিউ)</th>
                    <th data-bn="চাংক" data-en="Chunks">চাংক</th>
                    <th data-bn="ম্যাপিং" data-en="Mappings">ম্যাপিং</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var s in Model.Sections)
                {
                    <tr>
                        <td class="mono">@(string.IsNullOrEmpty(s.SectionNumber) ? "— header —" : s.SectionNumber)</td>
                        <td>@s.SectionTitle</td>
                        <td class="tiny" style="max-width: 420px">@s.SectionTextPreview</td>
                        <td>@s.ChunkCount</td>
                        <td>@s.MappingCount</td>
                    </tr>
                }
                @if (!Model.Sections.Any())
                {
                    <tr>
                        <td colspan="5" style="text-align:center; padding: 24px" class="muted"
                            data-bn="এই আইনে কোনো ধারা নেই" data-en="This act has no sections">এই আইনে কোনো ধারা নেই</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
</main>
```

- [ ] **Step 3: Rewrite `Views/Admin/Scenarios.cshtml` (bilingual)**

Replace the whole file:

```cshtml
@model AdminScenariosViewModel
@{
    ViewData["Title"] = "Scenario Mappings — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard" data-bn="ড্যাশবোর্ড" data-en="Dashboard">ড্যাশবোর্ড</a>
        <span class="sep">/</span>
        <span data-bn="সিনারিও ম্যাপিং" data-en="Scenario Mappings">সিনারিও ম্যাপিং</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="git-merge"></i> FR-18</span>
        <h1 class="page-title" data-bn="সিনারিও ম্যাপিং" data-en="Scenario Mappings">সিনারিও ম্যাপিং</h1>
        <p class="page-sub"
           data-bn="নাগরিকের কীওয়ার্ড → যে আইনের ধারা রিট্রিভাল পাইপলাইন অগ্রাধিকার দেয়। মোট @Model.Mappings.Count টি ম্যাপিং।"
           data-en="Citizen keywords → statute sections the retrieval pipeline prioritizes. @Model.Mappings.Count mappings.">
           নাগরিকের কীওয়ার্ড → রিট্রিভাল পাইপলাইনের অগ্রাধিকারভুক্ত ধারা। @Model.Mappings.Count টি ম্যাপিং।</p>
    </div>

    <div class="card" style="margin-bottom: 16px">
        <form asp-action="AddScenario" method="post" class="row wrap" style="gap: 10px; align-items: flex-end">
            @Html.AntiForgeryToken()
            <div>
                <label class="form-label" data-bn="কীওয়ার্ড" data-en="Keyword">কীওয়ার্ড</label>
                <input class="input" name="keyword" placeholder="বেতন বাকি" required />
            </div>
            <div>
                <label class="form-label" data-bn="Section ID" data-en="Section ID">Section ID</label>
                <input class="input" name="sectionId" type="number" min="1" placeholder="12345" required />
            </div>
            <div>
                <label class="form-label" data-bn="নোট (ঐচ্ছিক)" data-en="Notes (optional)">নোট (ঐচ্ছিক)</label>
                <input class="input" name="notes" placeholder="unpaid wages" />
            </div>
            <button class="btn btn-primary btn-sm" type="submit"><i data-lucide="plus"></i> <span data-bn="ম্যাপিং যোগ করুন" data-en="Add mapping">ম্যাপিং যোগ করুন</span></button>
        </form>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr>
                    <th data-bn="কীওয়ার্ড" data-en="Keyword">কীওয়ার্ড</th>
                    <th data-bn="আইন" data-en="Act">আইন</th>
                    <th data-bn="ধারা" data-en="Section">ধারা</th>
                    <th data-bn="নোট" data-en="Notes">নোট</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var m in Model.Mappings)
                {
                    <tr>
                        <td><b>@m.Keyword</b></td>
                        <td>@m.ActTitle</td>
                        <td class="mono">@(string.IsNullOrEmpty(m.SectionNumber) ? "#" : "ধারা " + m.SectionNumber)</td>
                        <td class="muted">@m.Notes</td>
                        <td>
                            <form asp-action="DeleteScenario" method="post" style="display:inline"
                                  data-confirm="Delete this mapping?">
                                @Html.AntiForgeryToken()
                                <input type="hidden" name="mappingId" value="@m.MappingId" />
                                <button class="icon-btn" type="submit" aria-label="Delete"><i data-lucide="trash-2"></i></button>
                            </form>
                        </td>
                    </tr>
                }
                @if (!Model.Mappings.Any())
                {
                    <tr>
                        <td colspan="5" style="text-align:center; padding: 24px" class="muted"
                            data-bn="এখনো কোনো ম্যাপিং নেই" data-en="No mappings yet">এখনো কোনো ম্যাপিং নেই</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
</main>
```

- [ ] **Step 4: Add nav links in `_Layout.cshtml`**

Desktop nav — in the `@if (isAdmin)` block (~line 77-91), insert after the Lawyers link:

```cshtml
                    <a asp-controller="Admin" asp-action="Acts">Acts</a>
```

Mobile drawer — in the admin `@if (isAdmin)` block (~line 177-184), insert after the Lawyers entry:

```cshtml
                <a asp-controller="Admin" asp-action="Acts"><i data-lucide="library"></i> Acts</a>
```

- [ ] **Step 5: Build**

Run: `dotnet build src/MuktoAin.Web/MuktoAin.Web.csproj`
Expected: Build succeeded — Razor views compile (this catches tag-helper and model-type errors in the new views).

- [ ] **Step 6: Manual browser E2E** (requires T-3.1/T-3.2 registered per Task 2 Step 6)

Run: `dotnet run --project src/MuktoAin.Web` and, logged in as an Admin user, verify:
1. `/Admin/Acts` lists acts from the DB with correct counts; filter by `q=labour` and `year=2006`; Prev/Next paging works; empty query state shows the bilingual empty row.
2. Click an act → `/Admin/ActDetail/{id}` lists its sections (preview text, chunk/mapping counts).
3. In Edit metadata: change year to 2099 → save → bilingual error toast appears, nothing saved. Set a valid year + check Repealed → save → bilingual success toast; reload shows updated values; `ACT` row updated in DB.
4. `/Admin/Scenarios`: add a mapping with a valid Section ID → success toast, row appears; add with empty keyword → bilingual error toast; delete → success toast, row gone.
5. Toggle the language switcher on all three pages — every new string swaps (no static leftovers).

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Flip the E-3.2 checkbox (line ~139) from `- [ ]` to `- [x]`, wrap the whole line in `~~strikethrough~~`, and append a one-line completion summary (actions wired, views created, E2E passed). Do NOT commit.

---

### Task 5: E-3.3 — Admin Users views: verify real-data wiring, i18n polish, guardrail tests

`/Admin/Users` is **already wired** to the real `IUserManagementService` (S-3.6): `AdminController.Users()` (AdminController.cs:176) lists via `GetAllUsersAsync()` and `Suspend()` (AdminController.cs:201) calls `SetAccountStatusAsync` with the admin-protection guardrails inside the service. This task verifies the real-data path, closes the i18n gap (the current view has zero `data-bn`/`data-en` pairs), adds an empty state, and locks the guardrail behavior in with tests.

**Files:**
- Modify: `src/MuktoAin.Web/Views/Admin/Users.cshtml` (bilingual + empty state)
- Create: `tests/MuktoAin.UnitTests/Controllers/AdminUserActionsTests.cs`

**Interfaces:**
- Consumes: `IUserManagementService.GetAllUsersAsync()` / `SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId)` — existing, unchanged.

- [ ] **Step 1: Write the controller tests**

Create `tests/MuktoAin.UnitTests/Controllers/AdminUserActionsTests.cs` (same ctor-helper pattern as `AdminActsTests` — repeat here since tasks may be executed independently):

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.UnitTests.Controllers;

// E-3.3: the /Admin/Users controller path against the real (S-3.6) service
// contract -- list mapping, suspend/unsuspend TempData contracts, and the
// admin-protection error path. IUserManagementService is Moq'd; the guardrail
// logic itself is covered by UserManagementServiceTests.
public class AdminUserActionsTests
{
    private readonly Mock<IUserManagementService> _users = new();

    private AdminController BuildController(int actingAdminId = 1)
    {
        var userManager = new UserManager<User>(
            new Mock<IUserStore<User>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var geminiClient = new GeminiClient(
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions
            {
                ApiKeys = new[] { "test-key-1" },
                GenerationModel = "gemini-2.5-flash",
                EmbeddingModel = "gemini-embedding-001"
            }),
            Mock.Of<IHttpClientFactory>(),
            new Polly.ResiliencePipelineBuilder<HttpResponseMessage>().Build());

        var controller = new AdminController(
            Mock.Of<ILogger<AdminController>>(),
            Mock.Of<AppDbContext>(),
            new ConfigurationBuilder().Build(),
            _users.Object,
            Mock.Of<LawyerVerificationService>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            userManager,
            Mock.Of<IActRepository>(),
            Mock.Of<IActSectionRepository>(),
            Mock.Of<IActSectionChunkRepository>(),
            Mock.Of<IScenarioMappingRepository>(),
            Mock.Of<IRepository<CaseCategory>>(),
            Mock.Of<IRepository<AiLog>>(),
            Mock.Of<PaymentService>(),
            geminiClient,
            Mock.Of<IActsManagementService>(),
            Mock.Of<IScenarioMappingService>());

        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, actingAdminId.ToString())
        }, "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    [Fact]
    public async Task Users_MapsServiceDtosToRowViewModels_AndAppliesRoleFilter()
    {
        _users.Setup(s => s.GetAllUsersAsync()).ReturnsAsync(new[]
        {
            new UserListDto(1, "Admin One", "admin@mukto.ain", "Admin", "Active"),
            new UserListDto(2, "Citizen Two", "c2@mukto.ain", "Citizen", "Active"),
            new UserListDto(3, "Lawyer Three", "lawyer@mukto.ain", "Lawyer", "Suspended")
        });

        var filtered = await BuildController().Users("Lawyer");
        var all = await BuildController().Users(null);

        var filteredVm = Assert.IsType<AdminUsersViewModel>(Assert.IsType<ViewResult>(filtered).Model);
        var row = Assert.Single(filteredVm.Users);
        Assert.Equal("Lawyer Three", row.FullName);
        Assert.Equal("Lawyer", filteredVm.RoleFilter);

        var allVm = Assert.IsType<AdminUsersViewModel>(Assert.IsType<ViewResult>(all).Model);
        Assert.Equal(3, allVm.Users.Count);
    }

    [Fact]
    public async Task Suspend_AdminProtectedUser_ServiceRejects_ShowsBilingualError()
    {
        _users.Setup(s => s.SetAccountStatusAsync(9, AccountStatus.Suspended, 1)).ReturnsAsync(false);

        var controller = BuildController();
        var result = await controller.Suspend(9, suspend: true);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Users", redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.NotNull(controller.TempData["ErrorEn"]);
        Assert.Null(controller.TempData["Success"]);
        _users.Verify(s => s.SetAccountStatusAsync(9, AccountStatus.Suspended, 1), Times.Once);
    }

    [Fact]
    public async Task Suspend_Activate_PassesActiveStatusAndShowsSuccess()
    {
        _users.Setup(s => s.SetAccountStatusAsync(3, AccountStatus.Active, 1)).ReturnsAsync(true);

        var controller = BuildController();
        var result = await controller.Suspend(3, suspend: false);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Success"]);
        Assert.NotNull(controller.TempData["SuccessEn"]);
        _users.Verify(s => s.SetAccountStatusAsync(3, AccountStatus.Active, 1), Times.Once);
    }
}
```

Note: if Task 3 already removed the three repository ctor params from `AdminController`, drop the corresponding `Mock.Of<IActRepository>()` / `Mock.Of<IActSectionRepository>()` / `Mock.Of<IScenarioMappingRepository>()` lines here too.

- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminUserActionsTests"`
Expected: PASS — these are regression tests over already-implemented controller logic (characterization tests). If any FAIL, the controller regressed — fix the controller, not the test.

- [ ] **Step 3: Bilingual rewrite of `Views/Admin/Users.cshtml`**

Replace the whole file:

```cshtml
@model AdminUsersViewModel
@{
    ViewData["Title"] = "Users — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard" data-bn="ড্যাশবোর্ড" data-en="Dashboard">ড্যাশবোর্ড</a>
        <span class="sep">/</span>
        <span data-bn="ব্যবহারকারী" data-en="Users">ব্যবহারকারী</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="users"></i> FR-18</span>
        <h1 class="page-title" data-bn="ব্যবহারকারী ব্যবস্থাপনা" data-en="User Management">ব্যবহারকারী ব্যবস্থাপনা</h1>
        <p class="page-sub"
           data-bn="অ্যাকাউন্ট স্থগিত বা পুনরায় চালু করুন। অ্যাডমিন অ্যাকাউন্ট পরিবর্তন থেকে সুরক্ষিত।"
           data-en="Suspend or restore accounts. Admin accounts are protected from modification.">
           অ্যাকাউন্ট স্থগিত বা পুনরায় চালু করুন। অ্যাডমিন অ্যাকাউন্ট সুরক্ষিত।</p>
    </div>

    <div class="chip-row" style="margin-bottom: 16px">
        @foreach (var f in new[] { "All", "Citizen", "Lawyer", "Admin" })
        {
            var bn = f switch
            {
                "All" => "সব",
                "Citizen" => "নাগরিক",
                "Lawyer" => "আইনজীবী",
                _ => "অ্যাডমিন"
            };
            <a class="chip chip-sm @(Model.RoleFilter == f ? "active" : "")"
               asp-action="Users" asp-route-role="@f"
               data-bn="@bn" data-en="@f">@bn</a>
        }
    </div>

    <div class="card">
        <table>
            <thead>
                <tr>
                    <th data-bn="নাম" data-en="Name">নাম</th>
                    <th data-bn="ইমেইল" data-en="Email">ইমেইল</th>
                    <th data-bn="ভূমিকা" data-en="Role">ভূমিকা</th>
                    <th data-bn="অবস্থা" data-en="Status">অবস্থা</th>
                    <th data-bn="কার্যক্রম" data-en="Actions">কার্যক্রম</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var u in Model.Users)
                {
                    <tr>
                        <td>@u.FullName</td>
                        <td class="mono">@u.Email</td>
                        <td><span class="badge badge-neutral" data-bn="@u.Role" data-en="@u.Role">@u.Role</span></td>
                        <td>
                            <span class="badge badge-@(u.Status == "Suspended" ? "rejected" : "final")"
                                  data-bn="@(u.Status == "Suspended" ? "স্থগিত" : "সক্রিয়")"
                                  data-en="@u.Status">@(u.Status == "Suspended" ? "স্থগিত" : "সক্রিয়")</span>
                        </td>
                        <td>
                            @if (u.Role == "Admin")
                            {
                                <span class="badge badge-neutral"
                                      data-bn="সুরক্ষিত" data-en="Protected"
                                      data-bn-title="অ্যাডমিন অ্যাকাউন্ট পরিবর্তনযোগ্য নয়"
                                      data-en-title="Admin accounts cannot be modified">সুরক্ষিত</span>
                            }
                            else if (u.Status == "Suspended")
                            {
                                <form asp-action="Suspend" method="post" style="display:inline">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="userId" value="@u.UserId" />
                                    <input type="hidden" name="suspend" value="false" />
                                    <button class="btn btn-outline btn-sm" type="submit"
                                            data-bn="পুনরায় চালু" data-en="Activate">পুনরায় চালু</button>
                                </form>
                            }
                            else
                            {
                                <form asp-action="Suspend" method="post" style="display:inline"
                                      data-confirm="Suspend this account? Login will be blocked.">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="userId" value="@u.UserId" />
                                    <input type="hidden" name="suspend" value="true" />
                                    <button class="btn btn-danger-outline btn-sm" type="submit"
                                            data-bn="স্থগিত" data-en="Suspend">স্থগিত</button>
                                </form>
                            }
                        </td>
                    </tr>
                }
                @if (!Model.Users.Any())
                {
                    <tr>
                        <td colspan="5" style="text-align:center; padding: 24px" class="muted"
                            data-bn="কোনো ব্যবহারকারী পাওয়া যায়নি" data-en="No users found">কোনো ব্যবহারকারী পাওয়া যায়নি</td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
</main>
```

- [ ] **Step 4: Build**

Run: `dotnet build src/MuktoAin.Web/MuktoAin.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Manual browser E2E**

Run: `dotnet run --project src/MuktoAin.Web` as Admin:
1. `/Admin/Users` shows the real `USER` table rows (not sample data) — cross-check row count against `SELECT COUNT(*) FROM [dbo].[USER]` in SSMS.
2. Suspend a test Citizen account → success toast (follows current language); the account can no longer log in (security-stamp rotation, S-3.6).
3. Attempt suspension on an Admin row → no Suspend button is rendered (Protected badge instead); service-level guard already prevents it server-side.
4. Language toggle swaps all strings on the page, including the filter chips and the Protected tooltip.

- [ ] **Step 6: Record completion in plans/Dependency_plan.md**

Flip the E-3.3 checkbox (line ~140) to `- [x]`, wrap in `~~…~~`, append summary. Do NOT commit.

---

### Task 6: E-3.4 (part 1) — Analytics view: replace hardcoded mock KPIs with real data + scrub mock VM defaults

`Views/Admin/Analytics.cshtml` still renders mock-era numbers hard-coded into the markup (`১,২৮৪`, `৩.৪ ঘণ্টা`, `৩৪২`, `১,৮২০ ms`, fixed funnel percentages, fixed latency table) even though its `AdminController.Analytics()` already computes a real `AdminDashboardViewModel`. This task makes the view render the model. It also scrubs the mock-era default values on `AdminDashboardViewModel` (`TotalUsersCount = 418`, `TotalLawyersCount = 34`, `TotalActsCount = 1484`, fake health-status strings).

**Files:**
- Modify: `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs` (AdminDashboardViewModel: new fields + default scrub)
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (BuildAdminDashboardViewModelAsync: populate new fields)
- Modify: `src/MuktoAin.Web/Views/Admin/Analytics.cshtml` (3 blocks)
- Create: `tests/MuktoAin.UnitTests/Controllers/AdminAnalyticsRealDataTests.cs`

**Interfaces:**
- Consumes: existing `IRepository<AiLog>` / `AppDbContext` already injected into `AdminController`.
- Produces: `AdminDashboardViewModel.DocumentsCreated`, `.ApprovedDocuments`, `.AiLatencyStats` (consumed by the new Analytics markup; Dashboard view is unaffected).

- [ ] **Step 1: Scrub mock defaults + add fields in `AdminDashboardViewModel`**

In `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs`, replace these members:

```csharp
    // System Infrastructure & Capacity
    public int TotalUsersCount { get; set; } = 418;
    public int TotalLawyersCount { get; set; } = 34;
    public int TotalActsCount { get; set; } = 1484;
    public bool IsDatabaseHealthy { get; set; } = true;
    public bool IsVectorDbHealthy { get; set; } = true;
    public bool IsAiServiceHealthy { get; set; } = true;
    public string OverallHealthBadgeText { get; set; } = "সকল সার্ভিস সচল (Operational)";
    public string OverallHealthBadgeClass { get; set; } = "badge-success";
    public string DatabaseStatus { get; set; } = "Connected (Microsoft SQL Server)";
    public string VectorDbStatus { get; set; } = "Operational (Qdrant Vector Store · 1,484 Acts)";
    public string AiServiceStatus { get; set; } = "Healthy (Gemini 2.5 Flash API · Circuit Breaker Closed)";
```

with (mock-era defaults removed — E-3.4; `BuildAdminDashboardViewModelAsync` always sets every one of these before the view renders):

```csharp
    // System Infrastructure & Capacity. No mock-era defaults (E-3.4) --
    // BuildAdminDashboardViewModelAsync assigns every field on each request.
    public int TotalUsersCount { get; set; }
    public int TotalLawyersCount { get; set; }
    public int TotalActsCount { get; set; }
    public bool IsDatabaseHealthy { get; set; }
    public bool IsVectorDbHealthy { get; set; }
    public bool IsAiServiceHealthy { get; set; }
    public string OverallHealthBadgeText { get; set; } = "";
    public string OverallHealthBadgeClass { get; set; } = "badge-neutral";
    public string DatabaseStatus { get; set; } = "";
    public string VectorDbStatus { get; set; } = "";
    public string AiServiceStatus { get; set; } = "";

    // FR-16 funnel inputs (real counts, E-3.4)
    public int DocumentsCreated { get; set; }
    public int ApprovedDocuments { get; set; }
    public List<AiLatencyStatViewModel> AiLatencyStats { get; set; } = new();
```

and append after `DistrictStatViewModel`:

```csharp
public class AiLatencyStatViewModel
{
    public string RequestType { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public int AvgLatencyMs { get; set; }
}
```

- [ ] **Step 2: Populate the new fields in `BuildAdminDashboardViewModelAsync`**

In `AdminController.cs`, inside the `try` block, right after `model.AiCallsToday = aiLogsToday.Count;` insert:

```csharp
            model.DocumentsCreated = documents.Count;
            model.ApprovedDocuments = documents.Count(d => d.Status == DocumentStatus.Approved);
            // FR-16 observability: honest per-request-type latency from today's
            // AI_LOG rows (replaces the mock "avg latency" table in Analytics).
            model.AiLatencyStats = aiLogsToday
                .GroupBy(l => l.RequestType)
                .Select(g => new AiLatencyStatViewModel
                {
                    RequestType = g.Key.ToString(),
                    CallCount = g.Count(),
                    AvgLatencyMs = (int)Math.Round(g.Average(l => (double)l.LatencyMs))
                })
                .OrderBy(s => s.RequestType)
                .ToList();
```

- [ ] **Step 3: Write the failing test**

Create `tests/MuktoAin.UnitTests/Controllers/AdminAnalyticsRealDataTests.cs`. This uses the real InMemory `AppDbContext` (`TestDbContextFactory`, `tests/MuktoAin.UnitTests/Repositories/`) because `BuildAdminDashboardViewModelAsync` reads `_dbContext` directly. With an unconfigured `IConfiguration`, the SQL/Qdrant/Gemini health probes degrade gracefully (existing try/catch paths) instead of throwing:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.Data;
using MuktoAin.UnitTests.Repositories;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.UnitTests.Controllers;

// E-3.4: Analytics must render aggregates from the live database, not the
// mock-era hardcoded KPIs. Verifies the new DocumentsCreated / ApprovedDocuments
// / AiLatencyStats fields are computed from seeded real rows.
public class AdminAnalyticsRealDataTests
{
    private static AdminController BuildController(AppDbContext dbContext)
    {
        var userManager = new Microsoft.AspNetCore.Identity.UserManager<User>(
            new Mock<Microsoft.AspNetCore.Identity.IUserStore<User>>().Object,
            null!, null!, null!, null!, null!, null!, null!, null!);

        var geminiClient = new GeminiClient(
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions
            {
                ApiKeys = new[] { "test-key-1" },
                GenerationModel = "gemini-2.5-flash",
                EmbeddingModel = "gemini-embedding-001"
            }),
            Mock.Of<IHttpClientFactory>(),
            new Polly.ResiliencePipelineBuilder<HttpResponseMessage>().Build());

        return new AdminController(
            Mock.Of<ILogger<AdminController>>(),
            dbContext,
            new ConfigurationBuilder().Build(),
            Mock.Of<Application.Services.IUserManagementService>(),
            Mock.Of<Application.Services.LawyerVerificationService>(),
            Mock.Of<IRepository<LawyerProfile>>(),
            userManager,
            Mock.Of<Domain.Interfaces.Repositories.IActRepository>(),
            Mock.Of<Domain.Interfaces.Repositories.IActSectionRepository>(),
            Mock.Of<Domain.Interfaces.Repositories.IActSectionChunkRepository>(),
            Mock.Of<Domain.Interfaces.Repositories.IScenarioMappingRepository>(),
            Mock.Of<IRepository<CaseCategory>>(),
            Mock.Of<IRepository<AiLog>>(),
            Mock.Of<Application.Services.PaymentService>(),
            geminiClient,
            Mock.Of<Application.Services.IActsManagementService>(),
            Mock.Of<Application.Services.IScenarioMappingService>());
    }

    [Fact]
    public async Task Analytics_ComputesFunnelAndLatencyFromRealRows()
    {
        var db = TestDbContextFactory.Create();
        var today = DateTime.UtcNow;
        db.AiLogs.AddRange(
            new AiLog
            {
                RequestType = AiRequestType.LawIdentification, LatencyMs = 1000, TokensUsed = 10,
                ModelUsed = "gemini-2.5-flash", PromptText = "p1", ResponseText = "r1", CreatedAt = today
            },
            new AiLog
            {
                RequestType = AiRequestType.LawIdentification, LatencyMs = 3000, TokensUsed = 20,
                ModelUsed = "gemini-2.5-flash", PromptText = "p2", ResponseText = "r2", CreatedAt = today
            });
        db.Cases.Add(new Case { Title = "t", Description = "d", TrackingCode = "MKT-2026-0001", CreatedAt = today });
        db.GeneratedDocuments.AddRange(
            new GeneratedDocument { CaseId = 1, Status = DocumentStatus.Approved, ContentDraft = "d1" },
            new GeneratedDocument { CaseId = 1, Status = DocumentStatus.UnderReview, ContentDraft = "d2" });
        db.SaveChanges();

        var vm = (Assert.IsType<ViewResult>(await BuildController(db).Analytics()).Model)
            as MuktoAin.Web.ViewModels.AdminDashboardViewModel;

        Assert.NotNull(vm);
        Assert.Equal(1, vm!.TotalCases);
        Assert.Equal(2, vm.DocumentsCreated);
        Assert.Equal(1, vm.ApprovedDocuments);
        var stat = Assert.Single(vm.AiLatencyStats);
        Assert.Equal(nameof(AiRequestType.LawIdentification), stat.RequestType);
        Assert.Equal(2, stat.CallCount);
        Assert.Equal(2000, stat.AvgLatencyMs);
        Assert.Equal(0, vm.TotalUsersCount); // mock default 418 scrubbed
    }
}
```

Note: adjust the seeded entity initializers to the exact required properties of `AiLog` / `Case` / `GeneratedDocument` if the compiler flags missing required members — keep the asserted values (2 AI logs @1000/3000ms, 1 case, 1 approved + 1 under-review document) identical.

- [ ] **Step 4: Run to verify RED**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminAnalyticsRealDataTests"`
Expected: FAIL — `AdminDashboardViewModel` has no `DocumentsCreated`/`AiLatencyStats` members yet (compile error = RED).

- [ ] **Step 5: Apply the Step 1 + Step 2 edits, run to verify GREEN**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~AdminAnalyticsRealDataTests"`
Expected: PASS.
Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: full suite PASS (Dashboard/HealthStatus behavior unchanged).

- [ ] **Step 6: Rewrite the three mock blocks in `Views/Admin/Analytics.cshtml`**

(a) Replace the KPI grid (current lines 23-44) with real-model cards:

```cshtml
    <div class="grid grid-4" style="margin-bottom: 24px;">
        <div class="card kpi">
            <span class="k-label" data-bn="সর্বমোট মামলা" data-en="Total Cases"><i data-lucide="folder-check" aria-hidden="true"></i>সর্বমোট মামলা</span>
            <span class="k-num">@Model.TotalCases.ToString("N0")</span>
            <span class="k-sub" data-bn="+@Model.CasesThisWeek এই সপ্তাহে নতুন" data-en="+@Model.CasesThisWeek new this week">+@Model.CasesThisWeek এই সপ্তাহে নতুন</span>
        </div>
        <div class="card kpi">
            <span class="k-label" data-bn="তৈরি খসড়া দলিল" data-en="Drafts Generated"><i data-lucide="file-text" aria-hidden="true"></i>তৈরি খসড়া দলিল</span>
            <span class="k-num">@Model.DocumentsCreated.ToString("N0")</span>
            <span class="k-sub" data-bn="রিভিউ অপেক্ষমাণ: @Model.PendingReviews" data-en="Awaiting review: @Model.PendingReviews">রিভিউ অপেক্ষমাণ: @Model.PendingReviews</span>
        </div>
        <div class="card kpi">
            <span class="k-label" data-bn="AI কল ভলিউম (আজ)" data-en="AI Calls Today"><i data-lucide="cpu" aria-hidden="true"></i>AI কল ভলিউম (আজ)</span>
            <span class="k-num">@Model.AiCallsToday.ToString("N0")</span>
            <span class="k-sub" data-bn="ব্যর্থতার হার: @Model.AiFailureRate%" data-en="Failure rate: @Model.AiFailureRate%">ব্যর্থতার হার: @Model.AiFailureRate%</span>
        </div>
        <div class="card kpi">
            <span class="k-label" data-bn="আইনজীবী-অনুমোদিত দলিল" data-en="Lawyer-Approved Documents"><i data-lucide="check-circle" aria-hidden="true"></i>আইনজীবী-অনুমোদিত দলিল</span>
            <span class="k-num">@Model.ApprovedDocuments.ToString("N0")</span>
            <span class="k-sub" data-bn="আইনজীবী-যাচাই গেট সক্রিয়" data-en="Lawyer-review gate enforced">আইনজীবী-যাচাই গেট সক্রিয়</span>
        </div>
    </div>
```

(b) Replace the fixed 4-step funnel block (current lines 76-105) with a 3-step funnel computed from real counts (PDF-download data is not tracked anywhere in the schema — that step is dropped rather than fabricated):

```cshtml
@{
    var funnelTotal = Math.Max(Model.TotalCases, 1);
    var draftPct = Math.Round(Model.DocumentsCreated * 100.0 / funnelTotal, 1);
    var approvedPct = Math.Round(Model.ApprovedDocuments * 100.0 / funnelTotal, 1);
}
<div style="display:flex; flex-direction:column; gap:12px; margin-top:8px;">
    <div>
        <div style="display:flex; justify-content:space-between; font-size:13px; margin-bottom:4px;">
            <span data-bn="১. আইনি সমস্যা ইনটেক ও অধিকার বিশ্লেষণ" data-en="1. Case Intake & Rights Analysis">১. আইনি সমস্যা ইনটেক ও অধিকার বিশ্লেষণ</span>
            <strong>@Model.TotalCases.ToString("N0") (100%)</strong>
        </div>
        <div class="bar-track"><div class="bar-fill primary" style="--w:100%"></div></div>
    </div>
    <div>
        <div style="display:flex; justify-content:space-between; font-size:13px; margin-bottom:4px;">
            <span data-bn="২. স্বয়ংক্রিয় আইনি ড্রাফট প্রস্তুত" data-en="2. Automated Legal Draft Prepared">২. স্বয়ংক্রিয় আইনি ড্রাফট প্রস্তুত</span>
            <strong>@draftPct% (@Model.DocumentsCreated.ToString("N0"))</strong>
        </div>
        <div class="bar-track"><div class="bar-fill gold" style="--w:@(Math.Min(draftPct, 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture))%"></div></div>
    </div>
    <div>
        <div style="display:flex; justify-content:space-between; font-size:13px; margin-bottom:4px;">
            <span data-bn="৩. আইনজীবী দ্বারা যাচাইকৃত ও অনুমোদিত" data-en="3. Lawyer Verified & Approved">৩. আইনজীবী দ্বারা যাচাইকৃত ও অনুমোদিত</span>
            <strong>@approvedPct% (@Model.ApprovedDocuments.ToString("N0"))</strong>
        </div>
        <div class="bar-track"><div class="bar-fill green" style="--w:@(Math.Min(approvedPct, 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture))%"></div></div>
    </div>
</div>
```

(c) Replace the hardcoded 4-row service-latency table body (current `<tbody>` at lines 145-170) with live per-request-type rows:

```cshtml
                    <tbody>
                        @foreach (var s in Model.AiLatencyStats)
                        {
                            var labelBn = s.RequestType switch
                            {
                                "LawIdentification" => "আইন শনাক্তকরণ (FR-3)",
                                "RightsExplanation" => "অধিকার ব্যাখ্যা (FR-4)",
                                "Drafting" => "দলিল ড্রাফটিং (FR-5)",
                                _ => s.RequestType
                            };
                            <tr>
                                <td><strong data-bn="@labelBn" data-en="@s.RequestType">@labelBn</strong></td>
                                <td><code>gemini-2.5-flash</code></td>
                                <td><span class="badge badge-success" data-bn="অনলাইন" data-en="Online">অনলাইন</span></td>
                                <td>@s.AvgLatencyMs ms · @s.CallCount calls</td>
                            </tr>
                        }
                        @if (!Model.AiLatencyStats.Any())
                        {
                            <tr>
                                <td colspan="4" class="muted" style="text-align:center"
                                    data-bn="আজ কোনো AI কল রেকর্ড হয়নি" data-en="No AI calls recorded today">আজ কোনো AI কল রেকর্ড হয়নি</td>
                            </tr>
                        }
                    </tbody>
```

- [ ] **Step 7: Build + E2E**

Run: `dotnet build src/MuktoAin.Web/MuktoAin.Web.csproj` — Expected: Build succeeded.
Run: `dotnet run --project src/MuktoAin.Web` as Admin, open `/Admin/Analytics`:
1. All KPI numbers match the Dashboard (`/Admin/Dashboard`) values and the SSMS row counts — none of the old hardcoded figures (`১,২৮৪`, `৩৪২`, `১,৮২০ ms`) appear anywhere (verify with browser Ctrl+F).
2. The funnel percentages change when cases/documents are added.
3. The Observability table lists only request types actually present in today's AI_LOG.
4. Language toggle works on all changed strings.

- [ ] **Step 8: Record completion in plans/Dependency_plan.md**

Do NOT commit. Note the Analytics de-mock work under the E-3.4 line (checkbox flips in Task 7).

---

### Task 7: E-3.4 (part 2) — mock sweep, dead-code deletion, full endpoint verification

**Files:**
- Delete: `src/MuktoAin.Web/MockData.cs`
- Verify-only: all controllers/views

**Interfaces:**
- Consumes: everything wired in Tasks 2–6 plus the already-real endpoints.

- [ ] **Step 1: Delete `MockData.cs`**

`src/MuktoAin.Web/MockData.cs` is verified dead code — `rg -n "MockData|SampleCase|SampleSearch|CategoriesDetailed|SampleLawyerReview|SampleAnalytics|SampleCaseResult" src/ tests/` matches **only** inside `MockData.cs` itself (no controller, view, or test references it). Delete the file.
Run: `dotnet build src/MuktoAin.Web/MuktoAin.Web.csproj` — Expected: Build succeeded (proves nothing referenced it).

- [ ] **Step 2: Mock/TODO sweep across the Web project**

Run each and confirm **0 hits** (or only benign CSS `::placeholder` matches):

```
rg -n "MockData|SampleCase|SampleSearch|CategoriesDetailed|SampleLawyerReview|SampleAnalytics" src/ tests/
rg -n "TODO|HACK|FIXME|mock data" src/MuktoAin.Web/Controllers/
rg -n "totalCount = 418|TotalActsCount { get; set; } = 1484|= 34;" src/MuktoAin.Web/ViewModels/
```

Expected: first command returns nothing; second returns nothing; third returns nothing after Task 6. If any hit appears, wire it to the real service in this task (the sweep exists to catch exactly that).

- [ ] **Step 3: Full unit-test suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS — entire suite green (no regressions from the ViewModel default scrub).

- [ ] **Step 4: Endpoint verification checklist**

Run the app (`dotnet run --project src/MuktoAin.Web`) as Admin and walk **every** row. "Data source" = where the rendered values come from; every row must show DB-backed values (SSMS cross-check where noted).

| Endpoint | Method | Data source (after this plan) | Verify |
|---|---|---|---|
| `/Admin/Dashboard` | GET | `BuildAdminDashboardViewModelAsync` (live EF + health checks) | KPIs match SSMS counts; health badges reflect real config |
| `/Admin/Analytics` | GET | `AdminDashboardViewModel` real aggregates (Task 6) | No hardcoded `১,২৮৪`/`৩৪২`/`১,৮২০` remain |
| `/Admin/HealthStatus` | GET (JSON) | Live SQL / Qdrant / Gemini probes | Toggle-valid values; no `418` |
| `/Admin/EmbeddingProgress` | GET (JSON) | `ACT_SECTION_CHUNK` + `EmbeddingProgressState` | Percent matches SSMS `VectorId IS NOT NULL` ratio |
| `/Admin/GeminiKeyStatus` | GET (JSON) | `GeminiClient.Snapshot()` | Key list matches appsettings |
| `/Admin/Users` | GET | `IUserManagementService.GetAllUsersAsync()` | Row count = `SELECT COUNT(*) FROM [dbo].[USER]` |
| `/Admin/Suspend` | POST | `IUserManagementService.SetAccountStatusAsync` | Suspend/activate + admin-protection toast (Task 5) |
| `/Admin/Lawyers` | GET | `IRepository<LawyerProfile>` + Identity | Rows match `LAWYER_PROFILE` |
| `/Admin/VerifyLawyer` | POST | `LawyerVerificationService.VerifyAsync` | Status change persists |
| `/Admin/Acts` | GET | `IActsManagementService.GetActsPagedAsync` (Task 2/4) | Paging + q/year filter on real corpus |
| `/Admin/ActDetail/{id}` | GET | `IActsManagementService.GetActDetailAsync` | Sections + chunk counts match DB |
| `/Admin/EditAct` | POST | `IActsManagementService.UpdateActMetadataAsync` | Metadata saved; re-index scheduled |
| `/Admin/Corpus` | GET | EF aggregates on `ACT_SECTION_CHUNK` | Totals match SSMS |
| `/Admin/Scenarios` | GET | `IScenarioMappingService.GetAllMappingsAsync` (Task 3) | Rows match `SCENARIO_MAPPING` |
| `/Admin/AddScenario` | POST | `IScenarioMappingService.AddMappingAsync` | Row inserted; bilingual toasts |
| `/Admin/DeleteScenario` | POST | `IScenarioMappingService.DeleteMappingAsync` | Row deleted; bilingual toasts |
| `/Admin/Categories` | GET | `IRepository<CaseCategory>` | 4 seeded categories render |
| `/Admin/AiLogs` | GET | `IRepository<AiLog>` | Rows match `AI_LOG` |
| `/Admin/Transactions` | GET | `PaymentService.GetOrdersAsync` / `GetPendingPayoutsAsync` | Ledger rows render |
| `/Admin/RefundOrder`, `/ApprovePayout`, `/MarkOrderPaid` | POST | `PaymentService` | Sandbox state transitions persist |
| `/` (Home), `/Chat`, `/Case/*`, `/Document/*`, `/Lawyer/*`, `/Search`, `/Category/*`, `/Account/*`, `/Payment/*` | mixed | Already wired to real services (CP1/CP2) | Spot-check one flow each: submit a case → AI result → document preview |

- [ ] **Step 5: Final build + full test run**

Run: `dotnet build MuktoAin.sln && dotnet test tests/MuktoAin.UnitTests`
Expected: Build succeeded; full test suite PASS.

- [ ] **Step 6: Record completion in plans/Dependency_plan.md**

Flip the E-3.4 checkbox (line ~141) from `- [ ]` to `- [x]`, wrap the whole line in `~~strikethrough~~`, append the sweep results (grep hit counts = 0) and the checklist completion note. Do NOT commit — Shads reviews and commits.

Note: **E-3.5** (Checkpoint 3 exit gate, line ~142) is gated on E-3.1–E-3.4 plus the other teammates' checkpoints — do not flip it from this plan unless every dependency is verifiably complete.

---

## Assumptions & Open Questions

1. **T-3.1/T-3.2 signatures** (`docs/superpowers/plans/2026-09-12-acts-scenario-management.md` was not present when this plan was written): the contract in the *Consumed Interfaces* section is Erin's proposal. Hrittika's landed signatures win; Task 1 Step 1 is the reconciliation point.
2. **Year floor 1700** in `EditAct` validation is chosen conservatively (pre-1900 `PublicationDate` is raw text per `Act.cs`); adjust if Hrittika's service enforces a different bound — the controller test asserts only the *shape* (reject → no service call), so the bound can move freely.
3. **"Avg lawyer review time" and "PDF download" KPIs were dropped, not faked** — no schema column tracks them today. If the team later adds review-turnaround tracking, a new KPI card can be added then.
4. **`data-confirm` strings remain English** (existing `main.js` behavior reads only `data-confirm`; making it bilingual is an R-ticket, not E-3.x scope).
5. **`TotalUsersCount/TotalActsCount` SQL-side override** in `BuildAdminDashboardViewModelAsync` still wins when the raw SQL health check succeeds — the scrubbed defaults only affect the pre-compute/failure path.
