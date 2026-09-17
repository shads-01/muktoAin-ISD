# Arpita Hardening: API Tests, Template Variants, Validation, Error Pages (A-3.7, A-3.9, A-3.11, A-3.12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden Arpita's slice of MuktoAin — a full HTTP-level integration test suite for the Document/Case/Payment endpoints (A-3.7), Bangla-only rendering variants for all 4 document templates (A-3.9), server+client input validation with length guards across all ViewModels (A-3.11), and bilingual user-friendly error handling for pages and fetch-based JSON APIs (A-3.12).

**Architecture:** A-3.7 builds a `WebApplicationFactory<Program>` host inside the existing `tests/MuktoAin.IntegrationTests` project (which today contains only mocked-pipeline smoke tests and does **not** reference `MuktoAin.Web`), swapping SQL Server for EF InMemory and stubbing `IAiService`/`IVectorStore`/`IEmbeddingService` so the RAG pipeline runs deterministically offline. A-3.9 adds a `RenderBanglaOnlyAsync` capability to each of the 4 template classes via a new `IBanglaDocumentVariant` interface, routed by `DocumentService` on `Case.Language == "bn"`. A-3.11 adds data annotations to the currently-unvalidated ViewModels (matching the `maxlength` attributes already on `Views/Case/Submit.cshtml` and DB column widths from `scripts/02_schema.sql`) plus XSS regression tests for `MarkdownText.ToHtml`. A-3.12 centralizes fetch-endpoint error JSON in a bilingual `ApiErrorDto` helper and integration-tests the existing error pages.

**Tech Stack:** .NET 8 / ASP.NET Core MVC, xUnit 2.9.3 + Moq 4.20.72, `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 (`WebApplicationFactory<Program>`), EF Core InMemory 8.0.11, Razor + jQuery unobtrusive validation.

**Spec:** `.agent/spec/requirements.md` (FR-8 case tracking, FR-11 disclaimer surfaces, FR-13/FR-14 lawyer review gate, FR-24 payments; NFR security/validation). Task registry: `plans/Dependency_plan.md` lines 287–292.

## Global Constraints

- **NO git commits, ever** — AGENTS.md §6: Shads is the sole committer. Where the writing-plans skill says "Commit", this plan instead says "Record completion in `plans/Dependency_plan.md`".
- Clean Architecture 4 projects (`src/MuktoAin.Domain/.Application/.Infrastructure/.Web`); tests in `tests/MuktoAin.UnitTests` (xUnit, references Web project already) and `tests/MuktoAin.IntegrationTests`.
- **BLOCKER for A-3.7 and A-3.11:** both are `[Blocked by: E-3.4]` (Erin's final controller↔service wiring, currently `- [ ]` at `plans/Dependency_plan.md:141`). **Executor MUST first confirm E-3.4 is done** (checkbox `[x]` in `plans/Dependency_plan.md`, or Erin's confirmation) **or explicitly coordinate with the human (Shads) before starting Tasks 1–4 and Task 6.** If E-3.4 is not done, Task 1's smoke test will reveal wiring gaps — treat failures as findings to report, not bugs to patch unilaterally.
- Mandatory 3-surface disclaimer policy: every generated document (Bangla-only variant included) MUST still stamp a legal disclaimer (Bangla-only variant stamps `Disclaimers.LegalBangla` only — no English interleaving).
- `DocumentService` invariant: `ContentDraft` is NEVER modified after generation; PDF bytes only for `DocumentStatus.Approved` (`GetPdfIfApprovedAsync`).
- Schema is authored in SSMS via `scripts/*.sql` — no EF migrations; test host must swap `AppDbContext` to InMemory in DI, never touch schema scripts.
- Bilingual copy convention: Bangla first, ` / `, then English (matches all existing `ErrorMessage = "বাংলা... / English..."` attributes).
- xUnit conventions: plain `[Fact]` methods with `MethodUnderTest_Scenario_ExpectedResult` naming (see `tests/MuktoAin.UnitTests/Services/GeneralDiaryTemplateTests.cs`); integration tests live under `tests/MuktoAin.IntegrationTests/` with one class per API area.
- Package versions must match the pinned set: xUnit 2.9.3, Moq 4.20.72, EFCore.InMemory 8.0.11, Test.Sdk 17.14.1.

---

### Task 1: A-3.7 — Integration Test Host (`WebApplicationFactory` + test auth + seeded data)

**Files:**
- Modify: `tests/MuktoAin.IntegrationTests/MuktoAin.IntegrationTests.csproj`
- Modify: `src/MuktoAin.Web/Program.cs` (append 1 line at end)
- Create: `tests/MuktoAin.IntegrationTests/Helpers/TestAuthHandler.cs`
- Create: `tests/MuktoAin.IntegrationTests/Helpers/MuktoAinWebApplicationFactory.cs`
- Create: `tests/MuktoAin.IntegrationTests/Helpers/TestData.cs`
- Create: `tests/MuktoAin.IntegrationTests/Api/WebHostSmokeTests.cs`

**Interfaces:**
- Consumes: `src/MuktoAin.Web/Program.cs` top-level statements (needs `public partial class Program {}` for `WebApplicationFactory<Program>` to find the entry point); DI registrations from `Program.cs` (`AppDbContext`, `MuktoAin.Domain.Interfaces.IAiService`, `IVectorStore`, `IEmbeddingService`).
- Produces: `MuktoAinWebApplicationFactory` (class fixture for Tasks 2–4); `TestAuthHandler` (scheme `"TestScheme"`, reads `X-Test-UserId` / `X-Test-Role` request headers); `TestData.SeedBaselineAsync(db)` returning `(District dhaka, CaseCategory labourCat)`; `TestData.NewUser(db, name, role)`; `TestData.NewCase(db, ...)` helpers used by every later test.

> **Note on `AppDbContext` config check:** `Program.cs:314-319` throws if `Gemini:EmbeddingOutputDimensionality` is set and mismatches `Qdrant:VectorSize`. The factory pins `Qdrant:VectorSize=3` and leaves `EmbeddingOutputDimensionality` unset, so the guard passes.

- [ ] **Step 1: Add Web test dependencies to `MuktoAin.IntegrationTests.csproj`**

The project currently references only Domain/Application/Infrastructure (verified — no `MuktoAin.Web` reference, no `Microsoft.AspNetCore.Mvc.Testing`). Add both:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MuktoAin.Application\MuktoAin.Application.csproj" />
    <ProjectReference Include="..\..\src\MuktoAin.Domain\MuktoAin.Domain.csproj" />
    <ProjectReference Include="..\..\src\MuktoAin.Infrastructure\MuktoAin.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\MuktoAin.Web\MuktoAin.Web.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Make `Program` visible to `WebApplicationFactory`**

Append to the very end of `src/MuktoAin.Web/Program.cs` (after `app.Run();`):

```csharp

// E-3.4 / A-3.7: WebApplicationFactory<Program> requires a compilable Program type.
// Top-level statements don't emit one unless a partial class is declared.
public partial class Program { }
```

- [ ] **Step 3: Write the test authentication handler**

`tests/MuktoAin.IntegrationTests/Helpers/TestAuthHandler.cs` — authenticates any request carrying `X-Test-UserId`. The role claim is included directly in the ticket, which makes `UserRoleClaimsTransformation` (src/MuktoAin.Web/Auth/UserRoleClaimsTransformation.cs:24) skip its DB lookup (it returns early when a role claim already exists), so tests are deterministic without Identity seeding:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MuktoAin.IntegrationTests.Helpers;

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string UserIdHeader = "X-Test-UserId";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers[UserIdHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, $"test-user-{userId}"),
        };

        var role = Request.Headers[RoleHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

- [ ] **Step 4: Write the application factory**

`tests/MuktoAin.IntegrationTests/Helpers/MuktoAinWebApplicationFactory.cs` — boots the REAL `Program` pipeline against EF InMemory, with the AI/vector seams stubbed. Startup seeders are safe for tests: `SeedDistricts`/`SeedCategories` read from the Web project's `data/` folder (the factory's content root), `ActImportService.SeedAsync` self-skips when the git-ignored Kaggle file is missing (src/MuktoAin.Infrastructure/Data/Seeding/ActImportService.cs:50), and `EmbeddingBatchJob` no-ops when `Embedding:RunOnStartup` is false (src/MuktoAin.Infrastructure/VectorStore/EmbeddingBatchJob.cs:127):

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.VectorStore;

namespace MuktoAin.IntegrationTests.Helpers;

public class MuktoAinWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: skips UseDeveloperExceptionPage + Razor runtime
        // compilation, and exercises the real UseExceptionHandler pipeline.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // EmbeddingBatchJob: false -> background job no-ops in tests
                ["Embedding:RunOnStartup"] = "false",
                // Program.cs dimension guard needs VectorSize; dimensionality unset -> guard skipped
                ["Qdrant:VectorSize"] = "3",
                ["SeedAdmin:Password"] = "Test-Admin-Passw0rd!",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap SSMS-backed SQL Server for EF InMemory (tests have no LocalDB).
            services.RemoveAll<Microsoft.EntityFrameworkCore.DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"MuktoAin-Tests-{Guid.NewGuid():N}"));

            // Deterministic offline AI seams: the real GeminiClient would make
            // HTTP calls (wrapped in Polly retries) — unacceptable in tests.
            services.AddSingleton<MuktoAin.Domain.Interfaces.IAiService, StubAiService>();
            services.AddSingleton<MuktoAin.Domain.Interfaces.IEmbeddingService, StubEmbeddingService>();
            services.RemoveAll<IVectorStore>();
            services.AddSingleton<IVectorStore, StubVectorStore>();
        });
    }

    // Returns a client that authenticates as the given user for every request.
    public HttpClient CreateAuthenticatedClient(int userId, UserRole role)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role.ToString());
        return client;
    }
}

// Plain-text answer — AiOrchestrationService treats the AI response as opaque
// content (verified: no JSON parsing of the explanation body), so a constant is enough.
public class StubAiService : MuktoAin.Domain.Interfaces.IAiService
{
    public Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult("স্টাব উত্তর: আপনার অধিকার বিশ্লেষণ / Stub analysis of your rights.");
}

public class StubEmbeddingService : MuktoAin.Domain.Interfaces.IEmbeddingService
{
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });
}

// Returns one high-score hit for any query so RagContextBuilder produces a
// deterministic citation (SimilaritySearchService then hydrates via the
// section repository — seed Act/ActSection id 1 in TestData when needed).
public class StubVectorStore : IVectorStore
{
    public Task EnsureCollectionAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK) =>
        Task.FromResult<IReadOnlyList<VectorSearchResult>>(new[]
        {
            new VectorSearchResult(
                VectorId: "sec_1_chk_1",
                Score: 0.95f,
                Payload: new Dictionary<string, string>
                {
                    ["SectionId"] = "1",
                    ["ChunkId"] = "1",
                    ["ActTitle"] = "Bangladesh Labour Act, 2006",
                    ["SectionNumber"] = "123",
                })
        });
}
```

> **Verify `IVectorStore` members compile:** the interface lives in `src/MuktoAin.Domain/Interfaces/IVectorStore.cs` (namespace `MuktoAin.Domain.Interfaces`) and `VectorSearchResult` in `MuktoAin.Domain.Models` — the parameter names above mirror `tests/MuktoAin.IntegrationTests/AiPipeline/RagRetrievalSmokeTests.cs:55`. Adjust to the exact positional signatures in those files if they differ (e.g. `RelevanceScore` instead of `Score`).
> **Remove `using Microsoft.Data.SqlClient;` if unused** — included only because some builders fail without it when removing the SqlServer options descriptor; if `RemoveAll<DbContextOptions<AppDbContext>>` alone is sufficient, drop it (keep the build warning-clean).

- [ ] **Step 5: Write the deterministic test-data seeder**

`tests/MuktoAin.IntegrationTests/Helpers/TestData.cs` — direct entity insertion into the InMemory context (Identity users without password hashes — nothing in these tests signs in through the cookie flow; `TestAuthHandler` fabricates the principal):

```csharp
using Microsoft.AspNetCore.Identity;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Helpers;

public static class TestData
{
    public const int DhakaDistrictId = 1;

    public static async Task<(District District, CaseCategory Category)> SeedBaselineAsync(AppDbContext db)
    {
        var district = new District { DistrictId = DhakaDistrictId, Name = "Dhaka" };
        var category = new CaseCategory { CategoryId = 1, Name = "Labour", NameBn = "শ্রম", Description = "Labour complaints" };
        db.Districts.Add(district);
        db.CaseCategories.Add(category);
        await db.SaveChangesAsync();
        return (district, category);
    }

    public static async Task<Act> SeedActAsync(AppDbContext db)
    {
        var act = new Act { ActId = 1, Title = "Bangladesh Labour Act, 2006", ActNumber = "XLII", Year = 2006 };
        var section = new ActSection
        {
            SectionId = 1,
            ActId = 1,
            Act = act,
            SectionNumber = "123",
            SectionText = "The wages of every worker shall be paid before the expiry of the seventh working day.",
        };
        db.Acts.Add(act);
        db.ActSections.Add(section);
        await db.SaveChangesAsync();
        return act;
    }

    public static async Task<User> NewUserAsync(AppDbContext db, string fullName, UserRole role)
    {
        var user = new User
        {
            UserName = $"{fullName}-{Guid.NewGuid():N}"[..24],
            Email = $"{Guid.NewGuid():N}@test.muktoain.bd",
            NormalizedUserName = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            NormalizedEmail = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            FullName = fullName,
            Role = role,
            AccountStatus = AccountStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<Case> NewCaseAsync(
        AppDbContext db, int? userId, byte districtId = DhakaDistrictId,
        int categoryId = 1, bool anonymous = false, string? trackingCode = null)
    {
        var c = new Case
        {
            UserId = anonymous ? null : userId,
            CategoryId = categoryId,
            DistrictId = districtId,
            Title = "৩ মাসের বকেয়া বেতন",
            Description = "Employer has not paid wages for three months.",
            Language = "en", // English templates keep existing tests independent of Task 5's A-3.9 routing
            Status = CaseStatus.Submitted,
            IsAnonymous = anonymous,
            AnonymousTrackingCode = trackingCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Cases.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public static GeneratedDocument NewDocument(int caseId, DocumentStatus status, string contentDraft = "Draft body text") =>
        new()
        {
            CaseId = caseId,
            DocumentType = DocumentType.LabourComplaint,
            ContentDraft = contentDraft,
            Status = status,
            VersionNo = 1,
            CreatedAt = DateTime.UtcNow,
        };
}
```

> **Property-name caveat:** `db.Districts` / `db.CaseCategories` / `db.Acts` / `db.ActSections` must match the actual `DbSet` names in `src/MuktoAin.Infrastructure/Data/AppDbContext.cs`. Read that file first and align (e.g. it may be `db.Set<District>()` style or differently-named sets). `CaseCategory` requires `NameBn NOT NULL DEFAULT N''` per `scripts/02_schema.sql:52` — set it explicitly.

- [ ] **Step 6: Write the smoke test proving the host boots**

`tests/MuktoAin.IntegrationTests/Api/WebHostSmokeTests.cs`:

```csharp
using System.Net;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class WebHostSmokeTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public WebHostSmokeTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_Home_Index_Returns_Success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        Assert.True(response.IsSuccessStatusCode,
            $"Expected success from GET / but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns_404_Via_StatusCodePages()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/DefinitelyNotARealRoute");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_NotFound_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/NotFound");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_ServerError_Returns_500()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/ServerError");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_AccessDenied_Returns_403()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

(The last three also pre-cover Task 7's A-3.12 page assertions — keep them here.)

- [ ] **Step 7: Build and run the smoke tests**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~WebHostSmokeTests"`
Expected: all PASS. If `GET /` fails, inspect the test output for the server exception (startup seeds, DI wiring). Fix root causes in the factory/config — do NOT weaken assertions.

- [ ] **Step 8: Run the full integration + unit suites to catch regressions**

Run: `dotnet test tests/MuktoAin.UnitTests && dotnet test tests/MuktoAin.IntegrationTests`
Expected: PASS (314+ unit tests green; no unit test references `Program` so the partial-class addition is inert).

---

### Task 2: A-3.7 — Document API integration tests (Preview / Download, auth + IDOR + review gate)

**Files:**
- Create: `tests/MuktoAin.IntegrationTests/Api/DocumentApiTests.cs`

**Interfaces:**
- Consumes: `MuktoAinWebApplicationFactory` + `TestData` (Task 1); endpoints `GET /Document/Preview?id={id}` and `GET /Document/Download?id={id}` (src/MuktoAin.Web/Controllers/DocumentController.cs:31,73). Access semantics from `CaseService.GetCaseDetailAsync` (src/MuktoAin.Application/Services/CaseService.cs:55): Citizen sees only own non-anonymous cases (or anonymous + valid tracking code); Lawyer/Admin see all (pool-claim model by design).
- Produces: regression suite for FR-14's human-in-the-loop gate (`Download` must 302-redirect unless `DocumentStatus.Approved`).

- [ ] **Step 1: Write the failing test class**

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class DocumentApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public DocumentApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    private async Task<(User Citizen, Case Case, GeneratedDocument Doc)> SeedOwnedDocumentAsync(
        DocumentStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "Owner Citizen", UserRole.Citizen);
        var other = await TestData.NewUserAsync(db, "Other Citizen", UserRole.Citizen);
        _ = other; // keeps the second user materialized for clarity
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        var doc = TestData.NewDocument(c.CaseId, status);
        db.GeneratedDocuments.Add(doc);
        await db.SaveChangesAsync();
        return (citizen, c, doc);
    }
```

Then the facts (same class):

```csharp
    [Fact]
    public async Task Preview_UnknownDocumentId_Returns_404()
    {
        var client = _factory.CreateClient(); // unauthenticated is fine — 404 fires before authz
        var response = await client.GetAsync("/Document/Preview/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_ZeroOrNegativeId_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Document/Preview/0");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_NonOwnerCitizen_Returns_403_IdorGuard()
    {
        var (_, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);

        // Fresh citizen who does NOT own the case
        using var scope = _factory.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intruder = await TestData.NewUserAsync(db2, "Intruder", UserRole.Citizen);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_OwnerCitizen_Returns_200_WithDraftContent()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Draft body", html);
    }

    [Fact]
    public async Task Preview_Admin_Returns_200_ForAnyCase()
    {
        var (_, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminUser = await TestData.NewUserAsync(db, "Admin", UserRole.Admin);

        var client = _factory.CreateAuthenticatedClient(adminUser.Id, UserRole.Admin);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_BeforeLawyerApproval_RedirectsToPreview_ReviewGate()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.UnderReview);
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Download/{doc.DocumentId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Document/Preview/{doc.DocumentId}",
            response.Headers.Location?.ToString());
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Download_AfterLawyerApproval_ReturnsPdfBytes()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Approved);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        doc.ContentFinal = doc.ContentDraft; // UpdateStatusAsync-equivalent state
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Download/{doc.DocumentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        // QuestPDF files start with the PDF magic marker
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
```

> **Adjustments you may need when first compiling:**
> 1. `MuktoAin.IntegrationTests` has no `GlobalUsings` — add `using Microsoft.Extensions.DependencyInjection;` for `CreateScope`.
> 2. `GET /Document/Preview/{id}` resolves via the default route `{controller=Home}/{action=Index}/{id?}` (Program.cs:343-345) — the `/Document/Preview/5` form is correct.
> 3. The redirect check on `Download` assumes TempData round-trips via the session cookie — `CreateClient()` handles cookies automatically.

- [ ] **Step 2: Run the Document tests**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~DocumentApiTests"`
Expected: PASS. A failure in `Download_AfterLawyerApproval_ReturnsPdfBytes` usually means the NotoSansBengali fonts are missing under `src/MuktoAin.Web/wwwroot/fonts/` — verify with `Get-ChildItem src\MuktoAin.Web\wwwroot\fonts` (E-1.3 wired them; do not skip the test).

- [ ] **Step 3: Record A-3.7 progress (interim) — do NOT flip the checkbox yet**

A-3.7 stays `- [ ]` until Task 4's payment tests land.

---

### Task 3: A-3.7 — Case API integration tests (Submit / Result / Track, antiforgery, ownership)

**Files:**
- Create: `tests/MuktoAin.IntegrationTests/Helpers/AntiForgery.cs`
- Create: `tests/MuktoAin.IntegrationTests/Api/CaseApiTests.cs`

**Interfaces:**
- Consumes: factory + `TestData`; endpoints `GET /Case/SubmitOptions` (JSON districts), `POST /Case/Submit` (form + `[ValidateAntiForgeryToken]`, src/MuktoAin.Web/Controllers/CaseController.cs:74-76), `GET /Case/Result/{id}` (ownership via `CaseService.GetCaseDetailAsync` — anonymous cases need the tracking code), `GET /Case/Track`.
- Produces: `AntiForgeryHelper.PostFormAsync(client, anyGetUrl, postUrl, fields)` — fetches a page containing the antiforgery hidden input, extracts the token, POSTs the form with cookies intact.

- [ ] **Step 1: Write the antiforgery helper**

```csharp
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MuktoAin.IntegrationTests.Helpers;

public static class AntiForgeryHelper
{
    private static readonly Regex TokenRegex =
        new("__RequestVerificationToken[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// Gets the given page (any page renders an antiforgery token into its
    /// forms — the token is cookie-scoped, not form-scoped), extracts the
    /// hidden token, and POSTs <paramref name="fields"/> to <paramref name="postUrl"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string anyGetUrl, string postUrl,
        Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(anyGetUrl);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No __RequestVerificationToken found on {anyGetUrl}");

        fields["__RequestVerificationToken"] = match.Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
```

- [ ] **Step 2: Write the failing test class**

```csharp
using System.Net;
using Microsoft.EntityFrameworkCore;
using MuktoAin.IntegrationTests.Helpers;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Api;

public class CaseApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public CaseApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SubmitOptions_ReturnsDistrictsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Case/SubmitOptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"districts\"", json);
        Assert.Contains("Dhaka", json);
    }

    [Fact]
    public async Task Submit_Post_MissingTitle_RerendersForm_WithModelStateError()
    {
        var client = _factory.CreateClient();
        var response = await AntiForgeryHelper.PostFormAsync(
            client, "/Case/Submit", "/Case/Submit",
            new Dictionary<string, string>
            {
                ["CategoryId"] = "1",
                ["DistrictId"] = "1",
                ["Title"] = "",
                ["Description"] = "Wages unpaid for three months despite repeated requests.",
                ["Language"] = "bn",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form re-rendered
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("শিরোনাম", html); // the failed form comes back to the user
    }

    [Fact]
    public async Task Submit_Post_ValidAnonymousSubmission_RedirectsToResultWithTrackingCode()
    {
        var client = _factory.CreateClient();
        var response = await AntiForgeryHelper.PostFormAsync(
            client, "/Case/Submit", "/Case/Submit",
            new Dictionary<string, string>
            {
                ["CategoryId"] = "1",
                ["DistrictId"] = "1",
                ["Title"] = "Unpaid wages complaint",
                ["Description"] = "Employer has not paid wages for three months despite repeated requests.",
                ["Language"] = "en",
                ["IsAnonymous"] = "true",
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.StartsWith("/Case/Result/", location);
        Assert.Contains("code=", location); // anonymous tracking code is round-tripped
    }

    [Fact]
    public async Task Result_NonOwnerCitizen_Returns_404_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", Domain.Enums.UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", Domain.Enums.UserRole.Citizen);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, Domain.Enums.UserRole.Citizen);
        var response = await client.GetAsync($"/Case/Result/{c.CaseId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Result_AnonymousCase_WithValidTrackingCode_Returns_200()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClient(); // no auth — guest + code path
        var response = await client.GetAsync($"/Case/Result/{c.CaseId}?code={code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Track_WithValidCode_RedirectsToResult()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/Case/Track?code={code}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Case/Result/{c.CaseId}", response.Headers.Location?.ToString());
    }
}
```

- [ ] **Step 3: Run the Case tests**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~CaseApiTests"`
Expected: PASS. `Submit_Post_ValidAnonymousSubmission_RedirectsToResultWithTrackingCode` exercises the full pipeline: moderation → ChatService transcript → `CommitToCaseAsync` (stubbed AI, seeded Act section) → template → redirect. If it 500s, check the server log for the moderation blocklist or encryption (DataProtection key ring) errors.

- [ ] **Step 4: Flip A-3.7 to in-progress in `plans/Dependency_plan.md` (do not mark complete until Task 4 passes)**

---

### Task 4: A-3.7 — Payment API integration tests (Honorarium / TopUp / Status, ownership + admin override)

**Files:**
- Create: `tests/MuktoAin.IntegrationTests/Api/PaymentApiTests.cs`

**Interfaces:**
- Consumes: factory + `TestData`; `[ApiController]`-routed endpoints (src/MuktoAin.Web/Controllers/PaymentController.cs:9-10, `[Route("[controller]/[action]")]`): `POST /Payment/Honorarium` `{ caseId, amount, trackingCode? }`, `POST /Payment/TopUp` `{ amount }`, `GET /Payment/Status/{id}`. Sandbox stub marks orders `Paid` immediately. Existing JSON error shapes: `{ success, message }` (BadRequest/NotFound), 403 via `Forbid()`, `{ success = true, orderId, amount, status, gatewayRef, message }` on success.
- Produces: baseline tests that Task 7 (A-3.12) must keep green while swapping `ex.Message` internals for friendly errors.

- [ ] **Step 1: Write the failing test class**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.IntegrationTests.Helpers;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Api;

public class PaymentApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public PaymentApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    private async Task<User> NewUserAsync(UserRole role)
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        return await TestData.NewUserAsync(db, "Payer", role);
    }

    private static HttpContent JsonBody(object body) => JsonContent.Create(body);

    [Fact]
    public async Task Honorarium_NonPositiveAmount_Returns_400_WithErrorJson()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = 1, amount = 0m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
    }

    [Fact]
    public async Task Honorarium_UnknownCase_Returns_404()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = 999999, amount = 500m }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Case not found", json);
    }

    [Fact]
    public async Task Honorarium_NonOwnerCitizen_Returns_403_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", UserRole.Citizen);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Honorarium_AnonymousCase_WithMatchingTrackingCode_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClient(); // guest, no principal — code is the credential
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m, trackingCode = code }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Paid", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Honorarium_AdminOverride_AllowedForAnyCase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var admin = await TestData.NewUserAsync(db, "Admin", UserRole.Admin);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(admin.Id, UserRole.Admin);
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
    }

    [Fact]
    public async Task TopUp_ValidAmount_ReturnsSandboxPaidOrder()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/TopUp", JsonBody(new { amount = 300m }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(300m, doc.RootElement.GetProperty("amount").GetDecimal());
        Assert.StartsWith("SANDBOX-TOP-", doc.RootElement.GetProperty("gatewayRef").GetString());
    }

    [Fact]
    public async Task Status_NonOwner_Returns_403_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", UserRole.Citizen);
        var order = new PaymentOrder
        {
            UserId = owner.Id, Purpose = PaymentPurpose.TopUp, Status = PaymentStatus.Paid,
            Amount = 100m, Commission = 0m, NetToLawyer = 100m,
            GatewayRef = "SANDBOX-TOP-TEST", CreatedAt = DateTime.UtcNow, PaidAt = DateTime.UtcNow,
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Payment/Status/{order.PaymentOrderId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_Owner_ReturnsOrderJson()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var order = new PaymentOrder
        {
            UserId = owner.Id, Purpose = PaymentPurpose.TopUp, Status = PaymentStatus.Paid,
            Amount = 100m, Commission = 0m, NetToLawyer = 100m, CreatedAt = DateTime.UtcNow,
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(owner.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Payment/Status/{order.PaymentOrderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Paid\"", json);
    }
}
```

- [ ] **Step 2: Run the Payment tests**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~PaymentApiTests"`
Expected: PASS.

- [ ] **Step 3: Run the whole solution test matrix**

Run: `dotnet test`
Expected: UnitTests (314+) + IntegrationTests (including RagRetrievalSmokeTests) all PASS.

- [ ] **Step 4: Record completion in plans/Dependency_plan.md**

Flip `- [ ] **[A-3.7]** Comprehensive API Integration Test Suite — *Arpita*` (line ~287) to `- [x]`, wrap the task text in `~~...~~` strikethrough, keeping the trailing description intact:
`- [x] ~~**[A-3.7]** Comprehensive API Integration Test Suite — *Arpita* — end-to-end test coverage for Document, Case, and Payment controller endpoints~~`

---

### Task 5: A-3.9 — Bangla-only document template variants + coverage tests

**Files:**
- Create: `src/MuktoAin.Application/Documents/IBanglaDocumentVariant.cs`
- Modify: `src/MuktoAin.Application/Documents/Templates/LabourComplaintTemplate.cs` (add method)
- Modify: `src/MuktoAin.Application/Documents/Templates/GeneralDiaryTemplate.cs` (add method)
- Modify: `src/MuktoAin.Application/Documents/Templates/RtiRequestTemplate.cs` (add method)
- Modify: `src/MuktoAin.Application/Documents/Templates/ConsumerComplaintTemplate.cs` (add method)
- Modify: `src/MuktoAin.Application/Documents/DocumentGenerator.cs` (add `GenerateBanglaOnlyAsync`)
- Modify: `src/MuktoAin.Application/Services/DocumentService.cs:64` (route by `Case.Language`)
- Test: `tests/MuktoAin.UnitTests/Services/BanglaOnlyTemplateTests.cs`

**Interfaces:**
- Consumes: existing `IDocumentTemplate` (`DocumentType`, `RenderAsync(Case, RightsExplanationDto)`), `RightsExplanationDto(Explanation, CitedSections, Disclaimer)`, `Disclaimers.LegalBangla`, `Case.Language` ("bn"/"en" — see `scripts/02_schema.sql:181` NVARCHAR(10)).
- Produces: `IBanglaDocumentVariant { DocumentType DocumentType { get; } Task<string> RenderBanglaOnlyAsync(Case, RightsExplanationDto) }`; `DocumentGenerator.GenerateBanglaOnlyAsync(Case, RightsExplanationDto): Task<string>`; DocumentService auto-selects Bangla-only rendering whenever `Case.Language == "bn"`.
- **Behavior change (flag for Shads):** demo data and the chat commit path set `Language = "bn"` (src/MuktoAin.Application/Services/ChatService.cs:352, SeedDemoData.cs:179), so after this task Bangla cases render Bangla-only documents instead of the current English templates. That is the intended product behavior (Bangla-first legal aid); English templates remain for `Language == "en"` or empty.

- [ ] **Step 1: Write the failing tests**

`tests/MuktoAin.UnitTests/Services/BanglaOnlyTemplateTests.cs`:

```csharp
using MuktoAin.Application.Documents;
using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class BanglaOnlyTemplateTests
{
    // All four concrete templates — mirrors Program.cs:208-211 DI registrations.
    public static TheoryData<IDocumentTemplate, DocumentType> AllTemplates => new()
    {
        { new LabourComplaintTemplate(), DocumentType.LabourComplaint },
        { new GeneralDiaryTemplate(), DocumentType.GeneralDiary },
        { new RtiRequestTemplate(), DocumentType.RtiRequest },
        { new ConsumerComplaintTemplate(), DocumentType.ConsumerComplaint },
    };

    private static Case BuildCase(string description, string language = "bn") => new()
    {
        CaseId = 30,
        CategoryId = 1,
        District = new District { DistrictId = 1, Name = "ঢাকা" },
        DistrictId = 1,
        Title = "অবৈধ বেতন কাটা",
        Description = description,
        Language = language,
    };

    private static RightsExplanationDto BuildExplanation(bool withSections = true) => new(
        Explanation: "শ্রম আইন অনুযায়ী আপনার বেতন পাওয়ার অধিকার রয়েছে।",
        CitedSections: withSections
            ? new List<CitedSectionDto>
              {
                  new(1, "Bangladesh Labour Act, 2006", "123",
                      "The wages of every worker shall be paid before the expiry of the seventh working day.",
                      0.92f, "Vector")
              }
            : new List<CitedSectionDto>(),
        Disclaimer: Disclaimers.LegalBangla);

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_ContainsNoEnglishTemplateCopy(IDocumentTemplate template, DocumentType _)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।"), BuildExplanation());

        Assert.DoesNotContain("FACTS OF THE CASE", rendered);
        Assert.DoesNotContain("STATEMENT OF FACTS", rendered);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", rendered);
        Assert.DoesNotContain("RELIEF SOUGHT", rendered);
        Assert.DoesNotContain("DECLARATION:", rendered);
        Assert.DoesNotContain("Respected", rendered);
        Assert.DoesNotContain("Subject:", rendered);
        // English disclaimer must NOT appear in the Bangla-only variant
        Assert.DoesNotContain(Disclaimers.Legal, rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_ContainsBanglaSkeletonAndBanglaDisclaimer(
        IDocumentTemplate template, DocumentType _)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।"), BuildExplanation());

        Assert.Contains("বরাবর", rendered);                      // header salutation
        Assert.Contains("মামলার ঘটনাবলি", rendered);
        Assert.Contains("প্রযোজ্য আইনি বিধান", rendered);
        Assert.Contains("ঘোষণা", rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);      // surface 3 of 3 preserved
        Assert.Contains("তারিখ:", rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_MixedLanguage_UserEnglishContentStillRenderedVerbatim(
        IDocumentTemplate template, DocumentType _)
    {
        const string englishDescription =
            "The employer has withheld wages for three months despite written requests.";
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase(englishDescription), BuildExplanation());

        // Citizen-supplied content is DATA, not template copy — it flows through
        // untouched even when the skeleton is Bangla-only.
        Assert.Contains(englishDescription, rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_NoCitedSections_RendersBanglaFallback(
        IDocumentTemplate template, DocumentType _)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("বিবরণ"), BuildExplanation(withSections: false));

        Assert.Contains("[নির্দিষ্ট কোনো ধারা পাওয়া যায়নি", rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_MissingDistrict_UsesUnderscorePlaceholder(
        IDocumentTemplate template, DocumentType _)
    {
        var c = BuildCase("বিবরণ");
        c.District = null!;
        var rendered = await ((IBanglaDocumentVariant)template).RenderBanglaOnlyAsync(c, BuildExplanation());

        Assert.Contains("________", rendered);
    }

    [Fact]
    public async Task DocumentGenerator_GenerateBanglaOnlyAsync_RoutesLabourCaseToBanglaVariant()
    {
        var generator = new DocumentGenerator(new IDocumentTemplate[]
        {
            new LabourComplaintTemplate(), new GeneralDiaryTemplate(),
            new RtiRequestTemplate(), new ConsumerComplaintTemplate(),
        });
        var c = BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।");
        c.CategoryId = 1; // Labour per DocumentGenerator.MapCategoryToDocumentType

        var rendered = await generator.GenerateBanglaOnlyAsync(c, BuildExplanation());

        Assert.Contains("প্রযোজ্য আইনি বিধান", rendered);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", rendered);
    }

    [Fact]
    public async Task DocumentService_GenerateDocumentAsync_BanglaCase_UsesBanglaOnlyTemplate()
    {
        var docRepo = new Moq.Mock<MuktoAin.Domain.Interfaces.Repositories.IRepository<GeneratedDocument>>();
        var caseRepo = new Moq.Mock<MuktoAin.Domain.Interfaces.Repositories.ICaseRepository>();
        var districtRepo = new Moq.Mock<MuktoAin.Domain.Interfaces.Repositories.IRepository<District>>();
        var categoryRepo = new Moq.Mock<MuktoAin.Domain.Interfaces.Repositories.IRepository<CaseCategory>>();
        var pdfExporter = new Moq.Mock<MuktoAin.Domain.Interfaces.Services.IPdfExporter>();
        pdfExporter.Setup(p => p.GeneratePdf(It.IsAny<GeneratedDocument>(), It.IsAny<Case>()))
            .Returns(Array.Empty<byte>());
        var c = BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।", language: "bn");
        caseRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(c);
        districtRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(c.District);
        categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new CaseCategory { CategoryId = 1, Name = "Labour", NameBn = "শ্রম" });

        var service = new DocumentService(
            new DocumentGenerator(new IDocumentTemplate[]
            {
                new LabourComplaintTemplate(), new GeneralDiaryTemplate(),
                new RtiRequestTemplate(), new ConsumerComplaintTemplate(),
            }),
            docRepo.Object, caseRepo.Object, districtRepo.Object, categoryRepo.Object, pdfExporter.Object);

        var dto = await service.GenerateDocumentAsync(30, BuildExplanation());

        Assert.Contains("প্রযোজ্য আইনি বিধান", dto.ContentDraft);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", dto.ContentDraft);
    }
}
```

> Note: `DraftDocumentDto` exposes `ContentDraft` as positional record field 4 — see `src/MuktoAin.Application/DTOs/DraftDocumentDto.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~BanglaOnlyTemplateTests"`
Expected: FAIL — compile error `IBanglaDocumentVariant not found` / `GenerateBanglaOnlyAsync not defined`.

- [ ] **Step 3: Create the variant interface**

`src/MuktoAin.Application/Documents/IBanglaDocumentVariant.cs`:

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents;

/// <summary>
/// Bangla-only rendering variant (A-3.9). Implemented by each IDocumentTemplate:
/// emits the full legal-document skeleton with NO English template copy —
/// all fixed text (headers, section labels, boilerplate) in Bangla only.
/// Citizen-supplied facts and AI explanation text are data, not template copy,
/// and pass through verbatim. The disclaimer stamp uses Disclaimers.LegalBangla
/// only, preserving the mandatory surface-3 disclaimer without English.
/// </summary>
public interface IBanglaDocumentVariant
{
    DocumentType DocumentType { get; }
    Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation);
}
```

- [ ] **Step 4: Implement the Bangla-only renderer on all four templates**

Add to each template class (they already implement `IDocumentTemplate`; keep the existing English `RenderAsync` untouched). Common helper — since all four templates need it, add a small internal static class in the Templates folder, `BanglaOnlyRender.cs`:

```csharp
using System.Globalization;

namespace MuktoAin.Application.Documents.Templates;

internal static class BanglaOnlyRender
{
    internal const string Rule = "────────────────────────────";
    internal const string DoubleRule = "════════════════════════════════════════════";
    internal const string Placeholder = "________";

    // Numeric date — Bangla-only documents must not carry English month names.
    internal static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shared closing block: signature + Bangla-only disclaimer stamp
    /// (mandatory surface 3 of 3 — FR-11).
    /// </summary>
    internal static string AppendClosing(System.Text.StringBuilder sb, string roleLabelBn, string? districtName)
    {
        sb.AppendLine($"তারিখ: {Today()}");
        sb.AppendLine($"{roleLabelBn}: ________________________");
        sb.AppendLine($"জেলা: {districtName ?? Placeholder}");
        sb.AppendLine();
        sb.AppendLine(DoubleRule);
        sb.AppendLine(MuktoAin.Domain.Constants.Disclaimers.LegalBangla);
        sb.AppendLine(DoubleRule);
        return sb.ToString();
    }
}
```

Then, per template — `LabourComplaintTemplate.cs` (add + `using MuktoAin.Application.Documents;` at top if not already there; it is not, since the file currently imports only DTOs/Constants/Entities/Enums):

```csharp
    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine("বরাবর");
        sb.AppendLine("শ্রম পরিদপ্তর / জেলা শ্রম আদালত");
        sb.AppendLine($"{districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $"ধারা {primarySection.SectionNumber} (বাংলাদেশ শ্রম আইন, ২০০৬)-এর অধীনে"
            : "বাংলাদেশ শ্রম আইন, ২০০৬-এর অধীনে";
        sb.AppendLine($"বিষয়: {sectionRef} অভিযোগ");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        // ── Complainant Introduction ────────────────────────────
        sb.AppendLine($"আমি, স্বাক্ষরকারী, {districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, " +
                       "বাংলাদেশ শ্রম আইন, ২০০৬-এর নিম্নলিখিত লঙ্ঘনের বিষয়ে এই অভিযোগ জমা দিচ্ছি:");
        sb.AppendLine();

        // ── Facts ───────────────────────────────────────────────
        sb.AppendLine("মামলার ঘটনাবলি:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        // ── Legal provisions ────────────────────────────────────
        sb.AppendLine("প্রযোজ্য আইনি বিধান:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, ধারা {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("  [নির্দিষ্ট কোনো ধারা পাওয়া যায়নি — একজন যোগ্য আইনজীবীর পরামর্শ নিন]");
            sb.AppendLine();
        }

        // ── Rights explanation ──────────────────────────────────
        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("প্রযোজ্য আইনে আপনার অধিকার:");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        // ── Relief ──────────────────────────────────────────────
        sb.AppendLine("প্রার্থিত প্রতিকার:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("উপরোক্ত ঘটনা ও উদ্ধৃত আইনি বিধানের ভিত্তিতে, অভিযোগকারী ক্ষতিপূরণ, " +
                       "পুনর্বহাল এবং/অথবা মাননীয় আদালত যথাযথ মনে করেন এমন অন্য যেকোনো প্রতিকার প্রার্থনা করছেন।");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, উপরোক্ত তথ্য আমার জানামতে ও বিশ্বাসমতে সম্পূর্ণ সত্য ও সঠিক। " +
                       "মিথ্যা বিবৃতি প্রদানের ফলে আইনগত পরিণতি হতে পারে তা আমি অবগত।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "অভিযোগকারী", districtName);
    }
```

Class must additionally declare `IBanglaDocumentVariant`: change the class declaration to
`public class LabourComplaintTemplate : IDocumentTemplate, IBanglaDocumentVariant`
and add `using MuktoAin.Application.Documents;` only if the namespace differs (the templates are already IN `MuktoAin.Application.Documents.Templates` — `IBanglaDocumentVariant` is in the parent namespace `MuktoAin.Application.Documents`, which is implicitly visible; no extra using needed).

`GeneralDiaryTemplate.cs` — add (and add `, IBanglaDocumentVariant` to the class declaration):

```csharp
    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("ভারপ্রাপ্ত কর্মকর্তা (ওসি)");
        sb.AppendLine($"থানা, {districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" ({primarySection.ActTitle}, ধারা {primarySection.SectionNumber} সংশ্লিষ্ট)"
            : string.Empty;
        sb.AppendLine($"বিষয়: সাধারণ ডায়েরি (জিডি) ভুক্তির আবেদন{sectionRef}");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"আমি, স্বাক্ষরকারী, {districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, " +
                       "নিম্নবর্ণিত ঘটনা/পরিস্থিতি সংক্রান্ত একটি সাধারণ ডায়েরি (জিডি) ভুক্তি রেকর্ডের জন্য " +
                       "এই আবেদনটি বিনীতভাবে জমা দিচ্ছি:");
        sb.AppendLine();

        sb.AppendLine("ঘটনার বিবরণ:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        sb.AppendLine("প্রযোজ্য আইনি বিধান:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, ধারা {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("  [নির্দিষ্ট কোনো ধারা পাওয়া যায়নি — ডিউটি কর্মকর্তা বা একজন যোগ্য আইনজীবীর পরামর্শ নিন]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("প্রযোজ্য আইনি অধিকার ও প্রতিকার:");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine("প্রার্থনা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি সর্বান্তঃকরণে প্রার্থনা করছি যে, উপরোক্ত ঘটনা আপনার থানার সাধারণ ডায়েরি খাতায় যথাযথভাবে রেকর্ড করা হোক, " +
                       "প্রয়োজনীয় তদন্ত শুরু করা হোক এবং আইনগত সুরক্ষা ও নিরাপত্তার জন্য উপযুক্ত ব্যবস্থা গ্রহণ করা হোক।");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, উপরোক্ত তথ্য আমার জ্ঞান ও বিশ্বাসমতে সত্য ও সঠিক। " +
                       "আইন-শৃঙ্খলা বাহিনীকে মিথ্যা বা বিভ্রান্তিকর তথ্য প্রদান বাংলাদেশি আইনে শাস্তিযোগ্য।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "আবেদনকারী", districtName);
    }
```

`RtiRequestTemplate.cs` — add:

```csharp
    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("দায়িত্বপ্রাপ্ত কর্মকর্তা / তথ্য কর্মকর্তা");
        sb.AppendLine("[সরকারি সংস্থা / দপ্তরের নাম]");
        sb.AppendLine($"{districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        sb.AppendLine("বিষয়: তথ্য অধিকার আইন, ২০০৯-এর ধারা ৮-এর অধীনে তথ্য প্রাপ্তির আবেদন");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"তথ্য অধিকার আইন, ২০০৯-এর ধারা ৮-এর বিধান অনুসারে, আমি, স্বাক্ষরকারী বাংলাদেশি নাগরিক, " +
                       $"{districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, আপনার দায়িত্বপ্রাপ্ত দপ্তর হতে " +
                       "নিম্নলিখিত সরকারি তথ্য ও নথি প্রার্থনা করছি:");
        sb.AppendLine();

        sb.AppendLine("প্রার্থিত তথ্য:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        sb.AppendLine("প্রযোজ্য আইনি বিধান / যৌক্তিকতা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, ধারা {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("• তথ্য অধিকার আইন, ২০০৯, ধারা ৮ (তথ্য প্রাপ্তির পদ্ধতি)");
            sb.AppendLine("• তথ্য অধিকার আইন, ২০০৯, ধারা ৯ (তথ্য সরবরাহের সময়সীমা)");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("আইনগত অধিকার ও সময়সীমা (তথ্য অধিকার আইন, ২০০৯):");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine("তথ্যের পছন্দসই রূপ:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আইনের ধারা ৮(৪) অনুসারে প্রার্থিত তথ্য মুদ্রিত/ফটোকপি করা প্রত্যয়িত রূপে " +
                       "বা ইলেকট্রনিক রূপে (ই-মেইল/ডেটা সংরক্ষণ) প্রদান করা হোক।");
        sb.AppendLine("সরকারি বিধি অনুযায়ী নির্ধারিত প্রাতিষ্ঠানিক অনুলিপি ফি পরিশোধ করতে আমি প্রতিশ্রুতিবদ্ধ।");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, আমি বাংলাদেশের নাগরিক এবং এই তথ্য আবেদনটি " +
                       "বাংলাদেশি আইনের অধীনে বৈধ নাগরিক স্বচ্ছতা ও আইনি অধিকার সংরক্ষণের সুশৃঙ্খল উদ্দেশ্যে জমা দেওয়া হয়েছে।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "আবেদনকারী", districtName);
    }
```

`ConsumerComplaintTemplate.cs` — add (and `, IBanglaDocumentVariant`):

```csharp
    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("মহাপরিচালক / দায়িত্বপ্রাপ্ত কর্মকর্তা");
        sb.AppendLine("জাতীয় ভোক্তা অধিকার সংরক্ষণ অধিদপ্তর (ডিএনসিআরপি)");
        sb.AppendLine($"জেলা কার্যালয়: {districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯-এর ধারা {primarySection.SectionNumber}-এর অধীনে"
            : string.Empty;
        sb.AppendLine($"বিষয়: {sectionRef.TrimStart()} অভিযোগ");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"আমি, স্বাক্ষরকারী ভোক্তা, {districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, " +
                       "সংশ্লিষ্ট প্রতিষ্ঠান/বিক্রেতা/সেবাদাতার বিরুদ্ধে ভোক্তা-বিরোধী কার্যকলাপ ও " +
                       "ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ (২০০৯ সনের ২৬ নং আইন)-এর বিধিভঙ্গের বিষয়ে " +
                       "এই আনুষ্ঠানিক অভিযোগ জমা দিচ্ছি:");
        sb.AppendLine();

        sb.AppendLine("অভিযোগের ঘটনাবলি:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        sb.AppendLine("প্রযোজ্য আইনি বিধান:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, ধারা {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("• ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ (প্রাসঙ্গিক ভোক্তা-বিরোধী কার্যকলাপ সংক্রান্ত বিধান)");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("ভোক্তা আইনে আপনার অধিকার:");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine("প্রার্থিত প্রতিকার:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("উপরোক্ত ঘটনা ও প্রযোজ্য আইনি বিধানের ভিত্তিতে অভিযোগকারী বিনীতভাবে প্রার্থনা করছেন:");
        sb.AppendLine("১. প্রতিবাদী প্রতিষ্ঠানের বিরুদ্ধে দ্রুত তদন্ত ও শুনানি আয়োজন করা হোক;");
        sb.AppendLine("২. যথাযথ প্রতিস্থাপন, পূর্ণ আর্থিক ফেরত বা আইনগত ক্ষতিপূরণ প্রদান করা হোক;");
        sb.AppendLine("৩. আইন অনুযায়ী জরিমানা আরোপ করা হোক এবং আদায়কৃত জরিমানার ২৫% ধারা ৭৬(৪) মোতাবেক অভিযোগকারীকে প্রদান করা হোক।");
        sb.AppendLine();

        sb.AppendLine("সংযুক্ত প্রমাণপত্র:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("• ক্রয় রশিদ / মানি রশিদ / ক্যাশ মেমো / অর্ডার নিশ্চিতকরণ");
        sb.AppendLine("• পণ্যের ছবি, প্যাকেজিং, ব্যাচ নম্বর বা ওয়ারেন্টি নথি (প্রযোজ্য ক্ষেত্রে)");
        sb.AppendLine("• প্রতিবাদী সঙ্গে যোগাযোগের রেকর্ড / অভিযোগের স্মৃতিচিহ্ন");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, উপরোক্ত বিবরণ আমার জ্ঞান, তথ্য ও বিশ্বাসমতে সত্য ও সঠিক, " +
                       "এবং এই অভিযোগে আমি কোনো অপরিহার্য তথ্য গোপন করিনি।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "অভিযোগকারী", districtName);
    }
```

- [ ] **Step 5: Add `GenerateBanglaOnlyAsync` to `DocumentGenerator`**

In `src/MuktoAin.Application/Documents/DocumentGenerator.cs`, insert after `GenerateAsync`:

```csharp
    /// <summary>
    /// Bangla-only rendering path (A-3.9): fixed template copy is emitted in
    /// Bangla only (no English interleaving); citizen/AI content flows through
    /// verbatim. Routing decision (which language a case gets) is the caller's —
    /// DocumentService routes on Case.Language.
    /// </summary>
    public async Task<string> GenerateBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var docType = MapCategoryToDocumentType(caseEntity.CategoryId);

        if (!_templates.TryGetValue(docType, out var template))
            throw new InvalidOperationException($"No template found for document type {docType}");

        if (template is not IBanglaDocumentVariant banglaVariant)
            throw new InvalidOperationException($"Template {template.GetType().Name} has no Bangla-only variant");

        return await banglaVariant.RenderBanglaOnlyAsync(caseEntity, explanation);
    }
```

- [ ] **Step 6: Route in `DocumentService.GenerateDocumentAsync` by case language**

In `src/MuktoAin.Application/Services/DocumentService.cs`, replace line 64 (`var content = await _generator.GenerateAsync(caseEntity, explanation);`) with:

```csharp
        // A-3.9: Bangla-first rendering — a case whose language is "bn" (the
        // default for every chat commit and demo seed) gets the Bangla-only
        // template variant. English/unknown languages keep the legacy template.
        var content = string.Equals(caseEntity.Language, "bn", StringComparison.OrdinalIgnoreCase)
            ? await _generator.GenerateBanglaOnlyAsync(caseEntity, explanation)
            : await _generator.GenerateAsync(caseEntity, explanation);
```

- [ ] **Step 7: Run the new tests, then the full unit suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: BanglaOnlyTemplateTests PASS; **existing template tests still PASS** (English `RenderAsync` untouched); `DocumentGeneratorTests`/`DocumentServiceTests` PASS — if a service test asserted English draft output for a "bn" case, update that assertion to the Bangla-only skeleton markers (that expectation is what A-3.9 changes by design).

- [ ] **Step 8: Record completion in plans/Dependency_plan.md**

Flip `- [ ] **[A-3.9]** Extended Document Template Variants (Bangla-Only Versions + Coverage Tests) — *Arpita*` (line ~289) to `- [x]` wrapped in `~~...~~`.

---

### Task 6: A-3.11 — Input validation hardening (annotations, length guards, XSS review, client wiring)

**Files:**
- Modify: `src/MuktoAin.Web/ViewModels/CaseViewModels.cs:5-15` (`CaseSubmitViewModel`)
- Modify: `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs` (`SearchViewModel:3-15`, `LawyerApplyViewModel:45-50`, `LawyerReviewViewModel:52-62`)
- Modify: `src/MuktoAin.Web/ViewModels/RegisterViewModel.cs:45-49` (lawyer-only optional fields)
- Modify: `src/MuktoAin.Web/ViewModels/PasswordResetViewModels.cs:11-21` (`ResetPasswordViewModel` password policy)
- Modify: `src/MuktoAin.Web/Views/Case/Submit.cshtml` (client-side unobtrusive validation wiring)
- Test: `tests/MuktoAin.UnitTests/ViewModels/CaseSubmitViewModelTests.cs`
- Test: `tests/MuktoAin.UnitTests/Controllers/MarkdownTextTests.cs`

**Interfaces:**
- Consumes: `System.ComponentModel.DataAnnotations` (pattern per `RegisterViewModel.cs`); DB widths from `scripts/02_schema.sql` — `CASE.Title`/`Description` are NVARCHAR(MAX) because encrypted (line 174-180), so app-level guards are behavioral, not DB-forced; `LAWYER_PROFILE.BarRegistrationNumber NVARCHAR(100)` (line 149), `Specialization NVARCHAR(200)` (line 152); client `maxlength` hints already on `Views/Case/Submit.cshtml` (`maxlength="250"` Title, `maxlength="5000"` Description) — server annotations must MATCH these.
- Produces: `ModelState.IsValid`-driven rejections for empty/oversized Title/Description, invalid Language, out-of-range ids; `[ValidateAntiForgeryToken]` already present on the POSTs — unchanged.

- [ ] **Step 1: Write the failing ViewModel validation tests**

`tests/MuktoAin.UnitTests/ViewModels/CaseSubmitViewModelTests.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.ViewModels;

public class CaseSubmitViewModelTests
{
    private static bool TryValidate(CaseSubmitViewModel vm, out List<ValidationResult> results)
    {
        var context = new ValidationContext(vm);
        results = new List<ValidationResult>();
        return Validator.TryValidateObject(vm, context, results, validateAllProperties: true);
    }

    private static CaseSubmitViewModel ValidVm() => new()
    {
        CategoryId = 1,
        DistrictId = 1,
        Title = "৩ মাসের বকেয়া বেতন",
        Description = "নিয়োগকর্তা গত তিন মাস ধরে বেতন পরিশোধ করছেন না।",
        Language = "bn",
    };

    [Fact]
    public void Valid_Model_Passes()
    {
        Assert.True(TryValidate(ValidVm(), out _));
    }

    [Fact]
    public void Empty_Title_Fails()
    {
        var vm = ValidVm();
        vm.Title = "";
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Title)));
    }

    [Fact]
    public void Title_Over_250_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Title = new string('অ', 251);
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Title)));
    }

    [Fact]
    public void Description_Under_20_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Description = "too short";
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Description)));
    }

    [Fact]
    public void Description_Over_5000_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Description = new string('ক', 5001);
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Description)));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("bangla")]
    [InlineData("BN-1")]
    public void Invalid_Language_Fails(string language)
    {
        var vm = ValidVm();
        vm.Language = language;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Language)));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("bn")]
    public void Valid_Language_Passes(string language)
    {
        var vm = ValidVm();
        vm.Language = language;
        Assert.True(TryValidate(vm, out _));
    }

    [Fact]
    public void DistrictId_Zero_Fails()
    {
        var vm = ValidVm();
        vm.DistrictId = 0;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.DistrictId)));
    }

    [Fact]
    public void CategoryId_Negative_Fails()
    {
        var vm = ValidVm();
        vm.CategoryId = -1;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.CategoryId)));
    }
}
```

- [ ] **Step 2: Write the failing XSS-sanitization tests**

`tests/MuktoAin.UnitTests/Controllers/MarkdownTextTests.cs` — these document the CURRENT `DisableHtml()` pipeline behavior (src/MuktoAin.Web/Controllers/MarkdownText.cs:16-19); they are regression guards, and are expected to pass immediately (if any fails, the Markdig pipeline was weakened — that is a bug to fix, not the test):

```csharp
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class MarkdownTextTests
{
    private static string ToHtmlString(string? markdown)
    {
        var html = MarkdownText.ToHtml(markdown);
        using var writer = new System.IO.StringWriter();
        html.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    public void ToHtml_StripsRawHtml_FromAiOutput(string payload)
    {
        var html = ToHtmlString(payload);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToHtml_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ToHtmlString(null));
        Assert.Equal(string.Empty, ToHtmlString("   "));
    }

    [Fact]
    public void ToHtml_PlainMarkdown_StillRenders()
    {
        var html = ToHtmlString("**বোল্ড দাবি**");
        Assert.Contains("<strong>", html);
    }
}
```

> Note: this class lives under `tests/MuktoAin.UnitTests/Controllers/` because `MarkdownText` lives in the Web project's `Controllers` folder (namespace `MuktoAin.Web.Controllers`) — the UnitTests project already references `MuktoAin.Web`.

- [ ] **Step 3: Run tests to verify failure state**

Run: `dotnet test tests/MuktoAin.UnitTests --filter "FullyQualifiedName~CaseSubmitViewModelTests|FullyQualifiedName~MarkdownTextTests"`
Expected: CaseSubmitViewModelTests FAIL (no annotations on `CaseSubmitViewModel` yet); MarkdownTextTests PASS (guards).

- [ ] **Step 4: Harden `CaseSubmitViewModel` (server-side annotations matching client maxlengths)**

Replace `CaseSubmitViewModel` in `src/MuktoAin.Web/ViewModels/CaseViewModels.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

public class CaseSubmitViewModel
{
    [Required(ErrorMessage = "অভিযোগের ধরন নির্বাচন করুন / Please select a category")]
    [Range(1, int.MaxValue, ErrorMessage = "সঠিক অভিযোগের ধরন নির্বাচন করুন / Please select a valid category")]
    public int CategoryId { get; set; }

    // District FK is TINYINT-backed (64 districts, byte DistrictId) — 1..255.
    [Required(ErrorMessage = "জেলা নির্বাচন করুন / Please select a district")]
    [Range(1, 255, ErrorMessage = "সঠিক জেলা নির্বাচন করুন / Please select a valid district")]
    public byte DistrictId { get; set; }

    // Matches maxlength="250" on Views/Case/Submit.cshtml. (DB column is
    // NVARCHAR(MAX) because Title is stored encrypted — this is an app guard.)
    [Required(ErrorMessage = "শিরোনাম প্রয়োজন / Title is required")]
    [StringLength(250, MinimumLength = 5,
        ErrorMessage = "শিরোনাম ৫ থেকে ২৫০ অক্ষরের মধ্যে হতে হবে / Title must be 5–250 characters")]
    public string Title { get; set; } = string.Empty;

    // Matches maxlength="5000" on Views/Case/Submit.cshtml; also caps the AI prompt budget.
    [Required(ErrorMessage = "বিবরণ প্রয়োজন / Description is required")]
    [StringLength(5000, MinimumLength = 20,
        ErrorMessage = "বিবরণ ২০ থেকে ৫০০০ অক্ষরের মধ্যে হতে হবে / Description must be 20–5000 characters")]
    public string Description { get; set; } = string.Empty;

    // CASE.Language is NVARCHAR(10) and the pipeline only handles bn/en.
    [RegularExpression("^(bn|en)$",
        ErrorMessage = "ভাষা 'bn' বা 'en' হতে হবে / Language must be 'bn' or 'en'")]
    public string Language { get; set; } = "bn";

    public bool IsAnonymous { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Districts { get; set; } = new();
}
```

- [ ] **Step 5: Harden the remaining ViewModels (length guards matching DB columns)**

`MiscellaneousViewModels.cs` — add `using System.ComponentModel.DataAnnotations;` at the top, then:

```csharp
public class SearchViewModel
{
    // Free-text keyword search input — guard against oversized/abusive queries
    // before they reach FTS CONTAINS.
    [StringLength(200, ErrorMessage = "সার্চ ২০০ অক্ষরের মধ্যে হতে হবে / Search must be at most 200 characters")]
    public string Query { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "অবৈধ পৃষ্ঠা / Invalid page")]
    public int Page { get; set; } = 1;

    [Range(1, 50, ErrorMessage = "অবৈধ পৃষ্ঠার আকার / Invalid page size")]
    public int PageSize { get; set; } = 10;
    // ... (rest of the properties unchanged: TotalResults, ActId, HasSearched, Results)
```

Keep every existing member — only add attributes. `LawyerApplyViewModel`:

```csharp
public class LawyerApplyViewModel
{
    // LAWYER_PROFILE.BarRegistrationNumber is NVARCHAR(100) (scripts/02_schema.sql:149)
    [Required(ErrorMessage = "বার রেজিস্ট্রেশন নম্বর প্রয়োজন / Bar Registration Number is required")]
    [StringLength(100, ErrorMessage = "সর্বোচ্চ ১০০ অক্ষর / Maximum 100 characters")]
    public string BarRegistrationNumber { get; set; } = string.Empty;

    // LAWYER_PROFILE.Specialization is NVARCHAR(200) (line 152)
    [StringLength(200, ErrorMessage = "সর্বোচ্চ ২০০ অক্ষর / Maximum 200 characters")]
    public string? Specialization { get; set; }

    // No dedicated DB column persisted today (rendered profile data) — app-level guard only.
    [StringLength(500, ErrorMessage = "সর্বোচ্চ ৫০০ অক্ষর / Maximum 500 characters")]
    public string? ChamberAddress { get; set; }
}
```

`LawyerReviewViewModel` (Comments is NVARCHAR(MAX) — guard for review comment quality; Decision is a closed set):

```csharp
public class LawyerReviewViewModel
{
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ContentDraft { get; set; } = string.Empty;
    public string? EditedContent { get; set; }

    [RegularExpression("^(Approved|EditedApproved|Rejected)$",
        ErrorMessage = "সিদ্ধান্ত অবশ্যই Approved, EditedApproved অথবা Rejected হতে হবে / Decision must be Approved, EditedApproved or Rejected")]
    public string Decision { get; set; } = "Approved";

    // FR-14: review comments are mandatory; app-level cap (DB column is NVARCHAR(MAX)).
    [Required(ErrorMessage = "পর্যালোচনার মন্তব্য প্রয়োজন / Review comments are required")]
    [StringLength(4000, ErrorMessage = "সর্বোচ্চ ৪০০০ অক্ষর / Maximum 4000 characters")]
    public string Comments { get; set; } = string.Empty;
}
```

`RegisterViewModel.cs` — give the two lawyer-only optional fields the same DB-mirroring guards (both are currently unguarded):

```csharp
    [Display(Name = "বার রেজিস্ট্রেশন নম্বর / Bar Reg No (Lawyers only)")]
    [StringLength(100, ErrorMessage = "সর্বোচ্চ ১০০ অক্ষর / Maximum 100 characters")]
    public string? BarRegistrationNumber { get; set; }

    [Display(Name = "বিশেষজ্ঞতা / Specialization (Lawyers only)")]
    [StringLength(200, ErrorMessage = "সর্বোচ্চ ২০০ অক্ষর / Maximum 200 characters")]
    public string? Specialization { get; set; }
```

`PasswordResetViewModels.cs` — mirror the Identity password policy on `ResetPasswordViewModel` (currently only `[Required, MinLength(8)]`):

```csharp
public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 8)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,100}$",
        ErrorMessage = "পাসওয়ার্ডে ছোট হাতের অক্ষর, বড় হাতের অক্ষর, সংখ্যা ও বিশেষ চিহ্ন থাকতে হবে / Password must contain upper, lower, number, and special character")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Wire client-side unobtrusive validation into the case form**

`Views/Case/Submit.cshtml` renders form fields with `asp-for` but does not load the validation scripts (verified — no `_ValidationScriptsPartial` reference in the file; the partial exists at `Views/Shared/_ValidationScriptsPartial.cshtml` and jQuery libs are vendored). Append at the end of the page's content section:

```html
<partial name="_ValidationScriptsPartial" />
```

Then confirm the form's field elements keep their `asp-for` attributes (unobtrusive `data-val-*` attributes are generated by the tag helper from the annotations above). Do NOT add `maxlength` changes — the existing `maxlength="250"` / `maxlength="5000"` already match the new annotations.

- [ ] **Step 7: Run the validation + XSS tests, then the full unit suite**

Run: `dotnet test tests/MuktoAin.UnitTests`
Expected: PASS. `RegisterViewModelTests`/`ProfileViewModelTests` must remain green (only additive attributes were placed on optional fields — `StringLength` on a `null`-able optional field does not make empty invalid).

- [ ] **Step 8: Run the integration suite (validation must not break the happy-path submit)**

Run: `dotnet test tests/MuktoAin.IntegrationTests --filter "FullyQualifiedName~CaseApiTests"`
Expected: PASS — `Submit_Post_ValidAnonymousSubmission_RedirectsToResultWithTrackingCode` uses values inside the new limits.

- [ ] **Step 9: Record completion in plans/Dependency_plan.md**

Flip `- [ ] **[A-3.11]** Input Validation Hardening Across All Forms — *Arpita*` (line ~291) to `- [x]` wrapped in `~~...~~`.

---

### Task 7: A-3.12 — Error handling + bilingual user-friendly error pages & API error DTOs

**Files:**
- Create: `src/MuktoAin.Web/Models/ApiErrorDto.cs`
- Modify: `src/MuktoAin.Web/Controllers/ChatController.cs` (catch-block friendly error, ~line 249)
- Modify: `src/MuktoAin.Web/Controllers/PaymentController.cs:81-85,115-119` (500 responses)
- Modify: `src/MuktoAin.Web/Controllers/DocumentController.cs` (honor existing TempData pattern — no change needed; add XML doc only if touched)
- Test: `tests/MuktoAin.UnitTests/Models/ApiErrorDtoTests.cs`
- (Page-error coverage already added in Task 1's `WebHostSmokeTests`)

**Interfaces:**
- Consumes: verified existing shapes — PaymentController errors use `new { success = false, message }` (src/MuktoAin.Web/Controllers/PaymentController.cs:41,51,84,118); ChatController errors use `new { error }` (src/MuktoAin.Web/Controllers/ChatController.cs:56,207,209,249); page errors: `HomeController.Error/AccessDenied/NotFound/ServerError` + `UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}")` (Program.cs:254) — all present from the Aug 2026 hardening, so the PAGE half of A-3.12 is wiring/tests, not new pages.
- Produces: `ApiErrorDto` record + `ApiErrors.Json(Controller, int status, string errorEn, string errorBn, string? detail)` helper; unified fetch-endpoint error shape `{ success = false, error = "<EN>", errorBn = "<BN>", message = "<friendly detail>" }`. `detail` never contains raw exception text for user-facing 500s (no `ex.Message` leak).

- [ ] **Step 1: Write the failing tests**

`tests/MuktoAin.UnitTests/Models/ApiErrorDtoTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Web.Models;
using Xunit;

namespace MuktoAin.UnitTests.Models;

public class ApiErrorDtoTests
{
    [Fact]
    public void Friendly_BuildsBilingualErrorShape()
    {
        var dto = ApiErrorDto.Friendly(
            "Sorry — an answer could not be generated right now. Please try again in a moment.",
            "দুঃখিত — এই মুহূর্তে উত্তর তৈরি করা যায়নি। কিছুক্ষণ পর আবার চেষ্টা করুন।");

        Assert.False(dto.Success);
        Assert.Contains("could not be generated", dto.Error);
        Assert.Contains("তৈরি করা যায়নি", dto.ErrorBn);
        Assert.Null(dto.Message);
    }

    [Fact]
    public void Friendly_OptionalDetail_IsCarriedInMessage()
    {
        var dto = ApiErrorDto.Friendly("en-text", "bn-text", detail: "CASE_NOT_FOUND");
        Assert.Equal("CASE_NOT_FOUND", dto.Message);
        Assert.Equal("en-text", dto.Error);
    }

    [Fact]
    public void ApiErrors_Helper_SetsStatusCodeAndJsonShape()
    {
        var controller = new Microsoft.AspNetCore.Mvc.Controller
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };

        var result = ApiErrors.BadRequest(
            controller: controller,
            errorEn: "Question and session id are required.",
            errorBn: "প্রশ্ন ও সেশন আইডি প্রয়োজন।");

        Assert.Equal(400, controller.Response.StatusCode);
        var json = Assert.IsType<JsonResult>(result);
        var value = Assert.IsType<ApiErrorDto>(json.Value);
        Assert.False(value.Success);
        Assert.Contains("প্রয়োজন", value.ErrorBn);
    }
}
```

- [ ] **Step 2: Create the API error DTO + helpers**

`src/MuktoAin.Web/Models/ApiErrorDto.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace MuktoAin.Web.Models;

/// <summary>
/// Unified JSON error shape for the fetch-based endpoints (Chat, Payment, Case
/// SubmitOptions, Document). Client JS reads `success` and renders `error`
/// (English) or `errorBn` (Bangla) per the mkt-lang toggle. `message` carries a
/// short non-sensitive detail code — NEVER raw exception text (A-3.12: no
/// internal error leakage to the client).
/// </summary>
public record ApiErrorDto(bool Success, string Error, string? ErrorBn = null, string? Message = null)
{
    public static ApiErrorDto Friendly(string errorEn, string errorBn, string? detail = null) =>
        new(false, errorEn, errorBn, detail);
}

public static class ApiErrors
{
    public const string AiUnavailableEn =
        "Sorry — an answer could not be generated right now. Please try again in a moment.";
    public const string AiUnavailableBn =
        "দুঃখিত — এই মুহূর্তে উত্তর তৈরি করা যায়নি। কিছুক্ষণ পর আবার চেষ্টা করুন।";

    public const string PaymentFailedEn =
        "The payment could not be completed. No amount was charged — please try again.";
    public const string PaymentFailedBn =
        "পেমেন্ট সম্পন্ন করা যায়নি। আপনার কোনো টাকা কাটা হয়নি — কিছুক্ষণ পর আবার চেষ্টা করুন।";

    public static JsonResult BadRequest(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 400, errorEn, errorBn, detail);

    public static JsonResult NotFound(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 404, errorEn, errorBn, detail);

    public static JsonResult ServerError(Controller controller, string errorEn, string errorBn, string? detail = null) =>
        Result(controller, 500, errorEn, errorBn, detail);

    private static JsonResult Result(Controller controller, int statusCode, string errorEn, string errorBn, string? detail)
    {
        controller.Response.StatusCode = statusCode;
        return new JsonResult(ApiErrorDto.Friendly(errorEn, errorBn, detail));
    }
}
```

> Create `tests/MuktoAin.UnitTests/Models/` directory if it does not exist (it doesn't today — UnitTests has Auth/Controllers/Repositories/Services/ViewModels only).

- [ ] **Step 3: Swap ChatController's raw-exception leak for the friendly shape**

In `src/MuktoAin.Web/Controllers/ChatController.cs`, find the catch block near line 249:

```csharp
            return Json(new { error = ex.Message });
```

Replace with (keeping the same HTTP 200 contract the chat JS already handles for `error`):

```csharp
            return Json(new
            {
                success = false,
                error = MuktoAin.Web.Models.ApiErrors.AiUnavailableEn,
                errorBn = MuktoAin.Web.Models.ApiErrors.AiUnavailableBn,
                message = "AI_REQUEST_FAILED", // stable detail code, no ex.Message leak
            });
```

Add `using MuktoAin.Web.Models;` at the top of the controller (or fully-qualify as above).

- [ ] **Step 4: Swap PaymentController's 500 internals for the friendly shape**

In `src/MuktoAin.Web/Controllers/PaymentController.cs`, both catch blocks (Honorarium ~line 81, TopUp ~line 115) currently return `StatusCode(500, new { success = false, message = ex.Message })`. Replace each with:

```csharp
            return StatusCode(500, new
            {
                success = false,
                message = "পেমেন্ট প্রক্রিয়াকরণে সমস্যা হয়েছে / The payment could not be processed.",
                error = MuktoAin.Web.Models.ApiErrors.PaymentFailedEn,
                errorBn = MuktoAin.Web.Models.ApiErrors.PaymentFailedBn,
            });
```

Keep the `_logger.LogError(ex, ...)` calls — full detail goes to the server log, never to the client. Do NOT change the 400/404 shapes (`{ success, message }`) — those messages are already user-facing and Task 4's tests pin them.

- [ ] **Step 5: Verify the page-error pipeline end-to-end (already wired, confirm + document)**

No new files needed — `HomeController` (src/MuktoAin.Web/Controllers/HomeController.cs:33-62) already serves `AccessDenied` (403), `NotFound` (404), `ServerError` (500), and routes `Error(statusCode)` re-executions, with bilingual `data-bn`/`data-en` copy in `Views/Home/{AccessDenied,NotFound,ServerError}.cshtml`. Task 1's `WebHostSmokeTests` pins the status codes. Add one content assertion to `WebHostSmokeTests`:

```csharp
    [Fact]
    public async Task Error_Pages_Render_Bilingual_Copy()
    {
        var client = _factory.CreateClient();
        var notFound = await client.GetAsync("/Home/NotFound");
        var html = await notFound.Content.ReadAsStringAsync();
        Assert.Contains("data-bn", html);
        Assert.Contains("data-en", html);
    }
```

- [ ] **Step 6: Run everything**

Run: `dotnet test`
Expected: PASS. If `ChatControllerTests` or `PaymentControllerTests` unit tests asserted the old `new { error = ex.Message }` / `message = ex.Message` 500 shape, update those specific assertions to the new friendly shape (grep first: `rg -n "ex.Message" tests/MuktoAin.UnitTests/Controllers/`) — the new shape is the contract going forward.

- [ ] **Step 7: Record completion in plans/Dependency_plan.md**

Flip `- [ ] **[A-3.12]** Error Handling Improvements + User-Friendly Error Pages — *Arpita*` (line ~292) to `- [x]` wrapped in `~~...~~`.

---

## Self-Review Checklist (executor runs at the end)

- [ ] `dotnet test` — full solution green (UnitTests 314+ and IntegrationTests).
- [ ] `dotnet build` — zero warnings from new code (nullable-enabled projects).
- [ ] No `ex.Message` returned in any client-facing JSON (grep: `rg -n "ex.Message" src/MuktoAin.Web/Controllers/` — remaining hits only in PaymentController BadRequest/NotFound literal messages, which are static strings, not exceptions).
- [ ] Every generated-document path (English AND Bangla-only) still stamps a disclaimer (surface 3 of 3).
- [ ] `plans/Dependency_plan.md` checkboxes A-3.7, A-3.9, A-3.11, A-3.12 flipped `[x]` + strikethrough.
- [ ] NO commits made (AGENTS.md §6) — all changes left in the working tree for Shads.

## Open Questions (raise with Shads before/while executing)

1. **E-3.4 blocker (A-3.7/A-3.11):** Erin's controller↔service wiring (`plans/Dependency_plan.md:141`) is unchecked. The plan's Task 1 smoke test doubles as the E-3.4 verification — if it fails on missing wiring, stop and coordinate rather than implementing Erin's tasks.
2. **A-3.9 behavior change:** routing `Language == "bn"` cases to Bangla-only templates changes what existing "bn" cases render on the Result page. Confirm Shads wants this live (the plan assumes yes — Bangla-first product).
3. **`AppDbContext` DbSet names** in `tests/MuktoAin.IntegrationTests/Helpers/TestData.cs` must be verified against `src/MuktoAin.Infrastructure/Data/AppDbContext.cs` (noted inline).
4. **`IVectorStore` / `VectorSearchResult` exact signatures** in the stub must mirror `Domain/Interfaces/IVectorStore.cs` and `Domain/Models/VectorSearchResult.cs` (noted inline; `RagRetrievalSmokeTests.cs` shows usage).
