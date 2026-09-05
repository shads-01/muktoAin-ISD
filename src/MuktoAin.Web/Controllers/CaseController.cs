using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class CaseController : Controller
{
    private const string TrackedCasesKey = "TrackedCases";

    private readonly CaseService _caseService;
    private readonly IRightsExplanationService _rightsExplanationService;
    private readonly DocumentService _documentService;
    private readonly ICaseRepository _caseRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly IRepository<LawyerReview> _reviewRepo;
    private readonly IRepository<LawyerProfile> _lawyerProfileRepo;

    public CaseController(
        CaseService caseService,
        IRightsExplanationService rightsExplanationService,
        DocumentService documentService,
        ICaseRepository caseRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo,
        IRepository<GeneratedDocument> docRepo,
        IRepository<LawyerReview> reviewRepo,
        IRepository<LawyerProfile> lawyerProfileRepo)
    {
        _caseService = caseService;
        _rightsExplanationService = rightsExplanationService;
        _documentService = documentService;
        _caseRepo = caseRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
        _docRepo = docRepo;
        _reviewRepo = reviewRepo;
        _lawyerProfileRepo = lawyerProfileRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Submit(int? cat, string? q)
    {
        var vm = await BuildSubmitViewModelAsync();
        if (cat.HasValue) vm.CategoryId = cat.Value;
        if (!string.IsNullOrWhiteSpace(q)) vm.Description = q;
        return View(vm);
    }

    // District list as JSON for the chat draft modal (A5).
    [HttpGet]
    public async Task<IActionResult> SubmitOptions()
    {
        var districts = await _districtRepo.GetAllAsync();
        return Json(new
        {
            districts = districts.OrderBy(d => d.Name)
                .Select(d => new { id = d.DistrictId, name = d.Name })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CaseSubmitViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync(vm);
            return View(vm);
        }

        var lang = !string.IsNullOrWhiteSpace(vm.Language) && vm.Language != "bn"
            ? vm.Language
            : (Request.Cookies["mkt-lang"] ?? vm.Language ?? "bn");

        var currentUserId = GetCurrentUserId();

        // Form answers become the case's transcript (unification rule: every
        // case = chat transcript + draft chain, regardless of entry path).
        var chatService = HttpContext.RequestServices
            .GetRequiredService<MuktoAin.Application.Services.ChatService>();
        var session = await chatService.GetOrCreateSessionAsync(currentUserId, null, vm.Title);
        await chatService.AppendMessageAsync(session.ChatSessionId, "user",
            $"বিষয়: {vm.Title}\nবিভাগ: {vm.Categories.FirstOrDefault(c => c.Value == vm.CategoryId.ToString())?.Text ?? vm.CategoryId.ToString()}\nবিবরণ: {vm.Description}", null);

        var categoryEntity = await _categoryRepo.GetByIdAsync(vm.CategoryId);
        var catName = categoryEntity?.Name ?? "";
        var documentType = catName switch
        {
            var n when n.Contains("শ্রম") || n.Contains("Labour", StringComparison.OrdinalIgnoreCase) => "LabourComplaint",
            var n when n.Contains("ডায়েরি") || n.Contains("Diary", StringComparison.OrdinalIgnoreCase) => "GeneralDiary",
            var n when n.Contains("তথ্য") || n.Contains("Information", StringComparison.OrdinalIgnoreCase) => "RtiRequest",
            var n when n.Contains("ভোক্তা") || n.Contains("Consumer", StringComparison.OrdinalIgnoreCase) => "ConsumerComplaint",
            _ => "LabourComplaint"
        };

        MuktoAin.Application.DTOs.ChatCommitResultDto result;
        try
        {
            result = await chatService.CommitToCaseAsync(
                session.ChatSessionId,
                vm.CategoryId,
                vm.DistrictId,
                vm.Title,
                notificationEmail: null,
                vm.IsAnonymous,
                currentUserId,
                documentType);
        }
        catch (Exception)
        {
            // Fall back to the legacy direct-submission path (no transcript)
            var dto = new CaseSubmissionDto(vm.CategoryId, vm.DistrictId, vm.Title, vm.Description, lang, vm.IsAnonymous);
            var legacy = await _caseService.SubmitCaseAsync(dto, currentUserId);
            RememberTrackedCase(legacy.CaseId, legacy.AnonymousTrackingCode);
            TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
            TempData["SuccessEn"] = "Case submitted successfully!";
            if (legacy.AnonymousTrackingCode != null)
                TempData["TrackingCode"] = legacy.AnonymousTrackingCode;
            return RedirectToAction(nameof(Result),
                new { id = legacy.CaseId, code = legacy.AnonymousTrackingCode });
        }

        RememberTrackedCase(result.CaseId, result.AnonymousTrackingCode);

        TempData["Success"] = "মামলা সফলভাবে জমা হয়েছে!";
        TempData["SuccessEn"] = "Case submitted successfully!";
        if (result.AnonymousTrackingCode != null)
            TempData["TrackingCode"] = result.AnonymousTrackingCode;

        return RedirectToAction(nameof(Result),
            new { id = result.CaseId, code = result.AnonymousTrackingCode });
    }

    [HttpGet]
    public async Task<IActionResult> Result(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var currentUserId = GetCurrentUserId();
        var role = GetCurrentUserRole();

        var detail = await _caseService.GetCaseDetailAsync(id, currentUserId, role, trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        if (caseEntity == null) return NotFound();

        var doc = caseEntity.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null)
        {
            // Legacy form-path case with no draft yet — generate once
            // (then served from AI_LOG/GENERATED_DOCUMENT on future views).
            caseEntity.Title = detail.Title;
            caseEntity.Description = detail.Description;
            var explanation = await _rightsExplanationService.ExplainRightsAsync(caseEntity);
            var docDto = await _documentService.GenerateDocumentAsync(id, explanation);
            doc = await _docRepo.GetByIdAsync(docDto.DocumentId);
            if (doc == null) return NotFound();
        }

        // Unread dot clear-on-view
        if (caseEntity.HasUnreadActivity)
        {
            caseEntity.HasUnreadActivity = false;
            await _caseRepo.SaveChangesAsync();
        }

        // Reviews are NOT loaded by GetWithDocumentsAsync (no lazy loading) —
        // fetch explicitly, same for the lawyer profile.
        var allReviews = await _reviewRepo.GetAllAsync();
        var review = allReviews
            .Where(r => r.DocumentId == doc.DocumentId)
            .OrderBy(r => r.ReviewId)
            .LastOrDefault();
        LawyerProfile? lawyer = null;
        if (doc.AssignedLawyerProfileId.HasValue)
            lawyer = await _lawyerProfileRepo.GetByIdAsync(doc.AssignedLawyerProfileId.Value);
        if (lawyer == null && review != null)
            lawyer = await _lawyerProfileRepo.GetByIdAsync(review.LawyerProfileId);

        string? lawyerName = null;
        if (lawyer != null)
        {
            var userManager = HttpContext.RequestServices
                .GetRequiredService<UserManager<MuktoAin.Domain.Entities.User>>();
            var lawyerUser = await userManager.FindByIdAsync(lawyer.UserId.ToString());
            lawyerName = lawyerUser?.FullName;
        }

        var vm = new CaseResultViewModel
        {
            CaseId = id,
            Title = detail.Title,
            Status = detail.Status,
            CategoryName = detail.CategoryName,
            DistrictName = detail.DistrictName,
            CreatedAt = detail.CreatedAt,
            TrackingCode = caseEntity.AnonymousTrackingCode ?? string.Empty,
            RightsExplanation = string.Empty,
            DocumentId = doc.DocumentId,
            DocumentContent = doc.ContentDraft,
            ContentFinal = doc.ContentFinal,
            DocumentStatus = doc.Status.ToString(),
            CanDownloadPdf = doc.Status == DocumentStatus.Approved,
            VersionNo = doc.VersionNo,
            CitizenEdited = doc.CitizenEdited,
            CanEdit = doc.Status == DocumentStatus.Draft || doc.Status == DocumentStatus.Rejected,
            TimelineCurrent = MapTimelineState(caseEntity.Status, doc.Status),
            LawyerName = lawyerName,
            LawyerBarNumber = lawyer?.BarRegistrationNumber,
            LawyerDecision = review?.Decision.ToString(),
            LawyerComments = review?.Comments,
            RejectionReason = doc.Status == DocumentStatus.Rejected ? review?.Comments : null,
            HonorariumPaid = caseEntity.HonorariumPaid
        };

        // Rights explanation from the cached AI_LOG entry — no regeneration
        // (fixes the generate-on-every-GET churn by design).
        var logRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.AiLog>>();
        var logs = await logRepo.GetAllAsync();
        var rightsLog = logs
            .Where(l => l.CaseId == id && l.RequestType == AiRequestType.RightsExplanation)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefault();
        vm.RightsExplanation = rightsLog?.ResponseText
            ?? "আইনি অধিকার বিশ্লেষণ প্রস্তুত হচ্ছে...";

        // Cited sections from CASE_ACT_REFERENCE (persisted at generation time).
        // The generic repo does no Includes and there is no lazy loading, so
        // Section/Act are hydrated by id (same approach as LawyerReviewService).
        var refRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.CaseActReference>>();
        var sectionRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.ActSection>>();
        var actRepo = HttpContext.RequestServices
            .GetRequiredService<IRepository<MuktoAin.Domain.Entities.Act>>();

        var actRefs = (await refRepo.GetAllAsync()).Where(r => r.CaseId == id).ToList();
        var sections = await sectionRepo.GetAllAsync();
        var acts = await actRepo.GetAllAsync();

        vm.CitedSections = actRefs
            .Select(r =>
            {
                var section = sections.FirstOrDefault(s => s.SectionId == r.SectionId);
                var act = section == null ? null : acts.FirstOrDefault(a => a.ActId == section.ActId);
                return new CitedSectionViewModel
                {
                    ActTitle = act?.Title ?? string.Empty,
                    SectionNumber = string.IsNullOrWhiteSpace(section?.SectionNumber)
                        ? string.Empty
                        : $"ধারা {section.SectionNumber}",
                    SectionText = section?.SectionText ?? string.Empty,
                    RelevanceScore = $"{Math.Round(r.RelevanceScore * 100)}%"
                };
            })
            .ToList();

        return View(vm);
    }

    private static string MapTimelineState(CaseStatus caseStatus, DocumentStatus docStatus) =>
        (caseStatus, docStatus) switch
        {
            (_, DocumentStatus.Rejected) => "Rejected",
            (CaseStatus.Finalized, _) => "Approved",
            (CaseStatus.UnderReview, _) => "UnderReview",
            _ => "DraftReady"
        };

    // FR-21: citizen edits the draft — saves ContentFinal, bumps version.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDraft(int id, string editedContent, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null) return NotFound();
        if (doc.Status != DocumentStatus.Draft && doc.Status != DocumentStatus.Rejected)
            return Forbid();

        if (!string.IsNullOrWhiteSpace(editedContent) && editedContent != doc.ContentFinal)
        {
            doc.ContentFinal = editedContent;
            doc.VersionNo++;
            doc.CitizenEdited = editedContent.Trim() != doc.ContentDraft.Trim();
            await _docRepo.SaveChangesAsync();
        }

        TempData["Success"] = $"খসড়া সংরক্ষিত হয়েছে (সংস্করণ {doc.VersionNo})।";
        TempData["SuccessEn"] = $"Draft saved (version {doc.VersionNo}).";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }

    // FR-13: citizen sends the draft to the lawyer pool (oldest-first, claim-based).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToLawyer(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc == null) return NotFound();
        if (doc.Status != DocumentStatus.Draft && doc.Status != DocumentStatus.Rejected)
            return Forbid();

        doc.Status = DocumentStatus.UnderReview;
        await _docRepo.SaveChangesAsync();
        await _caseService.TransitionStatusAsync(id, CaseStatus.UnderReview);

        TempData["Success"] = "আপনার খসড়া আইনজীবী পুলে পাঠানো হয়েছে।";
        TempData["SuccessEn"] = "Your draft was sent to the lawyer pool.";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }

    // FR-21: citizen withdraws a case (AI advised unsalvageable, or citizen
    // chooses to stop). Case stays viewable forever.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, string? code)
    {
        var trackingCode = ResolveTrackingCode(id, code);
        var detail = await _caseService.GetCaseDetailAsync(
            id, GetCurrentUserId(), GetCurrentUserRole(), trackingCode);
        if (detail == null) return NotFound();

        var caseEntity = await _caseRepo.GetWithDocumentsAsync(id);
        var doc = caseEntity?.Documents.OrderBy(d => d.DocumentId).LastOrDefault();
        if (doc != null && doc.Status == DocumentStatus.UnderReview)
            return Forbid(); // cannot withdraw while a lawyer holds it

        var ok = await _caseService.TransitionStatusAsync(id, CaseStatus.Finalized);
        if (!ok) return Forbid();

        TempData["Success"] = "মামলাটি প্রত্যাহৃত হয়েছে — যেকোনো সময় দেখতে পারবেন।";
        TempData["SuccessEn"] = "Case withdrawn — you can still view it anytime.";
        return RedirectToAction(nameof(Result), new { id, code = trackingCode });
    }

    [HttpGet]
    public async Task<IActionResult> Track(string? status, string? code)
    {
        var vm = new CaseTrackViewModel();

        // Guest tracking-code lookup (FR-8): valid code redirects straight to the case
        if (!string.IsNullOrWhiteSpace(code))
        {
            var all = await _caseRepo.GetAllAsync();
            var match = all.FirstOrDefault(c =>
                c.AnonymousTrackingCode == code.Trim());
            if (match != null)
            {
                return RedirectToAction(nameof(Result), new { id = match.CaseId, code = code.Trim() });
            }
            TempData["Error"] = "কোডটি মেলেনি — আবার চেষ্টা করুন।";
            TempData["ErrorEn"] = "Code did not match — try again.";
        }

        var currentUserId = GetCurrentUserId();

        if (currentUserId.HasValue)
        {
            var userCases = await _caseService.GetUserCasesAsync(currentUserId.Value);
            foreach (var detail in userCases)
            {
                vm.Cases.Add(await ToListItemAsync(detail, string.Empty));
            }
        }

        foreach (var (caseId, sessionCode) in GetTrackedCases())
        {
            if (vm.Cases.Any(c => c.CaseId == caseId)) continue;
            var detail = await _caseService.GetCaseDetailAsync(
                caseId, userId: null, UserRole.Citizen, sessionCode);
            if (detail == null) continue;
            vm.Cases.Add(await ToListItemAsync(detail, sessionCode));
        }

        // Server-side status filter (real param — fixes decorative chips)
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            vm.Cases = vm.Cases.Where(c => MatchesFilter(c.Status, status)).ToList();
        }

        vm.ActiveStatusFilter = status ?? "All";
        vm.LookupCode = code ?? string.Empty;
        return View(vm);
    }

    private static bool MatchesFilter(string caseStatus, string filter) =>
        filter switch
        {
            "Approved" => caseStatus == nameof(CaseStatus.Finalized),
            _ => caseStatus == filter // UnderReview / Submitted map directly
        };

    private async Task<CaseListItemViewModel> ToListItemAsync(CaseDetailDto detail, string code)
    {
        var entity = await _caseRepo.GetByIdAsync(detail.CaseId);
        return new CaseListItemViewModel
        {
            CaseId = detail.CaseId,
            TrackingCode = code,
            Title = detail.Title,
            CategoryName = detail.CategoryName,
            Status = detail.Status,
            CreatedAt = detail.CreatedAt,
            HasUnread = entity?.HasUnreadActivity ?? false
        };
    }

    private async Task<CaseSubmitViewModel> BuildSubmitViewModelAsync()
    {
        var vm = new CaseSubmitViewModel();
        await PopulateDropdownsAsync(vm);
        return vm;
    }

    private async Task PopulateDropdownsAsync(CaseSubmitViewModel vm)
    {
        var categories = await _categoryRepo.GetAllAsync();
        var districts = await _districtRepo.GetAllAsync();

        vm.Categories = categories
            .OrderBy(c => c.CategoryId)
            .Select(c => new SelectListItem { Value = c.CategoryId.ToString(), Text = c.Name })
            .ToList();

        vm.Districts = districts
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.DistrictId.ToString(), Text = d.Name })
            .ToList();
    }

    private string? ResolveTrackingCode(int caseId, string? queryCode)
    {
        if (!string.IsNullOrEmpty(queryCode)) return queryCode;
        if (TempData.Peek("TrackingCode") is string tempCode) return tempCode;
        return GetTrackedCases().FirstOrDefault(t => t.caseId == caseId).code;
    }

    private void RememberTrackedCase(int caseId, string? code)
    {
        if (code == null) return;
        var entries = GetTrackedCases();
        if (entries.Any(t => t.caseId == caseId)) return;
        entries.Add((caseId, code));
        SaveTrackedCases(entries);
    }

    private List<(int caseId, string code)> GetTrackedCases()
    {
        var raw = HttpContext.Session.GetString(TrackedCasesKey);
        if (string.IsNullOrEmpty(raw)) return new List<(int, string)>();

        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split(':', 2))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _))
            .Select(p => (int.Parse(p[0]), p[1]))
            .ToList();
    }

    private void SaveTrackedCases(List<(int caseId, string code)> entries)
    {
        HttpContext.Session.SetString(TrackedCasesKey,
            string.Join("|", entries.Select(t => $"{t.caseId}:{t.code}")));
    }


    private int? GetCurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var id) ? id : null;
    }

    private UserRole GetCurrentUserRole()
    {
        if (User.IsInRole(nameof(UserRole.Admin))) return UserRole.Admin;
        if (User.IsInRole(nameof(UserRole.Lawyer))) return UserRole.Lawyer;
        return UserRole.Citizen;
    }
}
