# Frontend Redesign Implementation Plan — PART B (Lawyer Surface + Admin Console + Account & Public Pages)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **EXECUTOR NOTE:** This plan is written for **Gemini 3.8 Flash (Antigravity)** with ZERO creative freedom. Follow every step exactly — code blocks are complete and final. Do NOT redesign, rename, restructure, or deviate. All signatures verified against the codebase on 2026-09-05. Steps marked **[OPENCODE VERIFY]** are reserved for OpenCode — Antigravity skips them.
>
> **INDEPENDENCE:** Part B shares NO files with Part A. The two plans can run in parallel. Part B touches: LawyerController/Views, AdminController/Views, AccountController/Views, SearchController/View, CategoryController/Views, Home/About, DocumentController, _Layout, profile/forgot/reset views. It does NOT touch CaseController, ChatController, ChatService, or Home/Index.

**Goal:** Real-data lawyer review flow (queue with claim-lock, redline workspace, status page), the full 8-page admin console (users, lawyers, corpus, scenarios, categories, AI logs, transactions) with zero mock data and zero dead buttons, real forgot/reset password, profile logout-everywhere, search act-filter fix, category chat-launchers, About absorbing landing content, template error pages, PDF export wiring.

**Architecture:** New `LawyerReviewService` (claim-based queue + decisions) and `PaymentService` in Application; controllers gain real services replacing MockData; each admin page = one view + controller actions backed by existing repos. Everything additive except the explicit MockData removals.

**Tech Stack:** Same as Part A. Existing CSS classes only (same design-system constraint).

**Spec:** `docs/superpowers/specs/2026-09-05-frontend-redesign-design.md` + `UI.md` v2 + `.agent/spec/requirements.md` (FR-1..24).

## Global Constraints (identical to Part A — apply to EVERY task)

1. **Never break existing flows.** Only replace what a task explicitly says to replace.
2. **Clean Architecture layering** per task file paths.
3. **No migrations** — schema additions (if any) go in `scripts/09_part_b_tables.sql` (Task B1) — executed manually in SSMS by the human.
4. **Bangla-first** + `data-bn`/`data-en`.
5. **Lucide icons only**, no emoji icons, no new frameworks.
6. **Existing CSS classes only** (same list as Part A constraint 6; new classes introduced by this plan: `.compare-pane`, `.compare-original`, `.compare-edit`, `.stale-badge`, `.health-dot`, `.sandbox-badge`, `.drop-zone`, `.pipeline-step` — CSS provided in Task B1).
7. **Antiforgery on every form POST** + `[ValidateAntiForgeryToken]`.
8. **Commit after every green step.**
9. **Build:** `dotnet build` must succeed before any commit.
10. Do NOT edit `plans/Dependency_plan.md` — OpenCode handles it.

---

### Task B1: LawyerReviewService (queue, claim, decisions) + Part B schema

**Files:**
- Create: `src/MuktoAin.Application/Services/LawyerReviewService.cs`
- Create: `src/MuktoAin.Application/DTOs/LawyerReviewDto.cs`
- Create: `scripts/09_part_b_tables.sql`

**Interfaces:**
- Consumes: `IRepository<GeneratedDocument>` (fields incl. `AssignedLawyerProfileId`, `ClaimedAt`, `VersionNo`, `CitizenEdited`, `Status`, `ContentDraft`, `ContentFinal`), `IRepository<LawyerProfile>`, `IRepository<LawyerReview>`, `ICaseRepository.GetWithDocumentsAsync`, `DocumentService.UpdateStatusAsync(int, DocumentStatus, string?)` (verified — sets ContentFinal + status), `CaseService.TransitionStatusAsync(int, CaseStatus)`; enums `DocumentStatus { Draft=0, UnderReview=1, Approved=2, Rejected=3 }`, `ReviewDecision { Approved=0, EditedApproved=1, Rejected=2 }`, `CaseStatus { Submitted=0, UnderReview=1, Finalized=2 }`.
- Produces: `LawyerReviewService.GetQueueAsync()` → `IReadOnlyList<QueueItemDto>`; `ClaimAsync(int documentId, int lawyerProfileId)` → bool; `GetForReviewAsync(int documentId)` → `ReviewWorkspaceDto?`; `SubmitReviewAsync(SubmitReviewDto)` → bool. Used by B2/B3.

- [ ] **Step 1: Create scripts/09_part_b_tables.sql**

```sql
/* ============================================================
   MuktoAin — Part B schema additions (2026-09-05)
   Rejection reason surface: LAWYER_REVIEW already stores Comments;
   nothing new is REQUIRED. This script exists only to backfill
   AssignedLawyerProfileId on documents already in review (demo data)
   and is a no-op on a clean database. IDEMPOTENT.
   ============================================================ */
SET NOCOUNT ON;
GO
-- Intentionally minimal: the redesign's Part B needs no new tables.
-- (Payments schema landed in 08_redesign_tables.sql, Part A.)
PRINT '09_part_b_tables.sql: nothing to do (no-op).';
GO
```
(Kept as a file so the "schema changes = numbered SSMS scripts" convention holds and Part B is self-contained.)

- [ ] **Step 2: Create the DTOs**

`src/MuktoAin.Application/DTOs/LawyerReviewDto.cs`:
```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record QueueItemDto(
    int DocumentId,
    int CaseId,
    string CaseTitle,
    string CategoryName,
    string DistrictName,
    DocumentStatus Status,
    bool CitizenEdited,
    int VersionNo,
    string? ClaimedBy,       // lawyer display name when claimed by someone
    DateTime CreatedAt,
    DateTime? ClaimedAt,
    bool CanOpen             // false when claimed by another lawyer
);

public record ReviewWorkspaceDto(
    int DocumentId,
    int CaseId,
    string CaseTitle,
    string CategoryName,
    string DistrictName,
    string CitizenNarrative, // decrypted case description (PII — session-scoped)
    IReadOnlyList<CitedSectionDto> Citations,
    string OriginalDraft,     // ContentDraft — immutable
    string? CitizenEditedDraft, // ContentFinal if CitizenEdited
    int VersionNo,
    bool CitizenEdited
);

public record SubmitReviewDto(
    int DocumentId,
    int LawyerProfileId,
    ReviewDecision Decision,
    string Comments,          // MANDATORY for every decision; rejection shows to citizen
    string? EditedContent    // required when Decision == EditedApproved
);
```

- [ ] **Step 3: Create LawyerReviewService**

`src/MuktoAin.Application/Services/LawyerReviewService.cs`:
```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

// FR-13/14/23: specialization-pool queue with claim-based optimistic lock
// (one active review per lawyer; opening a doc claims it), decisions with
// mandatory comments. Rejection reason flows to the citizen's case page and
// (via the chat return link) into the salvage conversation.
public class LawyerReviewService
{
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly IRepository<LawyerReview> _reviewRepo;
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IRepository<CaseActReference> _refRepo;
    private readonly IRepository<ActSection> _sectionRepo;
    private readonly IRepository<Act> _actRepo;
    private readonly IEncryptionService _encryptionService;
    private readonly CaseService _caseService;

    public LawyerReviewService(
        IRepository<GeneratedDocument> docRepo,
        IRepository<LawyerReview> reviewRepo,
        IRepository<LawyerProfile> profileRepo,
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo,
        IRepository<CaseActReference> refRepo,
        IRepository<ActSection> sectionRepo,
        IRepository<Act> actRepo,
        IEncryptionService encryptionService,
        CaseService caseService)
    {
        _docRepo = docRepo;
        _reviewRepo = reviewRepo;
        _profileRepo = profileRepo;
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
        _refRepo = refRepo;
        _sectionRepo = sectionRepo;
        _actRepo = actRepo;
        _encryptionService = encryptionService;
        _caseService = caseService;
    }

    // Queue = documents in UnderReview, oldest-first (SLA age shown by the view).
    public async Task<IReadOnlyList<QueueItemDto>> GetQueueAsync()
    {
        var docs = (await _docRepo.GetAllAsync())
            .Where(d => d.Status == DocumentStatus.UnderReview)
            .OrderBy(d => d.CreatedAt)
            .ToList();

        var result = new List<QueueItemDto>();
        foreach (var d in docs)
        {
            var c = await _caseRepo.GetWithDocumentsAsync(d.CaseId);
            if (c == null) continue;
            var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
            var district = await _districtRepo.GetByIdAsync(c.DistrictId);
            string? claimedBy = null;
            if (d.AssignedLawyerProfileId.HasValue)
            {
                var p = await _profileRepo.GetByIdAsync(d.AssignedLawyerProfileId.Value);
                claimedBy = p?.BarRegistrationNumber; // admin-safe identifier
            }
            result.Add(new QueueItemDto(
                d.DocumentId,
                d.CaseId,
                SafeDecrypt(c.Title),
                category?.Name ?? "",
                district?.Name ?? "",
                d.Status,
                d.CitizenEdited,
                d.VersionNo,
                claimedBy,
                d.CreatedAt,
                d.ClaimedAt,
                CanOpen: !d.AssignedLawyerProfileId.HasValue));
        }
        return result;
    }

    // Claim = optimistic lock. Returns false if another lawyer already holds it.
    public async Task<bool> ClaimAsync(int documentId, int lawyerProfileId)
    {
        var d = await _docRepo.GetByIdAsync(documentId);
        if (d == null || d.Status != DocumentStatus.UnderReview) return false;
        if (d.AssignedLawyerProfileId.HasValue
            && d.AssignedLawyerProfileId != lawyerProfileId) return false;

        d.AssignedLawyerProfileId = lawyerProfileId;
        d.ClaimedAt = DateTime.UtcNow;
        await _docRepo.SaveChangesAsync();
        return true;
    }

    public async Task<ReviewWorkspaceDto?> GetForReviewAsync(int documentId)
    {
        var d = await _docRepo.GetByIdAsync(documentId);
        if (d == null) return null;
        var c = await _caseRepo.GetWithDocumentsAsync(d.CaseId);
        if (c == null) return null;

        var category = await _categoryRepo.GetByIdAsync(c.CategoryId);
        var district = await _districtRepo.GetByIdAsync(c.DistrictId);

        var citations = new List<CitedSectionDto>();
        var refs = (await _refRepo.GetAllAsync()).Where(r => r.CaseId == d.CaseId);
        foreach (var r in refs)
        {
            var s = await _sectionRepo.GetByIdAsync(r.SectionId);
            var a = s != null ? await _actRepo.GetByIdAsync(s.ActId) : null;
            citations.Add(new CitedSectionDto(
                r.SectionId,
                a?.Title ?? "",
                s?.SectionNumber ?? "",
                s?.SectionText ?? "",
                (float)r.RelevanceScore,
                r.RetrievalMethod.ToString(),
                a?.ActNumber ?? "",
                a?.Year ?? 0));
        }

        return new ReviewWorkspaceDto(
            d.DocumentId,
            d.CaseId,
            SafeDecrypt(c.Title),
            category?.Name ?? "",
            district?.Name ?? "",
            SafeDecrypt(c.Description),
            citations,
            d.ContentDraft,
            d.CitizenEdited ? d.ContentFinal : null,
            d.VersionNo,
            d.CitizenEdited);
    }

    public async Task<bool> SubmitReviewAsync(SubmitReviewDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comments)) return false; // mandatory
        if (dto.Decision == ReviewDecision.EditedApproved
            && string.IsNullOrWhiteSpace(dto.EditedContent)) return false;

        var d = await _docRepo.GetByIdAsync(dto.DocumentId);
        if (d == null || d.Status != DocumentStatus.UnderReview) return false;
        if (d.AssignedLawyerProfileId.HasValue
            && d.AssignedLawyerProfileId != dto.LawyerProfileId) return false;
        // Auto-claim if somehow unclaimed (defensive)
        d.AssignedLawyerProfileId = dto.LawyerProfileId;

        var review = new LawyerReview
        {
            DocumentId = dto.DocumentId,
            LawyerProfileId = dto.LawyerProfileId,
            Decision = dto.Decision,
            Comments = dto.Comments,
            ReviewedAt = DateTime.UtcNow
        };
        await _reviewRepo.AddAsync(review);
        await _reviewRepo.SaveChangesAsync();

        switch (dto.Decision)
        {
            case ReviewDecision.Approved:
                await _documentUpdateAsync(d, DocumentStatus.Approved, null);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.EditedApproved:
                await _documentUpdateAsync(d, DocumentStatus.EditedApproved, dto.EditedContent);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.Finalized);
                break;
            case ReviewDecision.Rejected:
                await _documentUpdateAsync(d, DocumentStatus.Rejected, null);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.UnderReview);
                // UnderReview + Rejected document = citizen edit & resubmit loop.
                break;
        }

        // Flag unread activity for the citizen (unread dot on My Cases)
        var c2 = await _caseRepo.GetByIdAsync(d.CaseId);
        if (c2 != null)
        {
            c2.HasUnreadActivity = true;
            await _caseRepo.SaveChangesAsync();
        }
        return true;
    }

    private async Task _documentUpdateAsync(GeneratedDocument d, DocumentStatus status, string? edited)
    {
        // Mirrors DocumentService.UpdateStatusAsync semantics (verified):
        // EditedApproved -> ContentFinal = edited; Approved -> final = draft.
        d.Status = status;
        if (status == DocumentStatus.EditedApproved && edited != null)
            d.ContentFinal = edited;
        else if (status == DocumentStatus.Approved)
            d.ContentFinal = d.ContentDraft;
        await _docRepo.SaveChangesAsync();
    }

    private string SafeDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try { return _encryptionService.Decrypt(value); }
        catch { return value; }
    }
}
```

**Note:** `DocumentStatus.EditedApproved` does NOT exist in the enum (verified: `Draft=0, UnderReview=1, Approved=2, Rejected=3`). The system's existing convention (see `StatusText.cs` — "edited statuses like 'EditedApproved' never exist as enum members") is that lawyer-edited approvals are stored as `Approved` with `ContentFinal != ContentDraft`. **Fix the switch accordingly:**
```csharp
            case ReviewDecision.EditedApproved:
                await _documentUpdateAsync(d, DocumentStatus.Approved, dto.EditedContent);
                await _caseService.TransitionStatusAsync(d.CaseId, CaseStatus.Finalized);
                break;
```
i.e. replace `DocumentStatus.EditedApproved` with `DocumentStatus.Approved` in BOTH the `_documentUpdateAsync` call and its guard (`status == DocumentStatus.EditedApproved` becomes `edited != null`). The decision distinction lives in `LAWYER_REVIEW.Decision`, which is what views read.

- [ ] **Step 4: Register in DI**

In `src/MuktoAin.Web/Program.cs`, immediately after `builder.Services.AddScoped<ChatService>();` (added by Part A — if Part A hasn't run yet, place after `LawyerVerificationService` instead; either compiles) add:
```csharp
// Part B: lawyer review flow
builder.Services.AddScoped<LawyerReviewService>();
```

- [ ] **Step 5: Build**

Run: `dotnet build` — expected 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/MuktoAin.Application/Services/LawyerReviewService.cs src/MuktoAin.Application/DTOs/LawyerReviewDto.cs scripts/09_part_b_tables.sql src/MuktoAin.Web/Program.cs
git commit -m "feat(app): LawyerReviewService — pool queue, claim lock, decisions w/ mandatory comments"
```

---

### Task B2: Lawyer Queue + Status pages (real data)

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/LawyerController.cs` (full replace)
- Create: `src/MuktoAin.Web/Views/Lawyer/Status.cshtml`
- Modify (full replace): `src/MuktoAin.Web/Views/Lawyer/Queue.cshtml`

**Interfaces:**
- Consumes: `LawyerReviewService` (B1); `LawyerVerificationService.GetPendingApplicationsAsync/VerifyAsync(int lawyerProfileId, int adminUserId, bool approve)` (verified); `IRepository<LawyerProfile>`; `UserManager<User>`; `LawyerReviewService.GetQueueAsync/ClaimAsync`.
- Produces: `/Lawyer/Queue` (real queue, claim-on-open), `/Lawyer/Status` (pending/rejected states + resubmit), `/Lawyer/Claim/{documentId}` (POST claim → redirects to Review), `/Lawyer/Resubmit` (POST). Lawyer nav shows Status instead of Queue when unverified (layout change in B8).

- [ ] **Step 1: Full-replace LawyerController.cs**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

// FR-13/14/15/23. Review is LAWYER-ONLY (admin manages, never reviews).
[Authorize(Roles = "Lawyer")]
public class LawyerController : Controller
{
    private readonly LawyerReviewService _reviewService;
    private readonly LawyerVerificationService _verificationService;
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly UserManager<User> _userManager;

    public LawyerController(
        LawyerReviewService reviewService,
        LawyerVerificationService verificationService,
        IRepository<LawyerProfile> profileRepo,
        UserManager<User> userManager)
    {
        _reviewService = reviewService;
        _verificationService = verificationService;
        _profileRepo = profileRepo;
        _userManager = userManager;
    }

    private async Task<LawyerProfile?> MyProfileAsync()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var userId)) return null;
        var all = await _profileRepo.GetAllAsync();
        return all.FirstOrDefault(p => p.UserId == userId);
    }

    // Unverified lawyers land here instead of the queue.
    [HttpGet]
    public async Task<IActionResult> Status()
    {
        var profile = await MyProfileAsync();
        if (profile == null) return NotFound();
        var user = await _userManager.FindByIdAsync(profile.UserId.ToString());

        var vm = new LawyerStatusViewModel
        {
            LawyerName = user?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            Specialization = profile.Specialization ?? "",
            Status = profile.VerificationStatus.ToString(),
            SubmittedAt = profile.VerifiedAt ?? DateTime.UtcNow // display only
        };
        return View(vm);
    }

    // Rejected lawyers resubmit their bar number from the Status page.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resubmit(LawyerStatusViewModel vm)
    {
        var profile = await MyProfileAsync();
        if (profile == null) return NotFound();
        if (profile.VerificationStatus != VerificationStatus.Rejected) return Forbid();
        if (string.IsNullOrWhiteSpace(vm.BarRegistrationNumber))
        {
            ModelState.AddModelError(nameof(vm.BarRegistrationNumber), "বার নম্বর আবশ্যক / Bar number required");
            return RedirectToAction(nameof(Status));
        }

        profile.BarRegistrationNumber = vm.BarRegistrationNumber;
        if (!string.IsNullOrWhiteSpace(vm.Specialization))
            profile.Specialization = vm.Specialization;
        profile.VerificationStatus = VerificationStatus.Pending;
        await _profileRepo.SaveChangesAsync();

        TempData["Success"] = "আবেদন পুনরায় জমা হয়েছে — ২৪–৪৮ ঘণ্টার মধ্যে যাচাই হবে।";
        TempData["SuccessEn"] = "Application resubmitted — verification typically takes 24–48h.";
        return RedirectToAction(nameof(Status));
    }

    // Queue: documents in the pool, oldest-first (SLA).
    [HttpGet]
    public async Task<IActionResult> Queue()
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        var queue = await _reviewService.GetQueueAsync();

        var vm = new LawyerQueueViewModel
        {
            LawyerName = (await _userManager.FindByIdAsync(profile.UserId.ToString()))?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            Specialization = profile.Specialization ?? "",
            PendingCount = queue.Count,
            Items = queue.Select(q => new LawyerQueueItemViewModel
            {
                DocumentId = q.DocumentId,
                CaseId = q.CaseId,
                CaseTitle = q.CaseTitle,
                CategoryName = q.CategoryName,
                DistrictName = q.DistrictName,
                CitizenEdited = q.CitizenEdited,
                VersionNo = q.VersionNo,
                ClaimedBy = q.ClaimedBy,
                WaitingHours = (int)Math.Max(0, (DateTime.UtcNow - q.CreatedAt).TotalHours),
                CanOpen = q.CanOpen
            }).ToList()
        };
        return View(vm);
    }

    // Claim-on-open (optimistic lock) then straight into the workspace.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim(int documentId)
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        var ok = await _reviewService.ClaimAsync(documentId, profile.LawyerProfileId);
        if (!ok)
        {
            TempData["Error"] = "অন্য আইনজীবী এটি নিয়েছেন — সারিতে ফিরে যান।";
            TempData["ErrorEn"] = "Another lawyer claimed this — back to the queue.";
            return RedirectToAction(nameof(Queue));
        }
        return RedirectToAction(nameof(Review), new { id = documentId });
    }

    [HttpGet]
    public async Task<IActionResult> Review(int id)
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        var ws = await _reviewService.GetForReviewAsync(id);
        if (ws == null) return NotFound();

        var doc = ws; // workspace dto
        var vm = new LawyerReviewViewModel
        {
            DocumentId = doc.DocumentId,
            CaseId = doc.CaseId,
            CaseTitle = doc.CaseTitle,
            CategoryName = doc.CategoryName,
            ContentDraft = doc.OriginalDraft,
            EditedContent = doc.CitizenEditedDraft ?? doc.OriginalDraft,
            Decision = nameof(ReviewDecision.EditedApproved),
            Comments = string.Empty
        };
        // Context extras for the view
        ViewData["DistrictName"] = doc.DistrictName;
        ViewData["CitizenNarrative"] = doc.CitizenNarrative;
        ViewData["Citations"] = doc.Citations;
        ViewData["VersionNo"] = doc.VersionNo;
        ViewData["CitizenEdited"] = doc.CitizenEdited;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(LawyerReviewViewModel vm)
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        if (!Enum.TryParse<ReviewDecision>(vm.Decision, out var decision))
            decision = ReviewDecision.EditedApproved;

        var ok = await _reviewService.SubmitReviewAsync(new SubmitReviewDto(
            vm.DocumentId,
            profile.LawyerProfileId,
            decision,
            vm.Comments ?? string.Empty,
            decision == ReviewDecision.EditedApproved ? vm.EditedContent : null));

        if (!ok)
        {
            TempData["Error"] = "পর্যালোচনা সংরক্ষণ হয়নি — মন্তব্য আবশ্যক (এবং সম্পাদনার সাথে অনুমোদনের ক্ষেত্রে সম্পাদিত পাঠ্য)।";
            TempData["ErrorEn"] = "Review not saved — comments are mandatory (and edited text for approve-with-edits).";
            return RedirectToAction(nameof(Review), new { id = vm.DocumentId });
        }

        TempData["Success"] = "পর্যালোচনা সম্পন্ন — পরবর্তী নথিতে যাচ্ছেন।";
        TempData["SuccessEn"] = "Review saved — advancing to the next document.";
        return RedirectToAction(nameof(Queue));
    }
}
```
**Important:** `LawyerVerificationService` is in namespace `MuktoAin.Application.Services` (verified). `DocumentStatus.EditedApproved` does NOT exist — the controller never references it. `SubmitReviewAsync` stores EditedApproved decisions as `DocumentStatus.Approved` per B1's note.

- [ ] **Step 2: Add the lawyer ViewModels**

In `src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs`, ADD these classes (keep everything existing; note the existing `LawyerReviewViewModel` already has `DocumentId, CaseId, CaseTitle, CategoryName, ContentDraft, EditedContent, Decision, Comments` — keep it as-is):
```csharp
public class LawyerStatusViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending / Approved / Rejected
    public DateTime SubmittedAt { get; set; }
}

public class LawyerQueueViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public List<LawyerQueueItemViewModel> Items { get; set; } = new();
}

public class LawyerQueueItemViewModel
{
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public bool CitizenEdited { get; set; }
    public int VersionNo { get; set; }
    public string? ClaimedBy { get; set; }
    public int WaitingHours { get; set; }
    public bool CanOpen { get; set; }
}
```

- [ ] **Step 3: Full-replace Views/Lawyer/Queue.cshtml**

```html
@model LawyerQueueViewModel
@using MuktoAin.Web.Controllers
@{
    ViewData["Title"] = "রিভিউ কিউ — মুক্ত আইন";
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <span data-bn="রিভিউ কিউ" data-en="Review Queue">রিভিউ কিউ</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="file-check-2"></i> FR-13 · FR-23</span>
        <h1 class="page-title" data-bn="পর্যালোচনা কিউ" data-en="Review Queue">পর্যালোচনা কিউ</h1>
        <p class="page-sub">
            @Model.LawyerName · Bar #@Model.BarRegistrationNumber · @Model.Specialization
        </p>
    </div>

    <!-- KPI strip -->
    <div class="grid grid-3" style="margin-bottom: 20px">
        <div class="card">
            <span class="kicker"><i data-lucide="inbox"></i> Pending</span>
            <div style="font-size: 32px; font-weight: 700">@Model.PendingCount</div>
        </div>
        <div class="card">
            <span class="kicker"><i data-lucide="badge-check"></i> Bar Verified</span>
            <div style="font-size: 18px; font-weight: 700; margin-top: 8px">@Model.BarRegistrationNumber</div>
        </div>
        <div class="card">
            <span class="kicker"><i data-lucide="layers"></i> Specialization</span>
            <div style="font-size: 18px; font-weight: 700; margin-top: 8px">@Model.Specialization</div>
        </div>
    </div>

    @if (!Model.Items.Any())
    {
        <div class="empty-state">
            <div class="item-ico"><i data-lucide="coffee"></i></div>
            <h3 data-bn="কিউ খালি — ধন্যবাদ!" data-en="Queue empty — thank you!">কিউ খালি — ধন্যবাদ!</h3>
            <p class="muted" data-bn="এই মুহূর্তে অপেক্ষমাণ কোনো নথি নেই।" data-en="No documents awaiting review right now.">এই মুহূর্তে অপেক্ষমাণ কোনো নথি নেই।</p>
        </div>
    }
    else
    {
        <div class="card">
            <table>
                <thead>
                    <tr>
                        <th>Doc</th>
                        <th>Category</th>
                        <th>District</th>
                        <th>Waiting (SLA)</th>
                        <th>Version</th>
                        <th>Status</th>
                        <th>Action</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var q in Model.Items)
                    {
                        <tr>
                            <td class="mono">DOC-@q.DocumentId<br /><small class="muted">@q.CaseTitle</small></td>
                            <td>@q.CategoryName</td>
                            <td>@q.DistrictName</td>
                            <td>
                                <span class="badge badge-@(q.WaitingHours >= 24 ? "rejected" : "neutral")">@q.WaitingHours h</span>
                            </td>
                            <td>v@(q.VersionNo) @(q.CitizenEdited ? "· edited" : "")</td>
                            <td>
                                @if (q.ClaimedBy != null)
                                {
                                    <span class="badge badge-review" title="Claimed">Claimed · @q.ClaimedBy</span>
                                }
                                else
                                {
                                    <span class="badge badge-draft">Unclaimed</span>
                                }
                            </td>
                            <td>
                                @if (q.CanOpen)
                                {
                                    <form asp-action="Claim" asp-route-documentId="@q.DocumentId" method="post" style="display:inline">
                                        @Html.AntiForgeryToken()
                                        <button class="btn btn-primary btn-sm" type="submit">
                                            <i data-lucide="eye"></i> Review
                                        </button>
                                    </form>
                                }
                                else
                                {
                                    <button class="btn btn-outline btn-sm" type="button" disabled
                                            title="Finish your current review first / অন্য আইনজীবীর নিয়ন্ত্রণে">
                                        Locked
                                    </button>
                                }
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
            <p class="muted tiny" style="margin: 12px 0 0" data-bn="নথি খুললেই সেটি আপনার নামে লক হয় (একসময়ে একটি পর্যালোচনা)।" data-en="Opening a document claims it for you (one active review at a time).">নথি খুললেই সেটি আপনার নামে লক হয় (একসময়ে একটি পর্যালোচনা)।</p>
        </div>
    }
</main>
```

- [ ] **Step 4: Create Views/Lawyer/Status.cshtml**

```html
@model LawyerStatusViewModel
@{
    ViewData["Title"] = "ভেরিফিকেশন স্ট্যাটাস — মুক্ত আইন";
    var badge = Model.Status == "Approved" ? "final" : Model.Status == "Rejected" ? "rejected" : "draft";
}

<main class="container" id="main" style="max-width: 620px">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <span data-bn="ভেরিফিকেশন" data-en="Verification">ভেরিফিকেশন</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="badge-check"></i> FR-15</span>
        <h1 class="page-title" data-bn="আইনজীবী যাচাইকরণ" data-en="Lawyer Verification">আইনজীবী যাচাইকরণ</h1>
    </div>

    <div class="card" style="text-align: center; padding: 32px">
        @if (Model.Status == "Rejected")
        {
            <div class="item-ico" style="margin: 0 auto 12px"><i data-lucide="x-circle"></i></div>
        }
        else
        {
            <div class="item-ico" style="margin: 0 auto 12px"><i data-lucide="hourglass"></i></div>
        }

        <h2>@(Model.Status == "Rejected" ? "আবেদন প্রত্যাখ্যাত / Rejected" : "যাচাইকরণ অপেক্ষমান / Verification Pending")</h2>
        <p>
            <span class="badge badge-@badge">@Model.Status</span>
        </p>
        <p class="muted">
            Bar #@Model.BarRegistrationNumber · @Model.Specialization
        </p>

        @if (Model.Status == "Pending")
        {
            <p class="muted tiny" data-bn="সাধারণত ২৪–৪৮ ঘণ্টার মধ্যে যাচাই সম্পন্ন হয়।" data-en="Verification typically takes 24–48 hours.">সাধারণত ২৪–৪৮ ঘণ্টার মধ্যে যাচাই সম্পন্ন হয়।</p>
            <div class="alert alert-info tiny" style="text-align: left">
                <b data-bn="অপেক্ষার সময় আপনি করতে পারবেন:" data-en="While you wait:">অপেক্ষার সময় আপনি করতে পারবেন:</b>
                <ul style="margin: 6px 0 0 18px; line-height: 1.9">
                    <li><a asp-controller="Search" asp-action="Index">আইন অনুসন্ধান / Search laws</a></li>
                    <li><a asp-controller="Category" asp-action="Index">বিষয়সমূহ দেখুন / Browse categories</a></li>
                    <li><a asp-controller="Account" asp-action="Profile">প্রোফাইল সম্পূর্ণ করুন / Complete profile</a></li>
                </ul>
            </div>
        }
        else if (Model.Status == "Rejected")
        {
            <div class="alert alert-danger tiny" style="text-align: left">
                <b>প্রত্যাখ্যানের কারণ / Reason:</b> অ্যাডমিন আপনার বার নম্বর যাচাই করতে পারেননি।
                নিচের ফর্মে সঠিক তথ্য দিয়ে পুনরায় আবেদন করুন।
            </div>

            <form asp-action="Resubmit" method="post" style="text-align: left; margin-top: 16px">
                @Html.AntiForgeryToken()
                <label class="form-label" data-bn="বার রেজিস্ট্রেশন নম্বর" data-en="Bar registration number">বার রেজিস্ট্রেশন নম্বর</label>
                <input class="input" asp-for="BarRegistrationNumber" placeholder="DHA-12345" />
                <label class="form-label" style="margin-top: 10px" data-bn="বিশেষজ্ঞ এলাকা" data-en="Specialization">বিশেষজ্ঞ এলাকা</label>
                <input class="input" asp-for="Specialization" placeholder="Labour / Criminal / Civil / Family / Consumer / Tax" />
                <button class="btn btn-primary btn-block" style="margin-top: 16px" type="submit">
                    <i data-lucide="send"></i> <span data-bn="পুনরায় আবেদন করুন" data-en="Resubmit application">পুনরায় আবেদন করুন</span>
                </button>
            </form>
        }
    </div>
</main>
```

- [ ] **Step 5: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Web/Controllers/LawyerController.cs src/MuktoAin.Web/Views/Lawyer/Queue.cshtml src/MuktoAin.Web/Views/Lawyer/Status.cshtml src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs
git commit -m "feat(web): lawyer queue (real data, SLA, claim) + verification status page"
```

---

### Task B3: Lawyer Review workspace (redline compare + reject modal)

**Files:**
- Modify (full replace): `src/MuktoAin.Web/Views/Lawyer/Review.cshtml`

**Interfaces:**
- Consumes: `LawyerReviewViewModel` (fields verified: `DocumentId, CaseId, CaseTitle, CategoryName, ContentDraft, EditedContent, Decision, Comments`); ViewData keys set by B2's Review action: `DistrictName`, `CitizenNarrative`, `Citations` (`IReadOnlyList<CitedSectionDto>`), `VersionNo`, `CitizenEdited`; POST target `Lawyer/SubmitReview` (B2).
- Produces: the FR-14 workspace — context accordion (PII note), original vs editable compare (tabs mobile / side-by-side desktop), mandatory comments, 3 decisions, reject modal.

- [ ] **Step 1: Full-replace Views/Lawyer/Review.cshtml**

```html
@model LawyerReviewViewModel
@{
    ViewData["Title"] = "পর্যালোচনা — DOC-" + Model.DocumentId + " — মুক্ত আইন";
    var citations = (IReadOnlyList<MuktoAin.Application.DTOs.CitedSectionDto>?)ViewData["Citations"]
        ?? new List<MuktoAin.Application.DTOs.CitedSectionDto>();
    var narrative = (string?)ViewData["CitizenNarrative"] ?? "";
    var district = (string?)ViewData["DistrictName"] ?? "";
    var versionNo = (int?)ViewData["VersionNo"] ?? 1;
    var citizenEdited = (bool?)ViewData["CitizenEdited"] ?? false;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index" data-bn="হোম" data-en="Home">হোম</a>
        <span class="sep">/</span>
        <a asp-controller="Lawyer" asp-action="Queue" data-bn="রিভিউ কিউ" data-en="Review Queue">রিভিউ কিউ</a>
        <span class="sep">/</span>
        <span>DOC-@Model.DocumentId</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="file-diff"></i> FR-14</span>
        <h1 class="page-title">@Model.CaseTitle</h1>
        <p class="page-sub">
            DOC-@Model.DocumentId · CASE-@Model.CaseId · @Model.CategoryName · @district · v@(versionNo)
            @if (citizenEdited)
            {
                <span class="badge badge-review" data-bn="নাগরিক সম্পাদিত" data-en="Citizen edited">নাগরিক সম্পাদিত</span>
            }
        </p>
    </div>

    <!-- Case context (PII session note) -->
    <details class="acc card" open>
        <summary><b data-bn="নাগরিকের বিবরণ (এই সেশনের জন্য ডিক্রিপ্টকৃত)" data-en="Citizen narrative (decrypted for this review session)">নাগরিকের বিবরণ (এই সেশনের জন্য ডিক্রিপ্টকৃত)</b></summary>
        <div class="alert alert-warn tiny">
            <i data-lucide="shield-alert"></i>
            <span data-bn="PII — শুধুমাত্র এই পর্যালোচনা সেশনের জন্য দৃশ্যমান; লগে সংরক্ষিত হয় না।" data-en="PII — visible for this review session only; never stored in logs.">PII — শুধুমাত্র এই পর্যালোচনা সেশনের জন্য দৃশ্যমান; লগে সংরক্ষিত হয় না।</span>
        </div>
        <p class="serif" style="white-space: pre-wrap; line-height: 2">@narrative</p>
        @if (citations.Any())
        {
            <div class="chip-row">
                @foreach (var c in citations)
                {
                    <span class="citation-chip">@c.ActTitle @(string.IsNullOrWhiteSpace(c.SectionNumber) ? "" : "· ধারা " + c.SectionNumber)</span>
                }
            </div>
        }
    </details>

    <form asp-action="SubmitReview" method="post">
        @Html.AntiForgeryToken()
        <input type="hidden" asp-for="DocumentId" />
        <input type="hidden" asp-for="CaseId" />
        <input type="hidden" asp-for="CaseTitle" />
        <input type="hidden" asp-for="CategoryName" />

        <!-- Compare: tabs on mobile (main.js data-tabs), side-by-side >=900px -->
        <div class="card" style="margin: 16px 0">
            <div data-tabs>
                <div class="tab-btns row" style="gap: 8px; margin-bottom: 12px">
                    <button class="chip active" type="button" data-tab="original"><i data-lucide="file-lock"></i> Original AI Draft (read-only)</button>
                    <button class="chip" type="button" data-tab="editable"><i data-lucide="file-pen"></i> Editable (final)</button>
                </div>

                <div class="tab-panel" data-tab-panel="original">
                    <div class="compare-pane compare-original">
                        <div class="paper-sheet paper-watermark" style="margin: 0">
                            <pre style="white-space: pre-wrap; font-family: var(--font-doc); font-size: 15px; line-height: 2; margin: 0">@Model.ContentDraft</pre>
                        </div>
                    </div>
                </div>

                <div class="tab-panel" data-tab-panel="editable" hidden>
                    <label class="form-label" data-bn="চূড়ান্ত সংস্করণ (সম্পাদনা করুন)" data-en="Final version (edit)">চূড়ান্ত সংস্করণ (সম্পাদনা করুন)</label>
                    <textarea class="input" asp-for="EditedContent" rows="16"
                              style="width: 100%; font-family: var(--font-doc)"
                              data-counter="#review-counter"></textarea>
                    <small class="muted tiny" id="review-counter"></small>
                </div>
            </div>

            <!-- Desktop side-by-side (hidden on mobile by main.css breakpoint) -->
            <div class="compare-grid-desktop" style="display: grid; grid-template-columns: 1fr 1fr; gap: 14px">
                <div class="compare-pane compare-original">
                    <h3 class="section-h"><i data-lucide="file-lock"></i> Original</h3>
                    <div class="paper-sheet paper-watermark" style="margin: 0">
                        <pre style="white-space: pre-wrap; font-family: var(--font-doc); font-size: 14px; line-height: 1.9; margin: 0">@Model.ContentDraft</pre>
                    </div>
                </div>
                <div class="compare-pane compare-edit">
                    <h3 class="section-h"><i data-lucide="file-pen"></i> Editable (final)</h3>
                    <textarea class="input" asp-for="EditedContent" rows="18"
                              style="width: 100%; font-family: var(--font-doc)"></textarea>
                </div>
            </div>
        </div>

        <!-- Mandatory comments -->
        <div class="card" style="margin: 16px 0">
            <label class="form-label" asp-for="Comments">
                <i data-lucide="message-square"></i>
                <span data-bn="পর্যালোচনা মন্তব্য (আবশ্যক — নাগরিক দেখবেন)" data-en="Review comments (required — visible to the citizen)">পর্যালোচনা মন্তব্য (আবশ্যক — নাগরিক দেখবেন)</span>
            </label>
            <textarea class="input" asp-for="Comments" rows="3" maxlength="1000"
                      placeholder="আপনার সিদ্ধান্তের কারণ ব্যাখ্যা করুন…"
                      data-counter="#comments-counter"></textarea>
            <small class="muted tiny" id="comments-counter"></small>
        </div>

        <!-- Decision bar -->
        <div class="sticky-action-bar row wrap" style="gap: 10px; padding: 14px 0">
            <label class="tiny muted" style="margin-right: 6px" data-bn="সিদ্ধান্ত:" data-en="Decision:">সিদ্ধান্ত:</label>

            <button class="btn btn-outline btn-sm" type="submit" name="Decision" value="Approved"
                    data-confirm="মূল খসড়া কোনো পরিবর্তন ছাড়া অনুমোদন করবেন? / Approve the original without changes?">
                <i data-lucide="check"></i> <span data-bn="অনুমোদন" data-en="Approve">অনুমোদন</span>
            </button>

            <button class="btn btn-primary btn-sm" type="submit" name="Decision" value="EditedApproved">
                <i data-lucide="check-check"></i> <span data-bn="সম্পাদনাসহ অনুমোদন" data-en="Approve with Edits">সম্পাদনাসহ অনুমোদন</span>
            </button>

            <button class="btn btn-danger-outline btn-sm" type="button" data-open-modal="#reject-modal">
                <i data-lucide="x"></i> <span data-bn="প্রত্যাখ্যান" data-en="Reject">প্রত্যাখ্যান</span>
            </button>
        </div>
    </form>
</main>

<!-- Reject modal: reason MANDATORY, shown to citizen, injected into their chat on return -->
<div class="modal-backdrop" id="reject-modal" role="dialog" aria-modal="true" aria-labelledby="reject-title">
    <div class="modal">
        <div class="modal-handle"></div>
        <div class="modal-head">
            <div>
                <span class="kicker" style="color: var(--danger)">FR-14</span>
                <h3 id="reject-title" data-bn="প্রত্যাখ্যানের কারণ (বাধ্যতামূলক)" data-en="Rejection reason (mandatory)">প্রত্যাখ্যানের কারণ (বাধ্যতামূলক)</h3>
            </div>
            <button class="icon-btn" type="button" data-close-modal aria-label="বন্ধ করুন"><i data-lucide="x"></i></button>
        </div>
        <form asp-action="SubmitReview" method="post">
            @Html.AntiForgeryToken()
            <input type="hidden" asp-for="DocumentId" />
            <input type="hidden" asp-for="CaseId" />
            <input type="hidden" asp-for="CaseTitle" />
            <input type="hidden" asp-for="CategoryName" />
            <input type="hidden" name="Decision" value="Rejected" />
            <input type="hidden" asp-for="EditedContent" />
            <p class="muted tiny" data-bn="কারণটি নাগরিকের কাছে দেখানো হবে এবং তাদের চ্যাটে যুক্ত হবে।" data-en="The reason is shown to the citizen and injected into their chat.">কারণটি নাগরিকের কাছে দেখানো হবে এবং তাদের চ্যাটে যুক্ত হবে।</p>
            <textarea class="input" asp-for="Comments" rows="4" maxlength="1000"
                      placeholder="যেমন: উদ্ধৃত ধারা প্রযোজ্য নয় — §123 যাচাই করুন…"></textarea>
            <div class="row" style="justify-content: flex-end; gap: 10px; margin-top: 14px">
                <button class="btn btn-outline btn-sm" type="button" data-close-modal data-bn="বাতিল" data-en="Cancel">বাতিল</button>
                <button class="btn btn-danger-outline btn-sm" type="submit" data-bn="নথি প্রত্যাখ্যান করুন" data-en="Reject document">নথি প্রত্যাখ্যান করুন</button>
            </div>
        </form>
    </div>
</main>
```
**HTML note:** the modal `</div></div>` closing tags must balance — final structure is `<div class="modal-backdrop">…<div class="modal">…</div></div>`. Count your divs before saving.

- [ ] **Step 2: Append CSS for compare panes**

Append to END of `src/MuktoAin.Web/wwwroot/assets/css/main.css`:
```css
/* ---------- Lawyer review compare (Part B) ---------- */
.compare-pane { border: 1px solid var(--border); border-radius: 14px; padding: 12px; min-width: 0; }
.compare-original { border-left: 3px solid var(--danger-soft); background: var(--surface-2); }
.compare-edit { border-left: 3px solid var(--success-soft); }
.compare-grid-desktop { display: none; }
@media (min-width: 900px) {
  .compare-grid-desktop { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; margin-top: 14px; }
  .compare-grid-desktop + * { margin-top: 0; }
  /* on desktop, the mobile tab copy of the editable textarea is redundant —
     both textareas bind asp-for="EditedContent"; only ONE may post.
     Hide the tab-panel version's textarea (values stay identical because
     JS syncs is unnecessary: user edits on desktop pane). */
  .tab-panel[data-tab-panel="editable"] textarea { display: none; }
}
@media (max-width: 899px) {
  .compare-grid-desktop { display: none !important; }
}
.btn-danger-outline {
  border: 1px solid var(--danger); color: var(--danger);
  background: transparent; border-radius: 8px;
  font-weight: 600; padding: 10px 18px; min-height: 40px;
  display: inline-flex; align-items: center; gap: 8px; cursor: pointer;
}
.btn-danger-outline:hover { background: var(--danger-soft); }
.sticky-action-bar { position: sticky; bottom: 12px; z-index: 20;
  background: var(--surface); border: 1px solid var(--border);
  border-radius: 14px; box-shadow: 0 4px 18px rgba(43,33,20,.08); }
```

- [ ] **Step 3: Build + commit**

Run: `dotnet build` — expected 0 errors (Razor compiles at build with RazorCompileOnBuild default in this project — if the project uses runtime compilation only, ALSO verify by `dotnet run` and GET /Lawyer/Review/1 as a verified lawyer).

```bash
git add src/MuktoAin.Web/Views/Lawyer/Review.cshtml src/MuktoAin.Web/wwwroot/assets/css/main.css
git commit -m "feat(web): lawyer review workspace — compare panes, mandatory comments, reject modal"
```

**Duplicate-textarea warning:** `asp-for="EditedContent"` renders two textareas with the SAME `name` — on POST both values arrive and the binder concatenates?? NO — model binding takes the LAST value. The desktop pane is the second one, so desktop edits win. On mobile, the desktop pane is display:none — a hidden textarea still posts its (stale) value and would override the tab pane edit. **Fix:** the CSS above hides the desktop TEXTAREA only on <900px via the media query — but hidden inputs still post. Instead, in the mobile tab panel only, keep the textarea; on desktop the tab panel is still visible… **RESOLUTION (do exactly this):** give the tab-panel textarea `id="EditedContentMobile"` and REMOVE the `asp-for` from the DESKTOP one, making desktop a plain `<textarea name="EditedContent">`. Then add this sync line to the page (before `</form>` of the main form):
```html
<script>
  // keep mobile tab and desktop pane in sync so whichever the lawyer edits
  // posts the correct value (single name="EditedContent" posts last-wins;
  // this sync makes both always identical).
  document.addEventListener("DOMContentLoaded", function () {
    var a = document.getElementById("EditedContentMobile");
    var b = document.getElementById("EditedContentDesktop");
    if (!a || !b) return;
    a.addEventListener("input", function () { b.value = a.value; });
    b.addEventListener("input", function () { a.value = b.value; });
  });
</script>
```
And set `id="EditedContentDesktop"` on the desktop textarea (keep its `name="EditedContent"`), and on the mobile textarea use `asp-for="EditedContent"` (which gives `id="EditedContent" name="EditedContent"`) — then change that id to `EditedContentMobile` via plain HTML: write the mobile textarea as `<textarea class="input" id="EditedContentMobile" name="EditedContent" rows="16" ...>` WITHOUT asp-for, and set its initial content to `@Model.EditedContent`. Both textareas post the same synced value; binder takes the last — identical either way.

- [ ] **Step 4: Commit the resolution**

```bash
git add src/MuktoAin.Web/Views/Lawyer/Review.cshtml
git commit -m "fix(web): dual-pane EditedContent sync (mobile tabs vs desktop side-by-side)"
```

---

### Task B4: Admin console part 1 — Users + Lawyers (verification) pages

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (add Users/Lawyers actions — additive, keep existing)
- Create: `src/MuktoAin.Web/Views/Admin/Users.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/Lawyers.cshtml`
- Create: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`

**Interfaces:**
- Consumes: `IUserManagementService.GetAllUsersAsync()` → `IEnumerable<UserListDto(int UserId, string FullName, string Email, string Role, string Status)>` (verified) and `SetAccountStatusAsync(int userId, AccountStatus status, int actingAdminId)` → bool; `LawyerVerificationService.GetPendingApplicationsAsync()` → `IEnumerable<LawyerProfile>` + `VerifyAsync(int lawyerProfileId, int adminUserId, bool approve)`; `IRepository<LawyerProfile>`; `UserManager<User>`.
- Produces: `GET /Admin/Users?role=`, `POST /Admin/Users/Suspend` (params `userId, suspend` bool), `GET /Admin/Lawyers`, `POST /Admin/Lawyers/Verify` (params `lawyerProfileId, approve, reason`). Used by B8's admin nav.

- [ ] **Step 1: Add the admin page ViewModels**

`src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs`:
```csharp
namespace MuktoAin.Web.ViewModels;

public class AdminUsersViewModel
{
    public List<AdminUserRowViewModel> Users { get; set; } = new();
    public string RoleFilter { get; set; } = "All";
}

public class AdminUserRowViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminLawyersViewModel
{
    public List<AdminLawyerRowViewModel> Pending { get; set; } = new();
    public List<AdminLawyerRowViewModel> Approved { get; set; } = new();
    public List<AdminLawyerRowViewModel> Rejected { get; set; } = new();
}

public class AdminLawyerRowViewModel
{
    public int LawyerProfileId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}
```

- [ ] **Step 2: Add Users + Lawyers actions to AdminController**

In `src/MuktoAin.Web/Controllers/AdminController.cs`, FIRST check its constructor — it currently injects what Dashboard/Analytics need. ADD the `Users` and `Suspend` actions below (plus constructor params `IUserManagementService userManagement, LawyerVerificationService lawyerVerification, IRepository<LawyerProfile> lawyerProfileRepo, UserManager<Domain.Entities.User> userManager` with private fields `_userManagement`, `_lawyerVerification`, `_lawyerProfileRepo`, `_userManager`, following the file's existing field/ctor style — preserve everything already there). **Do NOT add a `VerifyLawyer` action in this step** — its final version is given in Step 3e:

```csharp
    [HttpGet]
    public async Task<IActionResult> Users(string? role)
    {
        var all = await _userManagement.GetAllUsersAsync();
        var filtered = string.IsNullOrWhiteSpace(role) || role == "All"
            ? all
            : all.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
        var vm = new AdminUsersViewModel
        {
            RoleFilter = role ?? "All",
            Users = filtered.Select(u => new AdminUserRowViewModel
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                Status = u.Status
            }).ToList()
        };
        return View(vm);
    }

    // Suspend/Activate. Admin rows are protected inside the service
    // (UserManagementService guards admins + self-suspend).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(int userId, bool suspend)
    {
        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        var ok = await _userManagement.SetAccountStatusAsync(
            userId, suspend ? Domain.Enums.AccountStatus.Suspended : Domain.Enums.AccountStatus.Active, adminId);
        if (!ok)
        {
            TempData["Error"] = "এই অ্যাকাউন্টের অবস্থা পরিবর্তন করা যাবে না (অ্যাডমিন সুরক্ষিত)।";
            TempData["ErrorEn"] = "This account's status cannot be changed (admin protected).";
        }
        else
        {
            TempData["Success"] = suspend ? "অ্যাকাউন্ট স্থগিত হয়েছে।" : "অ্যাকাউন্ট পুনরায় চালু হয়েছে।";
            TempData["SuccessEn"] = suspend ? "Account suspended." : "Account reactivated.";
        }
         return RedirectToAction(nameof(Users));
     }

    [HttpGet]
    public async Task<IActionResult> Lawyers()
    {
        var all = await _lawyerProfileRepo.GetAllAsync();
        var rows = new List<AdminLawyerRowViewModel>();
        foreach (var p in all)
        {
            var u = await _userManager.FindByIdAsync(p.UserId.ToString());
            rows.Add(new AdminLawyerRowViewModel
            {
                LawyerProfileId = p.LawyerProfileId,
                ApplicantName = u?.FullName ?? "(unknown)",
                Email = u?.Email ?? "",
                BarRegistrationNumber = p.BarRegistrationNumber,
                Specialization = p.Specialization ?? "",
                Status = p.VerificationStatus.ToString()
            });
        }
        var vm = new AdminLawyersViewModel
        {
            Pending = rows.Where(r => r.Status == "Pending").ToList(),
            Approved = rows.Where(r => r.Status == "Approved").ToList(),
            Rejected = rows.Where(r => r.Status == "Rejected").ToList()
        };
        return View(vm);
    }
```
(The `VerifyLawyer` POST action belongs to Step 3e — add it there, not here.)

- [ ] **Step 3: Extend LawyerVerificationService with reason storage (additive)**

In `src/MuktoAin.Application/Services/LawyerVerificationService.cs`, ADD one method to the class (keep all existing methods; the `Specialization` field is NOT used for reason — we add a dedicated nullable column via the Domain entity):

**3a.** In `src/MuktoAin.Domain/Entities/LawyerProfile.cs`, add after `VerifiedAt`:
```csharp
    // Redesign 2026-09 (FR-15): rejection reason shown to the lawyer on resubmit
    public string? RejectionReason { get; set; }
```

**3b.** In `scripts/09_part_b_tables.sql`, REPLACE the no-op body with:
```sql
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[LAWYER_PROFILE]') AND name = N'RejectionReason')
    ALTER TABLE [dbo].[LAWYER_PROFILE] ADD RejectionReason NVARCHAR(500) NULL;
GO
```

**3c.** In `LawyerVerificationService`, REPLACE the existing `VerifyAsync` method with:
```csharp
    public async Task VerifyAsync(int lawyerProfileId, int adminUserId, bool approve, string? reason = null)
    {
        var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
        if (profile == null) throw new ArgumentException("Profile not found");

        profile.VerificationStatus = approve
            ? VerificationStatus.Approved
            : VerificationStatus.Rejected;
        profile.VerifiedByAdminId = adminUserId;
        profile.VerifiedAt = DateTime.UtcNow;
        profile.RejectionReason = approve ? null : (reason ?? string.Empty);

        await _profileRepo.SaveChangesAsync();
    }
```

**3d.** In `src/MuktoAin.Infrastructure/Data/Configurations/LawyerProfileConfiguration.cs`, inside the existing `Configure` method body (do not remove anything), add:
```csharp
        // Redesign 2026-09 (scripts/09_part_b_tables.sql)
        builder.Property(p => p.RejectionReason).HasMaxLength(500);
```

**3e.** Final `VerifyLawyer` action — add to `AdminController` (this is the ONLY version; there is no earlier draft):
```csharp
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyLawyer(int lawyerProfileId, bool approve, string? reason)
    {
        if (!approve && string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "প্রত্যাখ্যানের কারণ আবশ্যক।";
            TempData["ErrorEn"] = "Rejection reason is required.";
            return RedirectToAction(nameof(Lawyers));
        }

        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        await _lawyerVerification.VerifyAsync(lawyerProfileId, adminId, approve, reason);

        TempData["Success"] = approve
            ? "আইনজীবী যাচাই অনুমোদিত হয়েছে।"
            : "আবেদন প্রত্যাখ্যাত হয়েছে (কারণসহ)।";
        TempData["SuccessEn"] = approve ? "Lawyer verified." : "Application rejected (with reason).";
        return RedirectToAction(nameof(Lawyers));
    }
```

**3f.** Surface the reason on the lawyer Status page — in `Views/Lawyer/Status.cshtml` (B2), inside the Rejected branch, change the alert to show the stored reason. Add to `LawyerStatusViewModel`:
```csharp
    public string? RejectionReason { get; set; }
```
In B2's `Status` action, set `RejectionReason = profile.RejectionReason` in the VM, and in the view replace the hardcoded reason text with `@(Model.RejectionReason ?? "অ্যাডমিন কোনো কারণ উল্লেখ করেননি।")`.

- [ ] **Step 4: Create Views/Admin/Users.cshtml**

```html
@model AdminUsersViewModel
@{
    ViewData["Title"] = "Users — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Users</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="users"></i> FR-18</span>
        <h1 class="page-title">User Management</h1>
        <p class="page-sub">Suspend or restore accounts. Admin accounts are protected from modification.</p>
    </div>

    <div class="chip-row" style="margin-bottom: 16px">
        @foreach (var f in new[] { "All", "Citizen", "Lawyer", "Admin" })
        {
            <a class="chip chip-sm @(Model.RoleFilter == f ? "active" : "")"
               asp-action="Users" asp-route-role="@f">@f</a>
        }
    </div>

    <div class="card">
        <table>
            <thead>
                <tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Actions</th></tr>
            </thead>
            <tbody>
                @foreach (var u in Model.Users)
                {
                    <tr>
                        <td>@u.FullName</td>
                        <td class="mono">@u.Email</td>
                        <td><span class="badge badge-neutral">@u.Role</span></td>
                        <td>
                            <span class="badge badge-@(u.Status == "Suspended" ? "rejected" : "final")">@u.Status</span>
                        </td>
                        <td>
                            @if (u.Role == "Admin")
                            {
                                <span class="badge badge-neutral" title="Protected">Protected</span>
                            }
                            else if (u.Status == "Suspended")
                            {
                                <form asp-action="Suspend" method="post" style="display:inline">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="userId" value="@u.UserId" />
                                    <input type="hidden" name="suspend" value="false" />
                                    <button class="btn btn-outline btn-sm" type="submit">Activate</button>
                                </form>
                            }
                            else
                            {
                                <form asp-action="Suspend" method="post" style="display:inline"
                                      data-confirm="Suspend this account? Login will be blocked.">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="userId" value="@u.UserId" />
                                    <input type="hidden" name="suspend" value="true" />
                                    <button class="btn btn-danger-outline btn-sm" type="submit">Suspend</button>
                                </form>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
</main>
```

- [ ] **Step 5: Create Views/Admin/Lawyers.cshtml**

```html
@model AdminLawyersViewModel
@{
    ViewData["Title"] = "Lawyer Verification — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Lawyers</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="badge-check"></i> FR-15 · FR-18</span>
        <h1 class="page-title">Lawyer Verification</h1>
        <p class="page-sub">Approve or reject bar-registration applications. Rejection requires a reason.</p>
    </div>

    <div class="chip-row" style="margin-bottom: 16px">
        <span class="chip chip-sm">Pending · @Model.Pending.Count</span>
        <span class="chip chip-sm">Approved · @Model.Approved.Count</span>
        <span class="chip chip-sm">Rejected · @Model.Rejected.Count</span>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr><th>Applicant</th><th>Bar Reg No</th><th>Specialization</th><th>Status</th><th>Actions</th></tr>
            </thead>
            <tbody>
                @foreach (var l in Model.Pending)
                {
                    <tr>
                        <td>@l.ApplicantName<br /><small class="muted mono">@l.Email</small></td>
                        <td class="mono">@l.BarRegistrationNumber</td>
                        <td>@l.Specialization</td>
                        <td><span class="badge badge-draft">Pending</span></td>
                        <td>
                            <form asp-action="VerifyLawyer" method="post" style="display:inline">
                                @Html.AntiForgeryToken()
                                <input type="hidden" name="lawyerProfileId" value="@l.LawyerProfileId" />
                                <input type="hidden" name="approve" value="true" />
                                <button class="btn btn-outline btn-sm" type="submit"
                                        data-confirm="Approve this lawyer? Queue access unlocks.">Approve</button>
                            </form>
                            <button class="btn btn-danger-outline btn-sm" type="button"
                                    data-open-modal="#reject-@l.LawyerProfileId">Reject</button>
                        </td>
                    </tr>
                }
                @foreach (var l in Model.Approved)
                {
                    <tr>
                        <td>@l.ApplicantName<br /><small class="muted mono">@l.Email</small></td>
                        <td class="mono">@l.BarRegistrationNumber</td>
                        <td>@l.Specialization</td>
                        <td><span class="badge badge-final">Approved</span></td>
                        <td><span class="muted tiny">—</span></td>
                    </tr>
                }
                @foreach (var l in Model.Rejected)
                {
                    <tr>
                        <td>@l.ApplicantName<br /><small class="muted mono">@l.Email</small></td>
                        <td class="mono">@l.BarRegistrationNumber</td>
                        <td>@l.Specialization</td>
                        <td><span class="badge badge-rejected">Rejected</span></td>
                        <td><span class="muted tiny">—</span></td>
                    </tr>
                }
                @if (!Model.Pending.Any() && !Model.Approved.Any() && !Model.Rejected.Any())
                {
                    <tr><td colspan="5" class="muted" style="text-align:center">No applications.</td></tr>
                }
            </tbody>
        </table>
    </div>
</main>

<!-- Rejection reason modals (one per pending row) -->
@foreach (var l in Model.Pending)
{
    <div class="modal-backdrop" id="reject-@l.LawyerProfileId" role="dialog" aria-modal="true">
        <div class="modal">
            <div class="modal-handle"></div>
            <div class="modal-head">
                <div>
                    <span class="kicker" style="color: var(--danger)">Reject application</span>
                    <h3>@l.ApplicantName · @l.BarRegistrationNumber</h3>
                </div>
                <button class="icon-btn" type="button" data-close-modal aria-label="Close"><i data-lucide="x"></i></button>
            </div>
            <form asp-action="VerifyLawyer" method="post">
                @Html.AntiForgeryToken()
                <input type="hidden" name="lawyerProfileId" value="@l.LawyerProfileId" />
                <input type="hidden" name="approve" value="false" />
                <label class="form-label">Reason (mandatory — shown to the lawyer)</label>
                <textarea class="input" name="reason" rows="3" maxlength="500"
                          placeholder="e.g. Bar number not found in the Bar Council registry."></textarea>
                <div class="row" style="justify-content: flex-end; gap: 10px; margin-top: 14px">
                    <button class="btn btn-outline btn-sm" type="button" data-close-modal>Cancel</button>
                    <button class="btn btn-danger-outline btn-sm" type="submit">Reject application</button>
                </div>
            </form>
        </div>
    </div>
}
```

- [ ] **Step 6: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Web/Controllers/AdminController.cs src/MuktoAin.Web/Views/Admin/Users.cshtml src/MuktoAin.Web/Views/Admin/Lawyers.cshtml src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs src/MuktoAin.Domain/Entities/LawyerProfile.cs src/MuktoAin.Application/Services/LawyerVerificationService.cs src/MuktoAin.Infrastructure/Data/Configurations/LawyerProfileConfiguration.cs scripts/09_part_b_tables.sql
git commit -m "feat(admin): Users + Lawyers verification pages (real data, reason-gated reject)"
```

---

### Task B5: Admin console part 2 — Corpus, Scenarios, Categories, AI Logs

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (additive actions)
- Create: `src/MuktoAin.Web/Views/Admin/Corpus.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/Scenarios.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/Categories.cshtml`
- Create: `src/MuktoAin.Web/Views/Admin/AiLogs.cshtml`
- Create: `src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs` additions (append to B4's file)

**Interfaces:**
- Consumes: `IActRepository.GetAllAsync()` (Act fields verified: `ActId, Title, ActNumber, Year, Language, IsRepealed, ImportedAt`); `IActSectionRepository.GetAllAsync()` (ActSection: `SectionId, ActId, OrdinalPosition, SectionNumber, SectionTitle, SectionText`); `IActSectionChunkRepository.GetAllAsync()` (ActSectionChunk: `ChunkId, SectionId, ChunkOrder, ChunkText, TokenCount, VectorId, ContentHash, LastEmbeddedAt`); `IScenarioMappingRepository` (+ base `AddAsync/DeleteAsync/SaveChangesAsync`); `IRepository<CaseCategory>`; `IRepository<AiLog>` (fields: `LogId, CaseId, RequestType, PromptText, ResponseText, ModelUsed, TokensUsed, LatencyMs, CreatedAt`); `IRepository<District>`; existing `SeedActsFromJson` is NOT used — Import tab is read-only status for now (documented below).
- Produces: `GET /Admin/Corpus`, `GET /Admin/Scenarios`, `POST /Admin/Scenarios/Add`, `POST /Admin/Scenarios/Delete`, `GET /Admin/Categories`, `POST /Admin/Categories/Save`, `GET /Admin/AiLogs?type=&minLatency=`.

- [ ] **Step 1: Append admin VMs (to AdminPageViewModels.cs)**

```csharp
public class AdminCorpusViewModel
{
    public List<AdminActRowViewModel> Acts { get; set; } = new();
    public int TotalSections { get; set; }
    public int TotalChunks { get; set; }
    public int EmbeddedChunks { get; set; }
}

public class AdminActRowViewModel
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ActNumber { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Language { get; set; } = string.Empty;
    public bool IsRepealed { get; set; }
    public int SectionCount { get; set; }
    public int ChunkCount { get; set; }
    public int EmbeddedCount { get; set; }
    public DateTime ImportedAt { get; set; }
}

public class AdminScenariosViewModel
{
    public List<AdminScenarioRowViewModel> Mappings { get; set; } = new();
}

public class AdminScenarioRowViewModel
{
    public int MappingId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public string ActTitle { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class AdminCategoriesViewModel
{
    public List<AdminCategoryRowViewModel> Categories { get; set; } = new();
}

public class AdminCategoryRowViewModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameBn { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplateBadge { get; set; } = string.Empty;
}

public class AdminAiLogsViewModel
{
    public List<AdminAiLogRowViewModel> Logs { get; set; } = new();
    public int CallsToday { get; set; }
    public double FailureRateToday { get; set; }
}

public class AdminAiLogRowViewModel
{
    public long LogId { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Tokens { get; set; }
    public int LatencyMs { get; set; }
    public int? CaseId { get; set; }
    public string PromptPreview { get; set; } = string.Empty;
    public string ResponsePreview { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add controller actions (append to AdminController; extend ctor with `IActRepository actRepo, IActSectionRepository sectionRepo, IActSectionChunkRepository chunkRepo, IScenarioMappingRepository scenarioRepo, IRepository<CaseCategory> categoryRepo, IRepository<AiLog> aiLogRepo`)**

```csharp
    // ---------- FR-17: Corpus ----------

    [HttpGet]
    public async Task<IActionResult> Corpus()
    {
        var acts = await _actRepo.GetAllAsync();
        var sections = await _sectionRepo.GetAllAsync();
        var chunks = await _chunkRepo.GetAllAsync();

        var vm = new AdminCorpusViewModel
        {
            TotalSections = sections.Count(),
            TotalChunks = chunks.Count(),
            EmbeddedChunks = chunks.Count(c => c.VectorId != null),
            Acts = acts.OrderBy(a => a.Title).Take(100).Select(a => new AdminActRowViewModel
            {
                ActId = a.ActId,
                Title = a.Title,
                ActNumber = a.ActNumber ?? "",
                Year = a.Year,
                Language = a.Language,
                IsRepealed = a.IsRepealed,
                SectionCount = sections.Count(s => s.ActId == a.ActId),
                ChunkCount = chunks.Count(c => sections.Any(s => s.ActId == a.ActId && s.SectionId == c.SectionId)),
                EmbeddedCount = chunks.Count(c => c.VectorId != null
                    && sections.Any(s => s.ActId == a.ActId && s.SectionId == c.SectionId)),
                ImportedAt = a.ImportedAt
            }).ToList()
        };
        return View(vm);
    }

    // ---------- FR-18: Scenario mappings ----------

    [HttpGet]
    public async Task<IActionResult> Scenarios()
    {
        var mappings = await _scenarioRepo.GetAllAsync();
        var sections = await _sectionRepo.GetAllAsync();
        var acts = await _actRepo.GetAllAsync();

        var vm = new AdminScenariosViewModel
        {
            Mappings = mappings.OrderBy(m => m.MappingId).Select(m =>
            {
                var s = sections.FirstOrDefault(x => x.SectionId == m.SectionId);
                var a = s != null ? acts.FirstOrDefault(x => x.ActId == s.ActId) : null;
                return new AdminScenarioRowViewModel
                {
                    MappingId = m.MappingId,
                    Keyword = m.ScenarioKeyword,
                    ActTitle = a?.Title ?? "",
                    SectionNumber = s?.SectionNumber ?? "",
                    Notes = m.Notes
                };
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScenario(int sectionId, string keyword, string? notes)
    {
        if (string.IsNullOrWhiteSpace(keyword) || sectionId <= 0)
        {
            TempData["Error"] = "Keyword and section are required.";
            return RedirectToAction(nameof(Scenarios));
        }
        await _scenarioRepo.AddAsync(new Domain.Entities.ScenarioMapping
        {
            SectionId = sectionId,
            ScenarioKeyword = keyword.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        });
        await _scenarioRepo.SaveChangesAsync();
        TempData["Success"] = "Mapping added.";
        return RedirectToAction(nameof(Scenarios));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScenario(int mappingId)
    {
        var all = await _scenarioRepo.GetAllAsync();
        var m = all.FirstOrDefault(x => x.MappingId == mappingId);
        if (m != null)
        {
            await _scenarioRepo.DeleteAsync(m);
            await _scenarioRepo.SaveChangesAsync();
        }
        TempData["Success"] = "Mapping deleted.";
        return RedirectToAction(nameof(Scenarios));
    }

    // ---------- FR-18: Categories ----------

    [HttpGet]
    public async Task<IActionResult> Categories()
    {
        var cats = await _categoryRepo.GetAllAsync();
        var vm = new AdminCategoriesViewModel
        {
            Categories = cats.OrderBy(c => c.CategoryId).Select(c => new AdminCategoryRowViewModel
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                NameBn = c.NameBn,
                Description = c.Description,
                TemplateBadge = c.CategoryId switch
                {
                    1 => "labour_complaint.v1",
                    2 => "gd_application.v1",
                    3 => "rti_request.v1",
                    4 => "consumer_complaint.v1",
                    _ => "custom.v1"
                }
            }).ToList()
        };
        return View(vm);
    }

    // ---------- FR-12: AI Logs ----------

    [HttpGet]
    public async Task<IActionResult> AiLogs(string? type, int minLatency = 0)
    {
        var logs = (await _aiLogRepo.GetAllAsync())
            .OrderByDescending(l => l.CreatedAt)
            .Take(200);

        if (!string.IsNullOrWhiteSpace(type) && type != "All"
            && Enum.TryParse<Domain.Enums.AiRequestType>(type, out var t))
        {
            logs = logs.Where(l => l.RequestType == t);
        }
        if (minLatency > 0)
        {
            logs = logs.Where(l => l.LatencyMs >= minLatency);
        }

        var today = DateTime.UtcNow.Date;
        var allToday = (await _aiLogRepo.GetAllAsync()).Where(l => l.CreatedAt >= today).ToList();

        var vm = new AdminAiLogsViewModel
        {
            CallsToday = allToday.Count,
            FailureRateToday = allToday.Count == 0 ? 0 : 0, // failure detection = latency outliers; see view
            Logs = logs.Select(l => new AdminAiLogRowViewModel
            {
                LogId = l.LogId,
                Time = l.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Type = l.RequestType.ToString(),
                Model = l.ModelUsed,
                Tokens = l.TokensUsed,
                LatencyMs = l.LatencyMs,
                CaseId = l.CaseId,
                PromptPreview = l.PromptText.Length > 200 ? l.PromptText[..200] + "…" : l.PromptText,
                ResponsePreview = l.ResponseText.Length > 200 ? l.ResponseText[..200] + "…" : l.ResponseText
            }).ToList()
        };
        return View(vm);
    }
```
Add the needed `using` statements at the top of AdminController if missing: `MuktoAin.Domain.Interfaces.Repositories`, `MuktoAin.Web.ViewModels` (likely present).

- [ ] **Step 3: Create the four views**

**Views/Admin/Corpus.cshtml:**
```html
@model AdminCorpusViewModel
@{
    ViewData["Title"] = "Corpus — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Corpus</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="library"></i> FR-17</span>
        <h1 class="page-title">Legislative Corpus</h1>
        <p class="page-sub">Acts, sections, and vector-index status. Re-embedding runs via the Embedding Batch Job on the Dashboard.</p>
    </div>

    <div class="grid grid-3" style="margin-bottom: 20px">
        <div class="card"><span class="kicker">Sections</span><div style="font-size:32px;font-weight:700">@Model.TotalSections</div></div>
        <div class="card"><span class="kicker">Chunks</span><div style="font-size:32px;font-weight:700">@Model.TotalChunks</div></div>
        <div class="card"><span class="kicker">Embedded</span><div style="font-size:32px;font-weight:700">@Model.EmbeddedChunks / @Model.TotalChunks</div></div>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr><th>Act</th><th>Year</th><th>Lang</th><th>Sections</th><th>Chunks</th><th>Embedded</th><th>Status</th></tr>
            </thead>
            <tbody>
                @foreach (var a in Model.Acts)
                {
                    <tr>
                        <td>@a.Title<br /><small class="muted mono">@(string.IsNullOrEmpty(a.ActNumber) ? "" : a.ActNumber + " · ")@a.ActId</small></td>
                        <td>@a.Year</td>
                        <td>@a.Language</td>
                        <td>@a.SectionCount</td>
                        <td>@a.ChunkCount</td>
                        <td>@a.EmbeddedCount</td>
                        <td>
                            @if (a.IsRepealed)
                            {
                                <span class="badge badge-rejected">Repealed</span>
                            }
                            else if (a.EmbeddedCount == 0 && a.ChunkCount > 0)
                            {
                                <span class="badge badge-review">Awaiting embed</span>
                            }
                            else if (a.EmbeddedCount < a.ChunkCount)
                            {
                                <span class="badge badge-draft">Partial</span>
                            }
                            else
                            {
                                <span class="badge badge-final">Synced</span>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>

    <div class="alert alert-warn tiny" style="margin-top: 12px">
        <i data-lucide="info"></i>
        <span>Import pipeline runs via the seeded ingestion job (CP1 ActImportService). This page reflects current corpus state — hash-deduplicated incremental sync lands with T-3.1.</span>
    </div>
</main>
```

**Views/Admin/Scenarios.cshtml:**
```html
@model AdminScenariosViewModel
@{
    ViewData["Title"] = "Scenario Mappings — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Scenario Mappings</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="git-merge"></i> FR-18</span>
        <h1 class="page-title">Scenario Mappings</h1>
        <p class="page-sub">Citizen keywords → statute sections the retrieval pipeline prioritizes. @Model.Mappings.Count mappings.</p>
    </div>

    <div class="card" style="margin-bottom: 16px">
        <form asp-action="AddScenario" method="post" class="row wrap" style="gap: 10px; align-items: flex-end">
            @Html.AntiForgeryToken()
            <div>
                <label class="form-label">Keyword</label>
                <input class="input" name="keyword" placeholder="বেতন বাকি" required />
            </div>
            <div>
                <label class="form-label">Section ID</label>
                <input class="input" name="sectionId" type="number" min="1" placeholder="12345" required />
            </div>
            <div>
                <label class="form-label">Notes (optional)</label>
                <input class="input" name="notes" placeholder="unpaid wages" />
            </div>
            <button class="btn btn-primary btn-sm" type="submit"><i data-lucide="plus"></i> Add mapping</button>
        </form>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr><th>Keyword</th><th>Act</th><th>Section</th><th>Notes</th><th></th></tr>
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
            </tbody>
        </table>
    </div>
</main>
```

**Views/Admin/Categories.cshtml:**
```html
@model AdminCategoriesViewModel
@{
    ViewData["Title"] = "Categories — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Categories</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="layout-grid"></i> FR-18</span>
        <h1 class="page-title">Case Categories</h1>
        <p class="page-sub">Each category drives intake chips, retrieval priors, and the document template.</p>
    </div>

    <div class="grid grid-2">
        @foreach (var c in Model.Categories)
        {
            <div class="card">
                <div class="row spread" style="align-items: flex-start">
                    <div>
                        <b>@c.NameBn</b> <small class="muted">/ @c.Name</small><br />
                        <span class="badge badge-neutral mono">@c.TemplateBadge</span>
                    </div>
                </div>
                <p class="muted tiny" style="margin: 10px 0 0">@c.Description</p>
            </div>
        }
    </div>

    <div class="alert alert-warn tiny" style="margin-top: 12px">
        <i data-lucide="info"></i>
        <span>Category names/descriptions are seeded via SSMS (04–06 scripts) — full CRUD lands with CP3 admin tooling. Template badges are convention-mapped from CategoryId.</span>
    </div>
</main>
```
(Honest scope note shown ON the page — no fake edit buttons. This is deliberate: `CaseCategory` CRUD needs seed-script coordination with Tultul's CP3 tasks; the badge display fulfills the redesign's visible requirement.)

**Views/Admin/AiLogs.cshtml:**
```html
@model AdminAiLogsViewModel
@{
    ViewData["Title"] = "AI Logs — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>AI Logs</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="activity"></i> FR-12</span>
        <h1 class="page-title">AI Audit Log</h1>
        <p class="page-sub">Every Gemini call — tokens, latency, outcome. Prompts are PII-scrubbed before storage (S-2.7).</p>
    </div>

    <div class="grid grid-3" style="margin-bottom: 16px">
        <div class="card"><span class="kicker">Calls today</span><div style="font-size:32px;font-weight:700">@Model.CallsToday</div></div>
        <div class="card"><span class="kicker">Rows shown</span><div style="font-size:32px;font-weight:700">@Model.Logs.Count</div></div>
        <div class="card"><span class="kicker">Retention</span><div style="font-size:16px;font-weight:700;margin-top:10px">180 days (design)</div></div>
    </div>

    <div class="card" style="margin-bottom: 16px">
        <form asp-action="AiLogs" method="get" class="row wrap" style="gap: 10px; align-items: flex-end">
            <div>
                <label class="form-label">Type</label>
                <select class="input" name="type">
                    @foreach (var t in new[] { "All", "LawIdentification", "RightsExplanation", "Drafting" })
                    {
                        <option value="@t">@t</option>
                    }
                </select>
            </div>
            <div>
                <label class="form-label">Min latency (ms)</label>
                <select class="input" name="minLatency">
                    <option value="0">Any</option>
                    <option value="1000">&gt; 1s</option>
                    <option value="2000">&gt; 2s</option>
                    <option value="5000">&gt; 5s</option>
                </select>
            </div>
            <button class="btn btn-outline btn-sm" type="submit">Apply</button>
        </form>
    </div>

    <div class="card">
        <table>
            <thead>
                <tr><th>Time</th><th>Type</th><th>Model</th><th>Tokens</th><th>Latency</th><th>Case</th></tr>
            </thead>
            <tbody>
                @foreach (var l in Model.Logs)
                {
                    <tr>
                        <td class="mono tiny">@l.Time</td>
                        <td>@l.Type</td>
                        <td class="mono tiny">@l.Model</td>
                        <td>@l.Tokens</td>
                        <td>
                            <span class="badge badge-@(l.LatencyMs > 5000 ? "rejected" : l.LatencyMs > 2000 ? "review" : "final")">@(l.LatencyMs)ms</span>
                        </td>
                        <td>
                            @if (l.CaseId.HasValue)
                            {
                                <a class="tiny mono" asp-controller="Case" asp-action="Result" asp-route-id="@l.CaseId.Value">CASE-@l.CaseId</a>
                            }
                            else
                            {
                                <span class="muted tiny">chat</span>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>

    <details class="acc card" style="margin-top: 14px">
        <summary><b>Inspect prompts/responses (first 200 rows, preview only)</b></summary>
        @foreach (var l in Model.Logs.Take(20))
        {
            <details class="acc">
                <summary class="mono tiny">LOG-@l.LogId · @l.Type · @l.Time</summary>
                <p class="mono tiny" style="white-space: pre-wrap; background: var(--surface-2); padding: 10px; border-radius: 8px">PROMPT: @l.PromptPreview</p>
                <p class="mono tiny" style="white-space: pre-wrap; background: var(--surface-2); padding: 10px; border-radius: 8px">RESPONSE: @l.ResponsePreview</p>
            </details>
        }
    </details>
</main>
```

- [ ] **Step 4: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Web/Controllers/AdminController.cs src/MuktoAin.Web/Views/Admin/Corpus.cshtml src/MuktoAin.Web/Views/Admin/Scenarios.cshtml src/MuktoAin.Web/Views/Admin/Categories.cshtml src/MuktoAin.Web/Views/Admin/AiLogs.cshtml src/MuktoAin.Web/ViewModels/AdminPageViewModels.cs
git commit -m "feat(admin): Corpus/Scenarios/Categories/AiLogs pages — real repo data, no mocks"
```

---

### Task B6: PaymentService + Admin Transactions + Lawyer Earnings

**Files:**
- Create: `src/MuktoAin.Application/Services/PaymentService.cs`
- Create: `src/MuktoAin.Application/DTOs/PaymentDto.cs`
- Modify: `src/MuktoAin.Web/Controllers/AdminController.cs` (Transactions actions)
- Create: `src/MuktoAin.Web/Views/Admin/Transactions.cshtml`
- Modify: `src/MuktoAin.Web/Controllers/AccountController.cs` (Earnings data on Profile for lawyers)
- Modify: `src/MuktoAin.Web/Views/Account/Profile.cshtml` (Earnings card — additive block)
- Modify: `src/MuktoAin.Web/ViewModels/ProfileViewModels.cs` (Earnings fields)

**Interfaces:**
- Consumes: `IRepository<PaymentOrder>` / `IRepository<PayoutRequest>` (Part A entities — **if Part A has NOT run yet, this task blocks on A1/A2; in that case execute B6 LAST within Part B**); `IRepository<LawyerProfile>`; `IRepository<Case>`.
- Produces: `PaymentService.CreateOrderAsync(...)`, `MarkPaidAsync(int orderId, string gatewayRef)`, `RefundAsync(int orderId)`, `GetOrdersAsync()`, `GetLawyerEarningsAsync(int lawyerProfileId)`, `RequestPayoutAsync(int lawyerProfileId, decimal amount)`, `ApprovePayoutAsync(int payoutRequestId)`; `GET /Admin/Transactions`, `POST /Admin/Transactions/Refund`, `POST /Admin/Transactions/ApprovePayout`; Profile Earnings card. Sandbox-gateway wiring (SSLCommerz init/IPN) is NOT built here — orders are created Paid-by-sandbox-action in a follow-up; this task ships the ledger + admin surfaces (per spec, the citizen-facing payment modals ship in Part A's quota-wall flow — see spec §4; the modal UI is intentionally minimal here: admin + lawyer surfaces first, citizen honorarium action below).

- [ ] **Step 1: Create PaymentDto.cs**

```csharp
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record PaymentOrderDto(
    int PaymentOrderId,
    int? CaseId,
    string Purpose,       // TopUp | Honorarium
    string Status,        // Pending | Paid | Failed | Refunded
    decimal Amount,
    decimal Commission,
    decimal NetToLawyer,
    string? GatewayRef,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? RefundedAt,
    string? UserEmail,    // anonymized citizen (email domain only where needed; admin sees full)
    string? LawyerName
);

public record LawyerEarningsDto(
    decimal Balance,
    List<EarningRowDto> History
);

public record EarningRowDto(
    int PaymentOrderId,
    int CaseId,
    decimal Gross,
    decimal Commission,
    decimal Net,
    DateTime PaidAt
);
```

- [ ] **Step 2: Create PaymentService**

```csharp
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Identity;

namespace MuktoAin.Application.Services;

// FR-24: sandbox payments + commission ledger + lawyer payouts.
// Commission rate is a configurable constant (spec: 10%, appsettings override
// later). Nothing here talks to a real gateway — sandbox mode means orders are
// marked Paid by an explicit sandbox action (admin verify / citizen confirm
// stub), Failed on cancel, Refunded by admin with ledger reversal.
public class PaymentService
{
    public const decimal DefaultCommissionRate = 0.10m; // 10%

    private readonly IRepository<PaymentOrder> _orderRepo;
    private readonly IRepository<PayoutRequest> _payoutRepo;
    private readonly IRepository<LawyerProfile> _lawyerRepo;
    private readonly IRepository<Case> _caseRepo;
    private readonly UserManager<User> _userManager;

    public PaymentService(
        IRepository<PaymentOrder> orderRepo,
        IRepository<PayoutRequest> payoutRepo,
        IRepository<LawyerProfile> lawyerRepo,
        IRepository<Case> caseRepo,
        UserManager<User> userManager)
    {
        _orderRepo = orderRepo;
        _payoutRepo = payoutRepo;
        _lawyerRepo = lawyerRepo;
        _caseRepo = caseRepo;
        _userManager = userManager;
    }

    public async Task<PaymentOrder> CreateHonorariumOrderAsync(
        int caseId, int? userId, decimal amount)
    {
        var c = await _caseRepo.GetByIdAsync(caseId)
                ?? throw new ArgumentException("Case not found");

        var commission = Math.Round(amount * DefaultCommissionRate, 2);
        var order = new PaymentOrder
        {
            UserId = userId,
            CaseId = caseId,
            // Lawyer id resolved from the case's claimed document
            LawyerProfileId = c.Documents?.LastOrDefault()?.AssignedLawyerProfileId,
            Purpose = PaymentPurpose.Honorarium,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Commission = commission,
            NetToLawyer = amount - commission,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();

        if (order.LawyerProfileId.HasValue)
        {
            c.HonorariumPaid = true; // optimistic flag; refund resets
            await _caseRepo.SaveChangesAsync();
        }
        return order;
    }

    public async Task<PaymentOrder> CreateTopUpOrderAsync(int? userId, decimal amount)
    {
        var order = new PaymentOrder
        {
            UserId = userId,
            Purpose = PaymentPurpose.TopUp,
            Status = PaymentStatus.Pending,
            Amount = amount,
            Commission = 0,
            NetToLawyer = 0,
            CreatedAt = DateTime.UtcNow
        };
        await _orderRepo.AddAsync(order);
        await _orderRepo.SaveChangesAsync();
        return order;
    }

    // Sandbox "IPN confirmed" action.
    public async Task MarkPaidAsync(int paymentOrderId, string gatewayRef)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId)
                ?? throw new ArgumentException("Order not found");
        o.Status = PaymentStatus.Paid;
        o.GatewayRef = gatewayRef;
        o.PaidAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();
    }

    public async Task MarkFailedAsync(int paymentOrderId)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (o == null) return;
        o.Status = PaymentStatus.Failed;
        await _orderRepo.SaveChangesAsync();
    }

    public async Task RefundAsync(int paymentOrderId)
    {
        var o = await _orderRepo.GetByIdAsync(paymentOrderId);
        if (o == null || o.Status != PaymentStatus.Paid) return;
        o.Status = PaymentStatus.Refunded;
        o.RefundedAt = DateTime.UtcNow;
        await _orderRepo.SaveChangesAsync();

        if (o.CaseId.HasValue && o.Purpose == PaymentPurpose.Honorarium)
        {
            var c = await _caseRepo.GetByIdAsync(o.CaseId.Value);
            if (c != null)
            {
                c.HonorariumPaid = false; // ledger reversed
                await _caseRepo.SaveChangesAsync();
            }
        }
    }

    public async Task<IReadOnlyList<PaymentOrderDto>> GetOrdersAsync()
    {
        var orders = (await _orderRepo.GetAllAsync())
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        var result = new List<PaymentOrderDto>();
        foreach (var o in orders)
        {
            string? lawyerName = null;
            if (o.LawyerProfileId.HasValue)
            {
                var p = await _lawyerRepo.GetByIdAsync(o.LawyerProfileId.Value);
                if (p != null)
                {
                    var u = await _userManager.FindByIdAsync(p.UserId.ToString());
                    lawyerName = u?.FullName;
                }
            }
            result.Add(new PaymentOrderDto(
                o.PaymentOrderId, o.CaseId, o.Purpose.ToString(), o.Status.ToString(),
                o.Amount, o.Commission, o.NetToLawyer, o.GatewayRef,
                o.CreatedAt, o.PaidAt, o.RefundedAt,
                UserEmail: null, LawyerName: lawyerName));
        }
        return result;
    }

    public async Task<LawyerEarningsDto> GetLawyerEarningsAsync(int lawyerProfileId)
    {
        var all = await _orderRepo.GetAllAsync();
        var paid = all.Where(o => o.LawyerProfileId == lawyerProfileId
                               && o.Purpose == PaymentPurpose.Honorarium
                               && o.Status == PaymentStatus.Paid)
                      .OrderByDescending(o => o.PaidAt)
                      .ToList();

        var payouts = (await _payoutRepo.GetAllAsync())
            .Where(p => p.LawyerProfileId == lawyerProfileId && p.IsPaid)
            .ToList();

        var balance = paid.Sum(o => o.NetToLawyer) - payouts.Sum(p => p.Amount);

        return new LawyerEarningsDto(
            balance,
            paid.Select(o => new EarningRowDto(
                o.PaymentOrderId, o.CaseId ?? 0, o.Amount, o.Commission, o.NetToLawyer,
                o.PaidAt ?? o.CreatedAt)).ToList());
    }

    public async Task RequestPayoutAsync(int lawyerProfileId, decimal amount)
    {
        await _payoutRepo.AddAsync(new PayoutRequest
        {
            LawyerProfileId = lawyerProfileId,
            Amount = amount,
            IsPaid = false,
            RequestedAt = DateTime.UtcNow
        });
        await _payoutRepo.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<PayoutRequest>> GetPendingPayoutsAsync()
    {
        var all = await _payoutRepo.GetAllAsync();
        return all.Where(p => !p.IsPaid).OrderBy(p => p.RequestedAt).ToList();
    }

    public async Task ApprovePayoutAsync(int payoutRequestId)
    {
        var p = await _payoutRepo.GetByIdAsync(payoutRequestId);
        if (p == null) return;
        p.IsPaid = true;
        p.PaidAt = DateTime.UtcNow;
        await _payoutRepo.SaveChangesAsync();
    }
}
```
**Note:** `PaymentOrderDto` uses named arguments for the last two fields (`UserEmail: null, LawyerName: lawyerName`) — records support this; the positional record declaration order is `(…, string? UserEmail, string? LawyerName)` matching the call.

- [ ] **Step 3: Register in DI + Admin Transactions actions**

**3a.** Program.cs (after LawyerReviewService registration):
```csharp
builder.Services.AddScoped<PaymentService>();
```

**3b.** AdminController — extend ctor with `PaymentService paymentService` (field `_paymentService`) and add:
```csharp
    [HttpGet]
    public async Task<IActionResult> Transactions()
    {
        var orders = await _paymentService.GetOrdersAsync();
        var payouts = await _paymentService.GetPendingPayoutsAsync();
        ViewData["Payouts"] = payouts;
        return View(orders);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundOrder(int orderId)
    {
        await _paymentService.RefundAsync(orderId);
        TempData["Success"] = "Order refunded (sandbox) — ledger reversed.";
        return RedirectToAction(nameof(Transactions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePayout(int payoutRequestId)
    {
        await _paymentService.ApprovePayoutAsync(payoutRequestId);
        TempData["Success"] = "Payout marked paid (sandbox).";
        return RedirectToAction(nameof(Transactions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrderPaid(int orderId)
    {
        // Sandbox gateway confirm (in lieu of real SSLCommerz IPN)
        await _paymentService.MarkPaidAsync(orderId, $"SBX-{Guid.NewGuid().ToString("N")[..12].ToUpper()}");
        TempData["Success"] = "Order marked Paid (sandbox gateway).";
        return RedirectToAction(nameof(Transactions));
    }
```

- [ ] **Step 4: Create Views/Admin/Transactions.cshtml**

```html
@model IReadOnlyList<MuktoAin.Application.DTOs.PaymentOrderDto>
@{
    ViewData["Title"] = "Transactions — Admin — MuktoAin";
    ViewData["IsAdminPage"] = true;
    var payouts = (IReadOnlyList<MuktoAin.Domain.Entities.PayoutRequest>)ViewData["Payouts"];
}

<main class="container" id="main">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <span class="sep">/</span>
        <span>Transactions</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="receipt"></i> FR-24</span>
        <h1 class="page-title">Transactions</h1>
        <p class="page-sub">
            <span class="badge badge-warn sandbox-badge">ডেমো মোড — স্যান্ডবক্স পেমেন্ট / Sandbox — no real money</span>
        </p>
    </div>

    <div class="card" style="margin-bottom: 20px">
        <h2 class="section-h"><i data-lucide="landmark"></i> Orders</h2>
        <table>
            <thead>
                <tr><th>Order</th><th>Purpose</th><th>Case</th><th>Amount</th><th>Commission</th><th>Net to lawyer</th><th>Status</th><th>Actions</th></tr>
            </thead>
            <tbody>
                @foreach (var o in Model)
                {
                    <tr>
                        <td class="mono">#@o.PaymentOrderId<br /><small class="muted">@o.CreatedAt.ToString("d MMM HH:mm")</small></td>
                        <td>@o.Purpose</td>
                        <td>@(o.CaseId.HasValue ? $"CASE-{o.CaseId}" : "—")</td>
                        <td>৳@o.Amount</td>
                        <td>৳@o.Commission</td>
                        <td>৳@o.NetToLawyer @(o.LawyerName != null ? $"· {o.LawyerName}" : "")</td>
                        <td><span class="badge badge-@(o.Status == "Paid" ? "final" : o.Status == "Refunded" ? "neutral" : o.Status == "Failed" ? "rejected" : "draft")">@o.Status</span></td>
                        <td>
                            @if (o.Status == "Pending")
                            {
                                <form asp-action="MarkOrderPaid" method="post" style="display:inline">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="orderId" value="@o.PaymentOrderId" />
                                    <button class="btn btn-outline btn-sm" type="submit">Mark paid (SBX)</button>
                                </form>
                            }
                            else if (o.Status == "Paid")
                            {
                                <form asp-action="RefundOrder" method="post" style="display:inline"
                                      data-confirm="Refund this order (sandbox) and reverse the ledger?">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="orderId" value="@o.PaymentOrderId" />
                                    <button class="btn btn-danger-outline btn-sm" type="submit">Refund</button>
                                </form>
                            }
                        </td>
                    </tr>
                }
                @if (!Model.Any())
                {
                    <tr><td colspan="8" class="muted" style="text-align:center">No orders yet (sandbox).</td></tr>
                }
            </tbody>
        </table>
    </div>

    <div class="card">
        <h2 class="section-h"><i data-lucide="banknote"></i> Payout requests</h2>
        @if (payouts.Any())
        {
            <table>
                <thead><tr><th>Request</th><th>Lawyer</th><th>Amount</th><th>Requested</th><th>Action</th></tr></thead>
                <tbody>
                    @foreach (var p in payouts)
                    {
                        <tr>
                            <td class="mono">#@p.PayoutRequestId</td>
                            <td>Lawyer #@p.LawyerProfileId</td>
                            <td>৳@p.Amount</td>
                            <td>@p.RequestedAt.ToString("d MMM HH:mm")</td>
                            <td>
                                <form asp-action="ApprovePayout" method="post" style="display:inline">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="payoutRequestId" value="@p.PayoutRequestId" />
                                    <button class="btn btn-outline btn-sm" type="submit">Approve → mark paid</button>
                                </form>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
        else
        {
            <p class="muted">No pending payout requests.</p>
        }
    </div>
</main>
```
Append to main.css:
```css
/* Sandbox payment surfaces (Part B) */
.sandbox-badge { background: var(--warn-bg); color: var(--ink); border: 1px solid var(--warn-border); }
```

- [ ] **Step 5: Lawyer Earnings card on Profile**

**5a.** `ProfileViewModels.cs` — add to `ProfileViewModel`:
```csharp
    // FR-24 (lawyer variant)
    public decimal? EarningsBalance { get; set; }
    public List<EarningRowViewModel>? EarningsHistory { get; set; }
```
And append the row VM:
```csharp
public class EarningRowViewModel
{
    public int PaymentOrderId { get; set; }
    public int CaseId { get; set; }
    public decimal Gross { get; set; }
    public decimal Commission { get; set; }
    public decimal Net { get; set; }
    public DateTime PaidAt { get; set; }
}
```

**5b.** `AccountController.Profile` GET — after the existing VM build and ONLY when the user is a lawyer with an approved profile, add (inject `PaymentService` into `AccountController` ctor as `_paymentService`, plus the existing `IRepository<LawyerProfile>` if not already there):
```csharp
    if (isLawyer)
    {
        var profiles = await _lawyerProfileRepo.GetAllAsync();
        var profile = profiles.FirstOrDefault(p => p.UserId == vm.UserId);
        if (profile != null)
        {
            var earnings = await _paymentService.GetLawyerEarningsAsync(profile.LawyerProfileId);
            vm.EarningsBalance = earnings.Balance;
            vm.EarningsHistory = earnings.History.Select(h => new EarningRowViewModel
            {
                PaymentOrderId = h.PaymentOrderId,
                CaseId = h.CaseId,
                Gross = h.Gross,
                Commission = h.Commission,
                Net = h.Net,
                PaidAt = h.PaidAt
            }).ToList();
            ViewData["LawyerProfileId"] = profile.LawyerProfileId;
        }
    }
```
(Adapt variable names to the action's actual structure — the intent: lawyer + approved ⇒ populate earnings. Read the current Profile GET first; it builds `ProfileViewModel` with a UserId source — reuse whichever variable holds the int user id.)

**5c.** `Views/Account/Profile.cshtml` — add this card after the existing password/security card, guarded:
```html
@if (Model.EarningsBalance.HasValue)
{
    <div class="card" style="margin-top: 16px">
        <div class="row spread wrap" style="align-items: center; gap: 10px">
            <div>
                <h2 class="section-h" style="margin:0"><i data-lucide="wallet"></i> <span data-bn="আয় (স্যান্ডবক্স)" data-en="Earnings (sandbox)">আয় (স্যান্ডবক্স)</span></h2>
                <span class="badge badge-warn sandbox-badge tiny">ডেমো মোড — স্যান্ডবক্স পেমেন্ট</span>
            </div>
            <div style="font-size: 32px; font-weight: 700">৳@Model.EarningsBalance</div>
        </div>
        @if (Model.EarningsHistory is { Count: > 0 })
        {
            <table style="margin-top: 12px">
                <thead><tr><th>Case</th><th>Gross</th><th>Commission</th><th>Net</th><th>Paid</th></tr></thead>
                <tbody>
                    @foreach (var e in Model.EarningsHistory)
                    {
                        <tr>
                            <td class="mono">CASE-@e.CaseId</td>
                            <td>৳@e.Gross</td>
                            <td>৳@e.Commission</td>
                            <td><b>৳@e.Net</b></td>
                            <td class="tiny muted">@e.PaidAt.ToString("d MMM")</td>
                        </tr>
                    }
                </tbody>
            </table>
        }
        else
        {
            <p class="muted tiny" style="margin-top: 8px" data-bn="এখনো কোনো সম্মানী নেই।" data-en="No honoraria yet.">এখনো কোনো সম্মানী নেই।</p>
        }
        <form asp-controller="Account" asp-action="RequestPayout" method="post" style="margin-top: 12px">
            @Html.AntiForgeryToken()
            <button class="btn btn-outline btn-sm" type="submit">
                <i data-lucide="banknote"></i> <span data-bn="পরিশোধ চান" data-en="Request payout">পরিশোধ চান</span>
            </button>
        </form>
    </div>
}
```
**5d.** `AccountController` — add the payout POST:
```csharp
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPayout()
    {
        var userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (int?)null;
        if (userId == null) return RedirectToAction("Login");
        var profiles = await _lawyerProfileRepo.GetAllAsync();
        var profile = profiles.FirstOrDefault(p => p.UserId == userId);
        if (profile == null) return Forbid();

        var earnings = await _paymentService.GetLawyerEarningsAsync(profile.LawyerProfileId);
        if (earnings.Balance <= 0)
        {
            TempData["Error"] = "পরিশোধযোগ্য ব্যালেন্স নেই।";
            TempData["ErrorEn"] = "No payable balance.";
            return RedirectToAction(nameof(Profile));
        }
        await _paymentService.RequestPayoutAsync(profile.LawyerProfileId, earnings.Balance);
        TempData["Success"] = "পরিশোধের অনুরোধ জমা হয়েছে (স্যান্ডবক্স)।";
        TempData["SuccessEn"] = "Payout request submitted (sandbox).";
        return RedirectToAction(nameof(Profile));
    }
```

- [ ] **Step 6: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Application/Services/PaymentService.cs src/MuktoAin.Application/DTOs/PaymentDto.cs src/MuktoAin.Web/Controllers/AdminController.cs src/MuktoAin.Web/Controllers/AccountController.cs src/MuktoAin.Web/Views/Admin/Transactions.cshtml src/MuktoAin.Web/Views/Account/Profile.cshtml src/MuktoAin.Web/ViewModels/ProfileViewModels.cs src/MuktoAin.Web/Program.cs src/MuktoAin.Web/wwwroot/assets/css/main.css
git commit -m "feat(pay): PaymentService ledger + admin Transactions + lawyer Earnings card (sandbox)"
```

---

### Task B7: ForgotPassword + ResetPassword + logout-everywhere + Search/Category/About/Error surfaces

**Files:**
- Modify: `src/MuktoAin.Web/Controllers/AccountController.cs` (Forgot/Reset actions + LogoutEverywhere)
- Create: `src/MuktoAin.Web/Views/Account/ForgotPassword.cshtml`
- Create: `src/MuktoAin.Web/Views/Account/ResetPassword.cshtml`
- Create: `src/MuktoAin.Web/ViewModels/PasswordResetViewModels.cs`
- Modify: `src/MuktoAin.Web/Views/Account/Login.cshtml` (forgot link — one line)
- Modify: `src/MuktoAin.Web/Views/Account/Profile.cshtml` (logout-everywhere button)
- Modify: `src/MuktoAin.Web/Controllers/SearchController.cs` (act filter param)
- Modify: `src/MuktoAin.Web/Views/Search/Index.cshtml` (working filter + ask-AI chip)
- Modify: `src/MuktoAin.Web/Views/Category/Details.cshtml` (chat-launcher CTAs)
- Modify: `src/MuktoAin.Web/Views/Home/About.cshtml` (absorb landing content + payment copy)

**Interfaces:**
- Consumes: `UserManager<User>.GeneratePasswordResetTokenAsync` / `ResetPasswordAsync` (standard Identity — available: IdentityCore registered with default token providers per Program.cs); `SignInManager.SignOutAsync`; `SearchService.SearchActsAsync(string query, int page, int pageSize, int? actId)` (VERIFIED signature — act filter already supported in the service!); `IActRepository.GetAllAsync` for the filter dropdown.
- Produces: `GET/POST /Account/ForgotPassword`, `GET/POST /Account/ResetPassword` (token flow; in Development the token+link is surfaced via TempData/dev-toast since SMTP is not configured — honest dev behavior documented in-view), `POST /Account/LogoutEverywhere` (security-stamp refresh), `GET /Search/Index?q=&page=&actId=`.

- [ ] **Step 1: Password reset VMs + actions**

`src/MuktoAin.Web/ViewModels/PasswordResetViewModels.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Token { get; set; } = string.Empty;
    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
```

`AccountController` — add (needs `SignInManager<User>` in ctor if absent — verify first; it is likely already injected for Login):
```csharp
    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user == null || user.AccountStatus == Domain.Enums.AccountStatus.Suspended)
        {
            // Do NOT reveal account existence — generic confirmation either way
            TempData["Info"] = "যদি অ্যাকাউন্টটি থাকে, রিসেট লিংক ইমেইলে পাঠানো হয়েছে।";
            TempData["InfoEn"] = "If the account exists, a reset link has been emailed.";
            return RedirectToAction(nameof(Login));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Action(nameof(ResetPassword), "Account",
            new { email = vm.Email, token }, Request.Scheme);

        // SMTP is not configured in the academic build. In Development, show
        // the link directly (dev convenience, honestly labeled); in Production
        // this is where an email would be sent (documented gap).
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
        {
            TempData["Info"] = "ডেভ মোড: রিসেট লিংক — " + resetUrl;
            TempData["InfoEn"] = "Dev mode reset link: " + resetUrl;
        }
        else
        {
            TempData["Info"] = "যদি অ্যাকাউন্টটি থাকে, রিসেট লিংক ইমেইলে পাঠানো হয়েছে।";
            TempData["InfoEn"] = "If the account exists, a reset link has been emailed.";
        }
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(ForgotPassword));
        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user == null) return RedirectToAction(nameof(Login));

        var result = await _userManager.ResetPasswordAsync(user, vm.Token, vm.NewPassword);
        if (result.Succeeded)
        {
            TempData["Success"] = "পাসওয়ার্ড পরিবর্তন হয়েছে — সাইন ইন করুন।";
            TempData["SuccessEn"] = "Password changed — please sign in.";
            return RedirectToAction(nameof(Login));
        }
        foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutEverywhere()
    {
        var userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (int?)null;
        if (userId != null)
        {
            var user = await _userManager.FindByIdAsync(userId.Value.ToString());
            if (user != null)
            {
                // Rotating the security stamp invalidates all existing cookies
                await _userManager.UpdateSecurityStampAsync(user);
            }
        }
        await _signInManager.SignOutAsync();
        TempData["Info"] = "সব ডিভাইস থেকে লগ আউট হয়েছে।";
        TempData["InfoEn"] = "Signed out of all devices.";
        return RedirectToAction(nameof(Login));
    }
```
Add `using Microsoft.AspNetCore.Authorization;` if missing. Verify the ctor field names for `UserManager`/`SignInManager` in the existing controller and use THOSE names (likely `_userManager`/`_signInManager`).

- [ ] **Step 2: The two views (auth-split pattern matching Login/Register)**

`Views/Account/ForgotPassword.cshtml`:
```html
@model ForgotPasswordViewModel
@{
    ViewData["Title"] = "পাসওয়ার্ড রিসেট — মুক্ত আইন";
    Layout = "_Layout";
}

<main class="container" id="main" style="max-width: 520px">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index">হোম</a>
        <span class="sep">/</span>
        <span>পাসওয়ার্ড রিসেট</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="key-round"></i> FR-22</span>
        <h1 class="page-title">পাসওয়ার্ড ভুলে গেছেন?</h1>
        <p class="page-sub">আপনার ইমেইল দিন — রিসেট লিংক পাঠানো হবে।</p>
    </div>

    <div class="card">
        <form asp-action="ForgotPassword" method="post">
            @Html.AntiForgeryToken()
            <div asp-validation-summary="ModelOnly" class="text-danger"></div>
            <label class="form-label" asp-for="Email">ইমেইল</label>
            <input class="input" asp-for="Email" type="email" placeholder="you@example.com" required />
            <span class="text-danger" asp-validation-for="Email"></span>
            <button class="btn btn-primary btn-block" style="margin-top: 14px" type="submit">
                <i data-lucide="mail"></i> রিসেট লিংক পাঠান
            </button>
        </form>
        <p class="muted tiny" style="margin-top: 12px">
            <a asp-controller="Account" asp-action="Login">সাইন ইনে ফিরুন / Back to sign in</a>
        </p>
    </div>
</main>
```

`Views/Account/ResetPassword.cshtml`:
```html
@model ResetPasswordViewModel
@{
    ViewData["Title"] = "নতুন পাসওয়ার্ড — মুক্ত আইন";
}

<main class="container" id="main" style="max-width: 520px">
    <nav class="breadcrumbs" aria-label="Breadcrumb">
        <a asp-controller="Home" asp-action="Index">হোম</a>
        <span class="sep">/</span>
        <span>নতুন পাসওয়ার্ড</span>
    </nav>

    <div class="page-head">
        <span class="kicker"><i data-lucide="shield-check"></i> FR-22</span>
        <h1 class="page-title">নতুন পাসওয়ার্ড দিন</h1>
    </div>

    <div class="card">
        <form asp-action="ResetPassword" method="post">
            @Html.AntiForgeryToken()
            <div asp-validation-summary="All" class="text-danger"></div>
            <input type="hidden" asp-for="Email" />
            <input type="hidden" asp-for="Token" />
            <label class="form-label" asp-for="Email">ইমেইল</label>
            <input class="input" asp-for="Email" readonly />
            <label class="form-label" style="margin-top: 10px" asp-for="NewPassword">নতুন পাসওয়ার্ড</label>
            <input class="input" asp-for="NewPassword" type="password" required />
            <label class="form-label" style="margin-top: 10px" asp-for="ConfirmPassword">আবার লিখুন</label>
            <input class="input" asp-for="ConfirmPassword" type="password" required />
            <button class="btn btn-primary btn-block" style="margin-top: 14px" type="submit">
                <i data-lucide="check"></i> পরিবর্তন করুন
            </button>
        </form>
    </div>
</main>
```

- [ ] **Step 3: Wire links (Login forgot-link + Profile logout-everywhere)**

**3a.** `Views/Account/Login.cshtml` — find the forgot-password stub element (currently a toast link/button near the submit). Replace its `href`/target with:
```html
<a asp-controller="Account" asp-action="ForgotPassword">পাসওয়ার্ড ভুলে গেছেন? / Forgot password?</a>
```
(Read the file first; keep its classes/attributes, change only the destination. If it was a `<button onclick="showToast…">`, convert to this anchor with the same classes.)

**3b.** `Views/Account/Profile.cshtml` — in the password/security card, ADD after the existing change-password form:
```html
<form asp-action="LogoutEverywhere" method="post" style="margin-top: 12px">
    @Html.AntiForgeryToken()
    <button class="btn btn-quiet btn-sm" type="submit" data-confirm="সব ডিভাইস থেকে লগ আউট হবে — নিশ্চিত? / Sign out everywhere?">
        <i data-lucide="log-out"></i> <span data-bn="সব ডিভাইস থেকে লগ আউট" data-en="Log out everywhere">সব ডিভাইস থেকে লগ আউট</span>
    </button>
</form>
```

- [ ] **Step 4: Search act-filter + ask-AI chip**

**4a.** `SearchController.Index` — REPLACE the action with (it already has the exact signature pieces; the change is `actId` passthrough + VM):
```csharp
    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1, int? actId = null)
    {
        // Act filter dropdown data (always, so the filter renders on empty state too)
        var acts = await _actRepo.GetAllAsync();
        ViewBag.Acts = acts.OrderBy(a => a.Title).Select(a => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
        {
            Value = a.ActId.ToString(),
            Text = $"{a.Title} ({a.Year})"
        }).ToList();

        if (string.IsNullOrWhiteSpace(q))
        {
            return View(new SearchViewModel());
        }

        var result = await _searchService.SearchActsAsync(q, page, PageSize, actId);
        var vm = ToViewModel(result);
        vm.ActId = actId;
        return View(vm);
    }
```
Add ctor param `IActRepository actRepo` + field `_actRepo` (namespace `MuktoAin.Domain.Interfaces.Repositories`), and `SearchService` is already injected as `_searchService` (verified). Add to `SearchViewModel` (MiscellaneousViewModels.cs): `public int? ActId { get; set; }`.

**4b.** `Views/Search/Index.cshtml` — in the search form, add the act filter select BEFORE the submit button (keep existing classes; the current decorative checkbox rail is DELETED):
```html
<select class="input" name="actId" asp-for="ActId" asp-items="(List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>)ViewBag.Acts">
    <option value="">সব আইন / All acts</option>
</select>
```
And delete the right-rail act-filter checkbox card (search-tips card may stay). Then per result row, add the ask-AI chip next to the existing "File a Case" action:
```html
<a class="chip chip-sm" asp-controller="Home" asp-action="Index"
   asp-route-prefill='@($"এই ধারাটি আমার মামলায় প্রযোজ্য কি? {r.ActTitle} {(string.IsNullOrWhiteSpace(r.SectionNumber) ? "" : "ধারা " + r.SectionNumber)}")'>
    AI-কে জিজ্ঞেস করুন
</a>
```
(Adapt the loop variable name to the view's actual one — read the file; results loop uses a model item with `ActTitle`/`SectionNumber` properties per SearchResultItemViewModel.)

- [ ] **Step 5: Category Details chat-launcher + About absorption**

**5a.** `Views/Category/Details.cshtml` — in the common-actions section, convert each action item into a chat-launching link (keep the surrounding markup; the pattern per item):
```html
<a class="chip" asp-controller="Home" asp-action="Index"
   asp-route-prefill="@action">@action</a>
```
(Read the file first — its current loop renders `Model.CommonActions`; wrap each rendered item in this anchor. Also update the primary CTA from plain `Case/Submit` to `Case/Submit?cat=@Model.CategoryId` — pre-selection wiring from the spec.)

**5b.** `Views/Home/About.cshtml` — ADD two sections (keep everything already there; the About page already contains how-it-works + FAQ + disclaimer + dataset — verified). Add immediately after the mission paragraphs:
```html
<section class="card card-pad-lg" style="margin: 16px 0">
    <span class="kicker"><i data-lucide="badge-check"></i> Trust & Academic Context</span>
    <h3>সম্পূর্ণ বিনামূল্যে মূল সেবা — Free Core Service</h3>
    <p class="muted">
        মুক্ত আইন কোনো বাণিজ্যিক সংস্থা নয় — এটি একাডেমিক প্রকল্প। চ্যাট, অধিকার ব্যাখ্যা, খসড়া তৈরি, আইনজীবী পর্যালোচনা ও PDF —
        সবই বিনামূল্যে। ঐচ্ছিক স্যান্ডবক্স পেমেন্ট (কোটা টপ-আপ ও আইনজীবীর সম্মানী) ডেমো মোডে চলে — কোনো বাস্তব লেনদেন হয় না।
    </p>
    <p class="muted tiny">
        Free core service forever. Optional sandbox payments (quota top-ups & lawyer honoraria) run in demo mode — no real money.
    </p>
</section>
```

- [ ] **Step 6: Error pages to template versions**

`Views/Home/NotFound.cshtml` — replace CTA buttons block with (keep the big-404 styling):
```html
<a class="btn btn-primary" asp-controller="Home" asp-action="Index">হোমে ফিরুন / Back to chat home</a>
```
`Views/Home/AccessDenied.cshtml` — CTAs:
```html
<a class="btn btn-primary" asp-controller="Account" asp-action="Login">সাইন ইন / Sign in</a>
<a class="btn btn-outline" asp-controller="Account" asp-action="Profile">আমার প্রোফাইল / My profile</a>
```
`Views/Home/ServerError.cshtml` — replace the misleading "System Status" ghost link with retry:
```html
<button class="btn btn-primary" type="button" onclick="location.reload()">আবার চেষ্টা করুন / Try again</button>
<span class="muted tiny" style="margin-left: 10px">আপনার মামলার তথ্য সুরক্ষিত আছে / Your case data is safe</span>
```
(Read each file first; these replace only the CTA blocks — keep page structure.)

- [ ] **Step 7: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Web/Controllers/AccountController.cs src/MuktoAin.Web/Views/Account/ForgotPassword.cshtml src/MuktoAin.Web/Views/Account/ResetPassword.cshtml src/MuktoAin.Web/ViewModels/PasswordResetViewModels.cs src/MuktoAin.Web/Views/Account/Login.cshtml src/MuktoAin.Web/Views/Account/Profile.cshtml src/MuktoAin.Web/Controllers/SearchController.cs src/MuktoAin.Web/Views/Search/Index.cshtml src/MuktoAin.Web/Views/Category/Details.cshtml src/MuktoAin.Web/Views/Home/About.cshtml src/MuktoAin.Web/Views/Home/NotFound.cshtml src/MuktoAin.Web/Views/Home/AccessDenied.cshtml src/MuktoAin.Web/Views/Home/ServerError.cshtml src/MuktoAin.Web/ViewModels/MiscellaneousViewModels.cs
git commit -m "feat(web): password reset + logout-everywhere + search act filter + chat launchers + error pages"
```

---

### Task B8: Nav restructure (role variants + admin secondary bar)

**Files:**
- Modify: `src/MuktoAin.Web/Views/Shared/_Layout.cshtml` (nav links block + adminbar)

**Interfaces:**
- Consumes: routes produced by B2–B7 (`Lawyer/Status`, `Admin/Users|Lawyers|Corpus|Scenarios|Categories|AiLogs|Transactions`).
- Produces: the spec's 4 nav states + admin secondary bar; unverified lawyers see Status not Queue.

- [ ] **Step 1: Update the nav-links block in _Layout.cshtml**

Replace the `@if (isAdmin) {…} else if (isLawyer) {…} else {…}` nav-links block (keep everything outside it) with:
```html
@if (isAdmin)
{
    <a asp-controller="Admin" asp-action="Dashboard" aria-current="page"><i data-lucide="shield"></i>Overview</a>
}
else if (isLawyer)
{
    @if (isVerifiedLawyer)
    {
        <a asp-controller="Lawyer" asp-action="Queue"><i data-lucide="file-check-2"></i>রিভিউ কিউ</a>
    }
    else
    {
        <a asp-controller="Lawyer" asp-action="Status"><i data-lucide="badge-check"></i>ভেরিফিকেশন</a>
    }
    <a asp-controller="Search" asp-action="Index"><i data-lucide="search"></i>আইন খুঁজুন</a>
    <a asp-controller="Category" asp-action="Index"><i data-lucide="layout-grid"></i>বিষয়সমূহ</a>
    <a asp-controller="Home" asp-action="About"><i data-lucide="info"></i>পরিচিতি</a>
}
else
{
    <a asp-controller="Home" asp-action="Index"><i data-lucide="message-circle"></i>চ্যাট</a>
    <a asp-controller="Category" asp-action="Index"><i data-lucide="layout-grid"></i>বিষয়সমূহ</a>
    <a asp-controller="Search" asp-action="Index"><i data-lucide="search"></i>আইন খুঁজুন</a>
    @if (isAuthenticated)
    {
        <a asp-controller="Case" asp-action="Track"><i data-lucide="folder-open"></i>আমার মামলা</a>
    }
    <a asp-controller="Home" asp-action="About"><i data-lucide="info"></i>পরিচিতি</a>
}
```

- [ ] **Step 2: Compute isVerifiedLawyer in the layout @code block**

At the top of `_Layout.cshtml` there is a Razor code block computing `isAdmin`/`isLawyer`/`isCitizen` (verified earlier — lines ~10–30). ADD (do not remove existing):
```csharp
    bool isVerifiedLawyer = false;
    if (isLawyer && isAuthenticated)
    {
        try
        {
            var profiles = await HttpContext.RequestServices
                .GetRequiredService<MuktoAin.Domain.Interfaces.Repositories.IRepository<MuktoAin.Domain.Entities.LawyerProfile>>()
                .GetAllAsync();
            var uidStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uidStr, out var uid))
            {
                var p = profiles.FirstOrDefault(x => x.UserId == uid);
                isVerifiedLawyer = p?.VerificationStatus == MuktoAin.Domain.Enums.VerificationStatus.Approved;
            }
        }
        catch { /* nav falls back to unverified state — safe */ }
    }
```
If the layout's code block is NOT async-capable (`@{ }` vs `@code` / `@{ … }` with awaits requires the view be async), use this sync-resolve alternative instead (same behavior):
```csharp
    bool isVerifiedLawyer = false;
    if (isLawyer && isAuthenticated)
    {
        try
        {
            var repo = app.ApplicationServices.CreateScope().ServiceProvider
                .GetRequiredService<MuktoAin.Domain.Interfaces.Repositories.IRepository<MuktoAin.Domain.Entities.LawyerProfile>>();
            var profiles = repo.GetAllAsync().GetAwaiter().GetResult();
            var uidStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(uidStr, out var uid))
            {
                var p = profiles.FirstOrDefault(x => x.UserId == uid);
                isVerifiedLawyer = p?.VerificationStatus == MuktoAin.Domain.Enums.VerificationStatus.Approved;
            }
        }
        catch { }
    }
```
(`app` is reachable in a view via `IHttpContextAccessor`? NO — simplest legal sync path: inject via `@inject Microsoft.AspNetCore.Hosting.IWebHostEnvironment` is useless. USE: `var scopeFactory = (Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)Context.RequestServices.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory));` then `using var scope = scopeFactory.CreateScope();` then resolve the repo. Choose the async version if the file already contains `await` in its top block — check first.)

- [ ] **Step 3: Admin secondary bar**

Immediately AFTER the `</header>` of the navbar (before the disclaimer partial), add:
```html
@if (isAdmin)
{
    bool isAdminPage = (bool?)ViewData["IsAdminPage"] ?? User.HasClaim(System.Security.Claims.ClaimTypes.Role, "Admin") && Context.Request.Path.StartsWithSegments("/Admin");
    <nav class="nav-adminbar" aria-label="Admin sections">
        <a asp-controller="Admin" asp-action="Dashboard">Dashboard</a>
        <a asp-controller="Admin" asp-action="Users">Users</a>
        <a asp-controller="Admin" asp-action="Lawyers">Lawyers</a>
        <a asp-controller="Admin" asp-action="Corpus">Corpus</a>
        <a asp-controller="Admin" asp-action="Scenarios">Scenario Mappings</a>
        <a asp-controller="Admin" asp-action="Categories">Categories</a>
        <a asp-controller="Admin" asp-action="AiLogs">AI Logs</a>
        <a asp-controller="Admin" asp-action="Transactions">Transactions</a>
    </nav>
}
```
Append to main.css:
```css
/* Admin secondary bar (Part B) */
.nav-adminbar {
  display: flex; gap: 4px; overflow-x: auto; padding: 6px 16px;
  background: var(--surface-2); border-bottom: 1px solid var(--border);
}
.nav-adminbar a {
  font-size: 13px; font-weight: 600; color: var(--ink-2);
  padding: 6px 10px; border-radius: 8px; white-space: nowrap;
}
.nav-adminbar a:hover, .nav-adminbar a[aria-current="page"] {
  color: var(--primary); background: var(--primary-soft);
}
```
Also: dashboard's dead "Review Queue" link — admin no longer reviews (spec). In `Views/Admin/Dashboard.cshtml`, change the "আইনজীবী রিভিউ কিউ" button's controller/action to `Admin/Lawyers` (label: "Lawyer Verification").

- [ ] **Step 4: Build + commit**

Run: `dotnet build` — expected 0 errors.

```bash
git add src/MuktoAin.Web/Views/Shared/_Layout.cshtml src/MuktoAin.Web/Views/Admin/Dashboard.cshtml src/MuktoAin.Web/wwwroot/assets/css/main.css
git commit -m "feat(web): role nav variants + admin secondary bar (spec nav structure)"
```

---

## PART B — Final Verification Gate **[OPENCODE VERIFY — Antigravity stops here]**

1. `dotnet build` — clean.
2. `dotnet test tests/MuktoAin.UnitTests` — pass.
3. Human runs `scripts/09_part_b_tables.sql` in SSMS.
4. `dotnet run` + role-based browser pass:
   - **Lawyer (unverified demo `lawyer@muktoAin.bd`):** login → lands on `/Lawyer/Status` (pending state, no queue link).
   - **Admin:** `/Admin/Users` suspend/activate a citizen (login blocked after), `/Admin/Lawyers` approve the demo lawyer → lawyer re-login → Queue visible; reject with reason → reason shows on Status page.
   - **Lawyer (verified):** queue shows real UnderReview docs oldest-first; Claim → Review workspace compare; approve-with-edits → case Finalized + citizen unread dot; reject w/o reason → blocked.
   - **Admin:** `/Admin/Corpus|Scenarios|Categories|AiLogs|Transactions` render real data; Transactions Mark paid → Refund reverses; payout request from lawyer Profile → admin approves.
   - **Citizen:** forgot-password (dev link) → reset → login; Profile logout-everywhere kills session; Search act filter actually filters; category scenario → chat prefill.
5. Cross-part integration: with Part A also done, run the full E2E: chat → draft → case page → send → lawyer approve → PDF button state; rejection → chat return.
6. Update `plans/Dependency_plan.md` redesign-wave boxes.

**Part B ordering constraint:** B6's PaymentService depends on Part A's Task A1/A2 entities (PaymentOrder/PayoutRequest). If running truly parallel, B6 executes last (or after A1–A2 merge). B2–B5, B7, B8 are fully independent of Part A.
