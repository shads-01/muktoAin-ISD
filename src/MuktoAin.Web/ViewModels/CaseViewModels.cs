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

    // Rights explanation
    public string RightsExplanation { get; set; } = string.Empty;
    public List<CitedSectionViewModel> CitedSections { get; set; } = new();

    // Document draft
    public int? DocumentId { get; set; }
    public string? DocumentContent { get; set; }
    public string? DocumentStatus { get; set; }
    public bool CanDownloadPdf { get; set; }
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
}

public class CaseListItemViewModel
{
    public int CaseId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
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
