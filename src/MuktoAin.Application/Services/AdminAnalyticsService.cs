using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

/// <summary>
/// Service providing anonymized platform analytics and metrics (FR-16).
/// Aggregates case volume, review status, category distribution, and district spread
/// without exposing citizen PII.
/// </summary>
public class AdminAnalyticsService : IAdminAnalyticsService
{
    private readonly IRepository<Case> _caseRepo;
    private readonly IRepository<GeneratedDocument> _docRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<District> _districtRepo;

    public AdminAnalyticsService(
        IRepository<Case> caseRepo,
        IRepository<GeneratedDocument> docRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<District> districtRepo)
    {
        _caseRepo = caseRepo;
        _docRepo = docRepo;
        _categoryRepo = categoryRepo;
        _districtRepo = districtRepo;
    }

    /// <summary>
    /// Computes summary KPIs: total cases, pending reviews, approved documents,
    /// category breakdowns, and geographic district distribution.
    /// </summary>
    public async Task<AnalyticsSummaryDto> GetSummaryAsync()
    {
        var cases = (await _caseRepo.GetAllAsync()).ToList();
        var documents = (await _docRepo.GetAllAsync()).ToList();
        var categories = (await _categoryRepo.GetAllAsync()).ToList();
        var districts = (await _districtRepo.GetAllAsync()).ToList();

        var totalCases = cases.Count;
        var pendingReviews = documents.Count(d => d.Status == DocumentStatus.UnderReview);
        var approvedDocs = documents.Count(d => d.Status == DocumentStatus.Approved);

        var byCategory = categories
            .Select(cat => new CategoryCountDto(
                cat.Name,
                cases.Count(c => c.CategoryId == cat.CategoryId)))
            .OrderBy(c => c.CategoryName)
            .ToList();

        var byDistrict = districts
            .Select(d => new DistrictCountDto(
                d.Name,
                cases.Count(c => c.DistrictId == d.DistrictId)))
            .OrderByDescending(d => d.Count)
            .ThenBy(d => d.DistrictName)
            .ToList();

        return new AnalyticsSummaryDto(
            totalCases,
            pendingReviews,
            approvedDocs,
            byCategory,
            byDistrict);
    }
}
