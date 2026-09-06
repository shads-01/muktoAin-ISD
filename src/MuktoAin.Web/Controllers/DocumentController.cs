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
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        ILogger<DocumentController> logger,
        IRepository<GeneratedDocument>? docRepo = null,
        DocumentService? documentService = null)
    {
        _logger = logger;
        _docRepo = docRepo;
        _documentService = documentService;
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        DocumentPreviewViewModel vm;

        if (_docRepo != null)
        {
            try
            {
                var doc = await _docRepo.GetByIdAsync(id);
                if (doc != null)
                {
                    var isApproved = doc.Status == DocumentStatus.Approved;
                    vm = new DocumentPreviewViewModel
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load document {DocumentId} from repository, falling back to mock", id);
            }
        }

        // Mock fallback for prototype / review flow demonstration
        vm = GetMockDocument(id);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        var isApproved = false;

        if (_docRepo != null)
        {
            try
            {
                var doc = await _docRepo.GetByIdAsync(id);
                if (doc != null)
                {
                    isApproved = doc.Status == DocumentStatus.Approved;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check document status for {DocumentId}", id);
            }
        }

        if (!isApproved)
        {
            TempData["Error"] = "পিডিএফ ডাউনলোড শুধুমাত্র একজন সনদপ্রাপ্ত আইনজীবীর অনুমোদনের পরই সম্ভব। / PDF download is available only after verified lawyer approval.";
            TempData["ErrorEn"] = "PDF download is available only after a verified lawyer approves this document.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        // A-2.5: real QuestPDF export via the approval-gated DocumentService path.
        // _documentService is optional purely for mock-mode compatibility.
        if (_documentService == null)
        {
            TempData["Error"] = "পিডিএফ পরিষেবা উপলব্ধ নেই। / PDF export service is unavailable.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        try
        {
            var pdf = await _documentService.GetPdfIfApprovedAsync(id);
            if (pdf == null || pdf.Length == 0)
            {
                TempData["Error"] = "পিডিএফ তৈরি করা যায়নি। / PDF could not be generated.";
                return RedirectToAction(nameof(Preview), new { id });
            }

            // Document type + id make a stable, meaningful filename; the type
            // name is ASCII so no UTF-8 Content-Disposition gymnastics needed.
            var fileName = $"MuktoAin-{id}-{DateTime.UtcNow:yyyyMMdd}.pdf";
            Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"{fileName}\"";
            return File(pdf, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF export failed for document {DocumentId}", id);
            TempData["Error"] = "পিডিএফ তৈরি করতে সমস্যা হয়েছে। / An error occurred while generating the PDF.";
            return RedirectToAction(nameof(Preview), new { id });
        }
    }

    private static DocumentPreviewViewModel GetMockDocument(int id)
    {
        return new DocumentPreviewViewModel
        {
            DocumentId = id,
            CaseId = 42,
            CaseTitle = "বকেয়া বেতন ও ভাতা পরিশোধের দাবি (৩ মাসের বকেয়া মজুরি)",
            DocumentType = "Labour Complaint / শ্রম অভিযোগ",
            ContentDraft = @"বরাবর,
কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর / শ্রম আদালত
ঢাকা, বাংলাদেশ।

বিষয়: বাংলাদেশ শ্রম আইন ২০০৬ এর ১২৩ ধারা মোতাবেক বকেয়া মজুরি ও ক্ষতিপূরণ আদায়ের আবেদন।

মহোদয়,
আমি নিম্নস্বাক্ষরকারী মো: রফিকুল ইসলাম, পিতা: মো: আব্দুল জলিল, আইডি নং: ইএমপি-৮৯৭৬, বিগত ২ বছর যাবত মেসার্স অ্যাপেক্স ফ্যাশনস লিমিটেড, প্লট-১৪, তেজগাঁও শিল্প এলাকা, ঢাকা-তে অপারেটর পদে কর্মরত আছি।

যথাবিহিত সম্মান প্রদর্শনপূর্বক নিবেদন এই যে, বিগত তিন মাস (জুন, জুলাই, আগস্ট ২০২৬) যাবত মালিকপক্ষ আমার এবং অন্যান্য শ্রমিকদের ন্যায্য মাসিক মজুরি (প্রতি মাসে ১৫,০০০/- টাকা হারে সর্বমোট ৪৫,০০০/- টাকা) পরিশোধ না করে নানা অজুহাতে কালক্ষেপণ করছে।

বাংলাদেশ শ্রম আইন ২০০৬ এর ১২৩ ধারা অনুযায়ী পরবর্তী মাসের ৭ কার্যদিবসের মধ্যে মজুরি পরিশোধ করার আইনগত বাধ্যবাধকতা রয়েছে। বারবার মৌখিক ও লিখিত অনুরোধ জানানো সত্ত্বেও কারখানা কর্তৃপক্ষ বকেয়া পরিশোধে ব্যর্থ হয়েছে।

এমতাবস্থায়, মহোদয়ের নিকট আকুল প্রার্থনা, উক্ত কারখানার বিরুদ্ধে তদন্তপূর্বক বকেয়া মজুরি ৪৫,০০০/- টাকা এবং ধারা ১২৪ মোতাবেক ক্ষতিপূরণ আদায়ের প্রয়োজনীয় আইনগত ব্যবস্থা গ্রহণে মর্জি হয়।

বিনীত,
মো: রফিকুল ইসলাম
ফোন: ০১৭১২-৩৪৫৬৭৮",
            ContentFinal = null,
            Status = "Draft",
            CanDownloadPdf = false,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
    }
}
