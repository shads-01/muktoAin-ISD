using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class AdminAnalyticsServiceTests
{
    private readonly Mock<IRepository<Case>> _caseRepoMock = new();
    private readonly Mock<IRepository<GeneratedDocument>> _docRepoMock = new();
    private readonly Mock<IRepository<CaseCategory>> _categoryRepoMock = new();
    private readonly Mock<IRepository<District>> _districtRepoMock = new();
    private readonly AdminAnalyticsService _service;

    public AdminAnalyticsServiceTests()
    {
        _service = new AdminAnalyticsService(
            _caseRepoMock.Object,
            _docRepoMock.Object,
            _categoryRepoMock.Object,
            _districtRepoMock.Object);
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyRepositories_ReturnsZeroCounts()
    {
        _caseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<Case>());
        _docRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<GeneratedDocument>());
        _categoryRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<CaseCategory>());
        _districtRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<District>());

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalCases);
        Assert.Equal(0, summary.PendingReviews);
        Assert.Equal(0, summary.ApprovedDocuments);
        Assert.Empty(summary.CasesByCategory);
        Assert.Empty(summary.CasesByDistrict);
    }

    [Fact]
    public async Task GetSummaryAsync_PopulatedData_ReturnsCorrectAggregates()
    {
        var categories = new List<CaseCategory>
        {
            new() { CategoryId = 1, Name = "Labour Complaint" },
            new() { CategoryId = 2, Name = "General Diary" },
            new() { CategoryId = 3, Name = "RTI Request" }
        };

        var districts = new List<District>
        {
            new() { DistrictId = 1, Name = "Dhaka" },
            new() { DistrictId = 2, Name = "Chattogram" },
            new() { DistrictId = 3, Name = "Sylhet" }
        };

        var cases = new List<Case>
        {
            new() { CaseId = 1, CategoryId = 1, DistrictId = 1 },
            new() { CaseId = 2, CategoryId = 1, DistrictId = 1 },
            new() { CaseId = 3, CategoryId = 2, DistrictId = 1 },
            new() { CaseId = 4, CategoryId = 2, DistrictId = 2 },
            new() { CaseId = 5, CategoryId = 3, DistrictId = 3 }
        };

        var documents = new List<GeneratedDocument>
        {
            new() { DocumentId = 1, Status = DocumentStatus.Draft },
            new() { DocumentId = 2, Status = DocumentStatus.UnderReview },
            new() { DocumentId = 3, Status = DocumentStatus.UnderReview },
            new() { DocumentId = 4, Status = DocumentStatus.Approved },
            new() { DocumentId = 5, Status = DocumentStatus.Rejected }
        };

        _caseRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(cases);
        _docRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(documents);
        _categoryRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(categories);
        _districtRepoMock.Setup(r => r.GetAllAsync()).ReturnsAsync(districts);

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(5, summary.TotalCases);
        Assert.Equal(2, summary.PendingReviews);
        Assert.Equal(1, summary.ApprovedDocuments);

        // Category breakdown
        Assert.Equal(3, summary.CasesByCategory.Count);
        var labourCount = summary.CasesByCategory.First(c => c.CategoryName == "Labour Complaint");
        var gdCount = summary.CasesByCategory.First(c => c.CategoryName == "General Diary");
        var rtiCount = summary.CasesByCategory.First(c => c.CategoryName == "RTI Request");
        Assert.Equal(2, labourCount.Count);
        Assert.Equal(2, gdCount.Count);
        Assert.Equal(1, rtiCount.Count);

        // District breakdown
        var dhakaCount = summary.CasesByDistrict.First(d => d.DistrictName == "Dhaka");
        var ctgCount = summary.CasesByDistrict.First(d => d.DistrictName == "Chattogram");
        var sylhetCount = summary.CasesByDistrict.First(d => d.DistrictName == "Sylhet");
        Assert.Equal(3, dhakaCount.Count);
        Assert.Equal(1, ctgCount.Count);
        Assert.Equal(1, sylhetCount.Count);
    }
}
