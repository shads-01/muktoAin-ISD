namespace MuktoAin.Web.ViewModels;

public class SearchViewModel
{
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalResults { get; set; }
    public List<SearchResultItemViewModel> Results { get; set; } = new();
}

public class SearchResultItemViewModel
{
    public int SectionId { get; set; }
    public string ActTitle { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    public string SectionTextSnippet { get; set; } = string.Empty;
    public string SectionTextFull { get; set; } = string.Empty;
    public string ActNumber { get; set; } = string.Empty;
    public int ActYear { get; set; }
    public bool IsTruncated { get; set; }
    public string SectionIntro { get; set; } = string.Empty;
    public List<string> SectionClauses { get; set; } = new();
}

public class CategoryViewModel
{
    public int CategoryId { get; set; }
    public string NameBn { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionBn { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string Icon { get; set; } = "folder";
    public List<string> CommonActions { get; set; } = new();
    public List<string> CommonActionsEn { get; set; } = new();
}

public class LawyerApplyViewModel
{
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string? Specialization { get; set; }
    public string? ChamberAddress { get; set; }
}

public class LawyerReviewViewModel
{
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ContentDraft { get; set; } = string.Empty;
    public string? EditedContent { get; set; }
    public string Decision { get; set; } = "Approved"; // Approved, EditedApproved, Rejected
    public string Comments { get; set; } = string.Empty;
}

public class AdminDashboardViewModel
{
    public int TotalCases { get; set; }
    public int CasesThisWeek { get; set; }
    public int PendingReviews { get; set; }
    public int VerificationsWaiting { get; set; }
    public int AiCallsToday { get; set; }
    public double AiFailureRate { get; set; }
    public List<CategoryStatViewModel> CategoryStats { get; set; } = new();
    public List<DistrictStatViewModel> DistrictStats { get; set; } = new();
    public List<LawyerApplicationViewModel> VerificationQueue { get; set; } = new();
}

public class CategoryStatViewModel
{
    public string Name { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string ColorClass { get; set; } = string.Empty;
}

public class DistrictStatViewModel
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Percentage { get; set; }
}

public class LawyerApplicationViewModel
{
    public int ApplicationId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string BarRegNo { get; set; } = string.Empty;
    public string AppliedDate { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

public class DocumentPreviewViewModel
{
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string ContentDraft { get; set; } = string.Empty;
    public string? ContentFinal { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, UnderReview, Approved, EditedApproved, Rejected
    public bool CanDownloadPdf { get; set; }
    public string? LawyerComments { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
