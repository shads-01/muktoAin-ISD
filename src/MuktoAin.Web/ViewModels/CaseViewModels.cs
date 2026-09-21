using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MuktoAin.Web.ViewModels;

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

public class CaseResultViewModel
{
    public int CaseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Submitted";
    public string CategoryName { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string TrackingCode { get; set; } = string.Empty;

    // Rights explanation
    public string RightsExplanation { get; set; } = string.Empty;
    public List<CitedSectionViewModel> CitedSections { get; set; } = new();

    // Document draft (embedded paper, version chain)
    public int? DocumentId { get; set; }
    public string? DocumentContent { get; set; }
    public string? ContentFinal { get; set; }
    public string? DocumentStatus { get; set; }
    public bool CanDownloadPdf { get; set; }
    public int VersionNo { get; set; } = 1;
    public bool CitizenEdited { get; set; }
    public bool CanEdit { get; set; }

    // Timeline (real, status-driven)
    public string TimelineCurrent { get; set; } = "DraftReady";

    // Lawyer block
    public string? LawyerName { get; set; }
    public string? LawyerBarNumber { get; set; }
    public string? LawyerDecision { get; set; }
    public string? LawyerComments { get; set; }
    public string? RejectionReason { get; set; }
    public bool HonorariumPaid { get; set; }
}

public class CitedSectionViewModel
{
    public string ActTitle { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string SectionText { get; set; } = string.Empty;
    public string RelevanceScore { get; set; } = string.Empty;
}

public class CaseTrackViewModel
{
    public List<CaseListItemViewModel> Cases { get; set; } = new();
    public string ActiveStatusFilter { get; set; } = "All";
    public string LookupCode { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount { get; set; }
}

public class CaseListItemViewModel
{
    public int CaseId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool HasUnread { get; set; }
}

public class CaseDetailViewModel
{
    public int CaseId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public string Status { get; set; } = "Submitted";
    public DateTime CreatedAt { get; set; }
}
