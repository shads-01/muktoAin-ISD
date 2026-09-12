using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web.Controllers;

public class DocumentController : Controller
{
    private readonly IRepository<GeneratedDocument>? _docRepo;
    private readonly DocumentService? _documentService;
    private readonly CaseService? _caseService;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument>? docRepo = null,
        DocumentService? documentService = null,
        CaseService? caseService = null)
    {
        _logger = logger;
        _docRepo = docRepo;
        _documentService = documentService;
        _caseService = caseService;
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, string? code = null)
    {
        if (id <= 0 || _docRepo == null)
        {
            return NotFound();
        }

        var doc = await _docRepo.GetByIdAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        if (_caseService != null)
        {
            var currentUserId = GetCurrentUserId();
            var currentRole = GetCurrentUserRole();
            var trackingCode = ResolveTrackingCode(doc.CaseId, code);
            var caseDetail = await _caseService.GetCaseDetailAsync(doc.CaseId, currentUserId, currentRole, trackingCode);
            if (caseDetail == null)
            {
                return Forbid();
            }
        }

        var isApproved = doc.Status == DocumentStatus.Approved;
        var vm = new DocumentPreviewViewModel
        {
            DocumentId = doc.DocumentId,
            CaseId = doc.CaseId,
            CaseTitle = $"মামলা #{doc.CaseId}",
            DocumentType = doc.DocumentType.ToString(),
            ContentDraft = doc.ContentDraft,
            ContentFinal = doc.ContentFinal,
            Status = doc.Status.ToString(),
            CanDownloadPdf = isApproved,
            CreatedAt = doc.CreatedAt
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, string? code = null)
    {
        if (id <= 0 || _docRepo == null)
        {
            return NotFound();
        }

        var doc = await _docRepo.GetByIdAsync(id);
        if (doc == null)
        {
            return NotFound();
        }

        if (_caseService != null)
        {
            var currentUserId = GetCurrentUserId();
            var currentRole = GetCurrentUserRole();
            var trackingCode = ResolveTrackingCode(doc.CaseId, code);
            var caseDetail = await _caseService.GetCaseDetailAsync(doc.CaseId, currentUserId, currentRole, trackingCode);
            if (caseDetail == null)
            {
                return Forbid();
            }
        }

        if (doc.Status != DocumentStatus.Approved)
        {
            TempData["Error"] = "পিডিএফ ডাউনলোড শুধুমাত্র একজন সনদপ্রাপ্ত আইনজীবীর অনুমোদনের পরই সম্ভব। / PDF download is available only after verified lawyer approval.";
            TempData["ErrorEn"] = "PDF download is available only after a verified lawyer approves this document.";
            return RedirectToAction(nameof(Preview), new { id, code });
        }

        if (_documentService == null)
        {
            TempData["Error"] = "পিডিএফ পরিষেবা উপলব্ধ নেই। / PDF export service is unavailable.";
            return RedirectToAction(nameof(Preview), new { id, code });
        }

        try
        {
            var pdf = await _documentService.GetPdfIfApprovedAsync(id);
            if (pdf == null || pdf.Length == 0)
            {
                TempData["Error"] = "পিডিএফ তৈরি করা যায়নি। / PDF could not be generated.";
                return RedirectToAction(nameof(Preview), new { id, code });
            }

            var fileName = $"MuktoAin-{id}-{DateTime.UtcNow:yyyyMMdd}.pdf";
            Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"{fileName}\"";
            return File(pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF export failed for document {DocumentId}", id);
            TempData["Error"] = "পিডিএফ তৈরি করতে সমস্যা হয়েছে। / An error occurred while generating the PDF.";
            return RedirectToAction(nameof(Preview), new { id, code });
        }
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

    private string? ResolveTrackingCode(int caseId, string? queryCode)
    {
        if (!string.IsNullOrEmpty(queryCode)) return queryCode;
        if (TempData.Peek("TrackingCode") is string tempCode) return tempCode;
        var raw = HttpContext?.Session?.GetString("TrackedCases");
        if (string.IsNullOrEmpty(raw)) return null;

        return raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Split(':', 2))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out var cid) && cid == caseId)
            .Select(p => p[1])
            .FirstOrDefault();
    }
}
