using Microsoft.AspNetCore.Mvc.Rendering;

namespace MuktoAin.Web.ViewModels;

/// <summary>
/// Admin User Management (FR-17). Mirrors the USER domain record the way
/// UserManagementService will expose it -- role/status as enum-name strings
/// so the view renders bilingual badges without a Domain dependency.
/// </summary>
public class AdminUserViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // "Citizen", "Lawyer", "Admin" (matches MuktoAin.Domain.Enums.UserRole names)
    public string Role { get; set; } = "Citizen";

    // "Active", "Suspended" (matches MuktoAin.Domain.Enums.AccountStatus names)
    public string AccountStatus { get; set; } = "Active";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Admin Lawyer Verification Triage (FR-17). Fields mirror LAWYER_PROFILE:
/// BarRegistrationNumber, Specialization, VerificationStatus.
/// </summary>
public class AdminLawyerViewModel
{
    public int LawyerProfileId { get; set; }
    public int UserId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;

    // "Pending", "Approved", "Rejected" (matches VerificationStatus enum names)
    public string VerificationStatus { get; set; } = "Pending";

    public string? Specialization { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Admin Acts Corpus Management (FR-18). SectionCount / ReindexedAt are derived
/// aggregates ActsManagementService will compute from ACT + ACT_SECTION + chunks.
/// </summary>
public class AdminActViewModel
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public int SectionCount { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public bool IsVectorIndexed { get; set; }
}

/// <summary>
/// Scenario Keyword Boost Mapping (FR-18). SectionId drives the linked
/// ActTitle + SectionNumber lookup that ScenarioMappingService will provide.
/// </summary>
public class AdminScenarioMappingViewModel
{
    public int MappingId { get; set; }
    public int SectionId { get; set; }
    public string ScenarioKeyword { get; set; } = string.Empty;
    public string ActTitle { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class AdminScenarioMappingsViewModel
{
    public List<AdminScenarioMappingViewModel> Mappings { get; set; } = new();
    public ScenarioMappingAddViewModel NewMapping { get; set; } = new();
    public List<SelectListItem> Sections { get; set; } = new();
}

public class ScenarioMappingAddViewModel
{
    public string ScenarioKeyword { get; set; } = string.Empty;
    public int SectionId { get; set; }
    public string? Notes { get; set; }
}