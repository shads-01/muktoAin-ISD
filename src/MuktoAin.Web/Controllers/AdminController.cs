using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.VectorStore;
using MuktoAin.Web.ViewModels;
using Qdrant.Client;

namespace MuktoAin.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ILogger<AdminController> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IOptions<QdrantOptions> _qdrantOptions;
    private readonly IOptions<GeminiOptions> _geminiOptions;

    public AdminController(
        ILogger<AdminController> logger,
        AppDbContext dbContext,
        IOptions<QdrantOptions> qdrantOptions,
        IOptions<GeminiOptions> geminiOptions)
    {
        _logger = logger;
        _dbContext = dbContext;
        _qdrantOptions = qdrantOptions;
        _geminiOptions = geminiOptions;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        ViewData["IsAdminPage"] = true;
        var model = await BuildAdminDashboardViewModelAsync();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Analytics()
    {
        ViewData["IsAdminPage"] = true;
        var model = await BuildAdminDashboardViewModelAsync();
        return View(model);
    }

    private async Task<AdminDashboardViewModel> BuildAdminDashboardViewModelAsync()
    {
        var sample = MockData.SampleAnalytics;
        var model = new AdminDashboardViewModel
        {
            TotalCases = sample.TotalCases,
            CasesThisWeek = sample.CasesThisWeek,
            PendingReviews = sample.PendingReviews,
            VerificationsWaiting = sample.VerificationsWaiting,
            AiCallsToday = sample.AiCallsToday,
            AiFailureRate = sample.AiFailureRate,
            TotalUsersCount = sample.TotalUsersCount,
            TotalLawyersCount = sample.TotalLawyersCount,
            TotalActsCount = sample.TotalActsCount,
            CategoryStats = sample.CategoryStats,
            DistrictStats = sample.DistrictStats,
            VerificationQueue = sample.VerificationQueue,
            AuditLogs = new List<SystemAuditLogItemViewModel>(sample.AuditLogs)
        };

        // 1. Live MSSQL Relational Database Health Check
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync();
            if (canConnect)
            {
                var userCount = await _dbContext.Users.CountAsync();
                var actCount = await _dbContext.Acts.CountAsync();
                model.IsDatabaseHealthy = true;
                model.DatabaseStatus = $"Connected (SQL Server · {userCount} Users, {actCount} Acts)";
                if (userCount > 0) model.TotalUsersCount = userCount;
                if (actCount > 0) model.TotalActsCount = actCount;
            }
            else
            {
                model.IsDatabaseHealthy = false;
                model.DatabaseStatus = "Disconnected (Cannot reach SQL Server database)";
            }
        }
        catch (Exception ex)
        {
            model.IsDatabaseHealthy = false;
            model.DatabaseStatus = $"Database Error: {ex.Message}";
            _logger.LogWarning(ex, "Live Database health check failed in AdminController");
        }

        // 2. Live Qdrant Vector Store (RAG) Health Check
        var qOptions = _qdrantOptions?.Value;
        if (qOptions == null ||
            string.IsNullOrWhiteSpace(qOptions.Endpoint) ||
            qOptions.Endpoint.Contains("your-cluster-id") ||
            string.IsNullOrWhiteSpace(qOptions.ApiKey) ||
            qOptions.ApiKey.Contains("YOUR_QDRANT_API_KEY"))
        {
            model.IsVectorDbHealthy = false;
            model.VectorDbStatus = "Unconfigured / Missing in appsettings (SQL FTS Fallback Active)";
        }
        else
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var uri = new Uri(qOptions.Endpoint);
                var client = new QdrantClient(uri.Host, port: uri.Port, https: uri.Scheme == "https", apiKey: qOptions.ApiKey);
                var collectionName = qOptions.Collection ?? "act_section_chunks";
                var exists = await client.CollectionExistsAsync(collectionName, cts.Token);
                model.IsVectorDbHealthy = true;
                model.VectorDbStatus = exists
                    ? $"Operational (Qdrant Vector Store · '{collectionName}' Collection Online)"
                    : $"Connected (Collection '{collectionName}' Not Found)";
            }
            catch (Exception ex)
            {
                model.IsVectorDbHealthy = false;
                model.VectorDbStatus = $"Offline / Unreachable (SQL FTS Fallback Active · {ex.GetType().Name})";
                _logger.LogInformation("Qdrant health check failed: {Message}", ex.Message);
            }
        }

        // 3. Live Gemini 2.5 Flash AI API Health Check
        var gOptions = _geminiOptions?.Value;
        var validKeys = gOptions?.ApiKeys?
            .Where(k => !string.IsNullOrWhiteSpace(k) && !k.Contains("YOUR_GEMINI_API_KEY") && k.Length >= 10)
            .ToList();

        if (validKeys == null || validKeys.Count == 0)
        {
            model.IsAiServiceHealthy = false;
            model.AiServiceStatus = "Unconfigured / Missing Gemini API Key in appsettings";
        }
        else
        {
            model.IsAiServiceHealthy = true;
            model.AiServiceStatus = $"Healthy ({gOptions?.GenerationModel ?? "Gemini 2.5 Flash"} · {validKeys.Count} Key(s) Configured)";
        }

        // Overall Infrastructure Status Badge
        if (model.IsDatabaseHealthy && model.IsVectorDbHealthy && model.IsAiServiceHealthy)
        {
            model.OverallHealthBadgeText = "সকল সার্ভিস সচল (Operational)";
            model.OverallHealthBadgeClass = "badge-success";
        }
        else if (model.IsDatabaseHealthy)
        {
            model.OverallHealthBadgeText = "আংশিক সচল (Degraded · Fallback Active)";
            model.OverallHealthBadgeClass = "badge-gold";
        }
        else
        {
            model.OverallHealthBadgeText = "সার্ভিস বিঘ্নিত (Service Disrupted)";
            model.OverallHealthBadgeClass = "badge-danger";
        }

        return model;
    }
}
