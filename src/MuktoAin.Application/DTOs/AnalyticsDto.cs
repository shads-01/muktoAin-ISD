namespace MuktoAin.Application.DTOs;

// Admin dashboard aggregates
public record AnalyticsSummaryDto(
    int TotalCases,
    int PendingReviews,
    int ApprovedDocuments,
    IReadOnlyList<CategoryCountDto> CasesByCategory,
    IReadOnlyList<DistrictCountDto> CasesByDistrict
);

public record CategoryCountDto(string CategoryName, int Count);

public record DistrictCountDto(string DistrictName, int Count);
