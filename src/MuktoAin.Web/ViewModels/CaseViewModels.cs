using Microsoft.AspNetCore.Mvc.Rendering;

namespace MuktoAin.Web.ViewModels;

public class CaseSubmitViewModel
{
    public int CategoryId { get; set; }
    public byte DistrictId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
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
