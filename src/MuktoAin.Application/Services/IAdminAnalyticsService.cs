using MuktoAin.Application.DTOs;

namespace MuktoAin.Application.Services;

/// <summary>
/// Service interface for anonymized admin analytics and reporting metrics (FR-16).
/// </summary>
public interface IAdminAnalyticsService
{
    /// <summary>
    /// Computes platform summary KPIs, review counts, and category/district distributions.
    /// </summary>
    Task<AnalyticsSummaryDto> GetSummaryAsync();
}
