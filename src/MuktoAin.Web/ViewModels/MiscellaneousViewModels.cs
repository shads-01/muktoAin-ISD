using System.ComponentModel.DataAnnotations;

namespace MuktoAin.Web.ViewModels;

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
    public int TotalResults { get; set; }
    public int? ActId { get; set; }
    // True once a search/browse has actually run -- distinct from Query being
    // non-empty, since selecting only the Act dropdown (no keyword) is a valid
    // search too and still needs to render the results panel.
    public bool HasSearched { get; set; }
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
    public string Accent { get; set; } = "gold";
    public List<string> CommonActions { get; set; } = new();
    public List<string> CommonActionsEn { get; set; } = new();
}

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
    public string Decision { get; set; } = "Approved"; // Approved, EditedApproved, Rejected

    // FR-14: review comments are mandatory; app-level cap (DB column is NVARCHAR(MAX)).
    [Required(ErrorMessage = "পর্যালোচনার মন্তব্য প্রয়োজন / Review comments are required")]
    [StringLength(4000, ErrorMessage = "সর্বোচ্চ ৪০০০ অক্ষর / Maximum 4000 characters")]
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
    
    // System Infrastructure & Capacity
    public int TotalUsersCount { get; set; } = 418;
    public int TotalLawyersCount { get; set; } = 34;
    public int TotalActsCount { get; set; } = 1484;
    public bool IsDatabaseHealthy { get; set; } = true;
    public bool IsVectorDbHealthy { get; set; } = true;
    public bool IsAiServiceHealthy { get; set; } = true;
    public string OverallHealthBadgeText { get; set; } = "সকল সার্ভিস সচল (Operational)";
    public string OverallHealthBadgeClass { get; set; } = "badge-success";
    public string DatabaseStatus { get; set; } = "Connected (Microsoft SQL Server)";
    public string VectorDbStatus { get; set; } = "Operational (Qdrant Vector Store · 1,484 Acts)";
    public string AiServiceStatus { get; set; } = "Healthy (Gemini 2.5 Flash API · Circuit Breaker Closed)";

    public List<CategoryStatViewModel> CategoryStats { get; set; } = new();
    public List<DistrictStatViewModel> DistrictStats { get; set; } = new();
    public List<LawyerApplicationViewModel> VerificationQueue { get; set; } = new();
    public List<SystemAuditLogItemViewModel> AuditLogs { get; set; } = new();
}

public class SystemAuditLogItemViewModel
{
    public string Timestamp { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Status { get; set; } = "Success"; // Success, Warning, Danger, Info
    public string Details { get; set; } = string.Empty;
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

public class LawyerStatusViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending / Approved / Rejected
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class LawyerQueueViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public string ActiveFilter { get; set; } = "All";
    public bool FieldFallback { get; set; } // "My field" had no usable specialization

    // AUD-8: pagination (mirrors LawyerHistoryViewModel)
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }

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
    public bool IsMine { get; set; }
    public int WaitingHours { get; set; }
    public bool CanOpen { get; set; }
}

public class LawyerHistoryViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string ActiveFilter { get; set; } = "All";
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string Sort { get; set; } = "date_desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public List<LawyerHistoryItemViewModel> Items { get; set; } = new();
}

public class LawyerHistoryItemViewModel
{
    public int ReviewId { get; set; }
    public int DocumentId { get; set; }
    public int CaseId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string DistrictName { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Comments { get; set; } = string.Empty;
    public DateTime ReviewedAt { get; set; }
    public int VersionNo { get; set; }
    public string DocumentText { get; set; } = string.Empty;
}

public class LawyerPaymentsViewModel
{
    public string LawyerName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public List<EarningRowViewModel> History { get; set; } = new();
}
