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
    private readonly PaymentService _paymentService;
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly UserManager<User> _userManager;

    public LawyerController(
        LawyerReviewService reviewService,
        LawyerVerificationService verificationService,
        PaymentService paymentService,
        IRepository<LawyerProfile> profileRepo,
        UserManager<User> userManager)
    {
        _reviewService = reviewService;
        _verificationService = verificationService;
        _paymentService = paymentService;
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
            RejectionReason = profile.RejectionReason,
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

    // Queue: documents in the pool, oldest-first (SLA). Filter chips:
    // All (default) · Unclaimed · Mine (my claimed docs, re-enterable).
    [HttpGet]
    public async Task<IActionResult> Queue(string? filter)
    {
        var profile = await MyProfileAsync();
        if (profile == null || profile.VerificationStatus != VerificationStatus.Approved)
            return RedirectToAction(nameof(Status));

        var queue = await _reviewService.GetQueueAsync(profile.LawyerProfileId, filter);

        var vm = new LawyerQueueViewModel
        {
            LawyerName = (await _userManager.FindByIdAsync(profile.UserId.ToString()))?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            Specialization = profile.Specialization ?? "",
            PendingCount = queue.Count,
            ActiveFilter = filter ?? "All",
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
                IsMine = profile.LawyerProfileId != 0 && q.ClaimedBy == profile.BarRegistrationNumber,
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

    private const int HistoryPageSize = 20;

    // "What did I review" — every decision this lawyer has submitted, newest
    // first. Filter chips: All/Approved/EditedApproved/Rejected + date range +
    // sort (date_desc default | date_asc | case_asc).
    [HttpGet]
    public async Task<IActionResult> History(string? decision, string? from, string? to, string? sort, int page = 1)
    {
        var profile = await MyProfileAsync();
        if (profile == null) return NotFound();

        DateTime? fromDate = DateTime.TryParse(from, out var f) ? f.Date : null;
        DateTime? toDate = DateTime.TryParse(to, out var t) ? t.Date.AddDays(1).AddTicks(-1) : null;

        var history = await _reviewService.GetHistoryAsync(profile.LawyerProfileId, decision, fromDate, toDate);
        // Service already returns newest-first; only re-sort for the other options.
        IEnumerable<ReviewHistoryItemDto> sorted = sort switch
        {
            "date_asc" => history.OrderBy(h => h.ReviewedAt),
            "case_asc" => history.OrderBy(h => h.CaseTitle, StringComparer.OrdinalIgnoreCase),
            _ => history
        };

        var vm = new LawyerHistoryViewModel
        {
            LawyerName = (await _userManager.FindByIdAsync(profile.UserId.ToString()))?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            ActiveFilter = decision ?? "All",
            FromDate = from,
            ToDate = to,
            Sort = sort ?? "date_desc",
            TotalCount = history.Count
        };
        vm.Page = Math.Max(1, Math.Min(page, Math.Max((int)Math.Ceiling(vm.TotalCount / (double)HistoryPageSize), 1)));
        vm.Items = sorted.Skip((vm.Page - 1) * HistoryPageSize).Take(HistoryPageSize)
            .Select(h => new LawyerHistoryItemViewModel
            {
                ReviewId = h.ReviewId,
                DocumentId = h.DocumentId,
                CaseId = h.CaseId,
                CaseTitle = h.CaseTitle,
                CategoryName = h.CategoryName,
                DistrictName = h.DistrictName,
                Decision = h.Decision.ToString(),
                Comments = h.Comments,
                ReviewedAt = h.ReviewedAt,
                VersionNo = h.VersionNo,
                DocumentText = h.DocumentText
            }).ToList();

        return View(vm);
    }

    // FR-24 (lawyer variant): balance + honorarium history, moved off the
    // shared Account/Profile page into its own lawyer-scoped route.
    [HttpGet]
    public async Task<IActionResult> Payments()
    {
        var profile = await MyProfileAsync();
        if (profile == null) return NotFound();

        var earnings = await _paymentService.GetLawyerEarningsAsync(profile.LawyerProfileId);
        var vm = new LawyerPaymentsViewModel
        {
            LawyerName = (await _userManager.FindByIdAsync(profile.UserId.ToString()))?.FullName ?? "",
            BarRegistrationNumber = profile.BarRegistrationNumber,
            Balance = earnings.Balance,
            History = earnings.History.Select(h => new EarningRowViewModel
            {
                PaymentOrderId = h.PaymentOrderId,
                CaseId = h.CaseId,
                Gross = h.Gross,
                Commission = h.Commission,
                Net = h.Net,
                PaidAt = h.PaidAt
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPayout()
    {
        var profile = await MyProfileAsync();
        if (profile == null) return NotFound();

        var earnings = await _paymentService.GetLawyerEarningsAsync(profile.LawyerProfileId);
        if (earnings.Balance <= 0)
        {
            TempData["Error"] = "পরিশোধযোগ্য ব্যালেন্স নেই।";
            TempData["ErrorEn"] = "No payable balance.";
            return RedirectToAction(nameof(Payments));
        }
        await _paymentService.RequestPayoutAsync(profile.LawyerProfileId, earnings.Balance);
        TempData["Success"] = "পরিশোধের অনুরোধ জমা হয়েছে (স্যান্ডবক্স)।";
        TempData["SuccessEn"] = "Payout request submitted (sandbox).";
        return RedirectToAction(nameof(Payments));
    }
}
