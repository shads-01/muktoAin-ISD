namespace MuktoAin.Web.ViewModels;

public class AdminUsersViewModel
{
    public List<AdminUserRowViewModel> Users { get; set; } = new();
    public string RoleFilter { get; set; } = "All";
}

public class AdminUserRowViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AdminLawyersViewModel
{
    public List<AdminLawyerRowViewModel> Pending { get; set; } = new();
    public List<AdminLawyerRowViewModel> Approved { get; set; } = new();
    public List<AdminLawyerRowViewModel> Rejected { get; set; } = new();
}

public class AdminLawyerRowViewModel
{
    public int LawyerProfileId { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string BarRegistrationNumber { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

public class AdminCorpusViewModel
{
    public List<AdminActRowViewModel> Acts { get; set; } = new();
    public int TotalSections { get; set; }
    public int TotalChunks { get; set; }
    public int EmbeddedChunks { get; set; }
}

public class AdminActRowViewModel
{
    public int ActId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ActNumber { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Language { get; set; } = string.Empty;
    public bool IsRepealed { get; set; }
    public int SectionCount { get; set; }
    public int ChunkCount { get; set; }
    public int EmbeddedCount { get; set; }
    public DateTime ImportedAt { get; set; }
}

public class AdminScenariosViewModel
{
    public List<AdminScenarioRowViewModel> Mappings { get; set; } = new();
}

public class AdminScenarioRowViewModel
{
    public int MappingId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public string ActTitle { get; set; } = string.Empty;
    public string SectionNumber { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class AdminCategoriesViewModel
{
    public List<AdminCategoryRowViewModel> Categories { get; set; } = new();
}

public class AdminCategoryRowViewModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameBn { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplateBadge { get; set; } = string.Empty;
}

public class AdminAiLogsViewModel
{
    public List<AdminAiLogRowViewModel> Logs { get; set; } = new();
    public int CallsToday { get; set; }
    public double FailureRateToday { get; set; }
}

public class AdminAiLogRowViewModel
{
    public long LogId { get; set; }
    public string Time { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Tokens { get; set; }
    public int LatencyMs { get; set; }
    public int? CaseId { get; set; }
    public string PromptPreview { get; set; } = string.Empty;
    public string ResponsePreview { get; set; } = string.Empty;
}
