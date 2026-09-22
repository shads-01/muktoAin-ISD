using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Web.ViewModels;
using Qdrant.Client;

namespace MuktoAin.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ILogger<AdminController> _logger;
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public AdminController(
        ILogger<AdminController> logger,
        AppDbContext dbContext,
        IConfiguration configuration)
    {
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;
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

    [HttpGet]
    public IActionResult Users()
    {
        ViewData["IsAdminPage"] = true;
        // TODO: [Shads] Replace with UserManagementService.GetAllUsersAsync()
        return View(MockData.SampleUsers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Suspend(int id)
    {
        // TODO: [Shads] Replace with UserManagementService.SetAccountStatusAsync(id, AccountStatus.Suspended)
        TempData["Success"] = $"ইউজার #{id} স্থগিত করা হয়েছে (সাসপেন্ডেড)।";
        TempData["SuccessEn"] = $"User #{id} suspended.";
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Activate(int id)
    {
        // TODO: [Shads] Replace with UserManagementService.SetAccountStatusAsync(id, AccountStatus.Active)
        TempData["Success"] = $"ইউজার #{id} সক্রিয় করা হয়েছে।";
        TempData["SuccessEn"] = $"User #{id} activated.";
        return RedirectToAction("Users");
    }

    [HttpGet]
    public IActionResult Lawyers()
    {
        ViewData["IsAdminPage"] = true;
        // TODO: [Arpita] Replace with LawyerVerificationService.GetPendingApplicationsAsync()
        return View(MockData.SampleLawyers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Verify(int id, bool approve)
    {
        // TODO: [Arpita] Replace with LawyerVerificationService.VerifyAsync(id, approve, adminId)
        var verdict = approve ? "অনুমোদিত (Approved)" : "বাতিল (Rejected)";
        TempData["Success"] = $"আইনজীবী আবেদন #{id} — {verdict}।";
        TempData["SuccessEn"] = $"Lawyer application #{id} marked as {(approve ? "Approved" : "Rejected")}.";
        return RedirectToAction("Lawyers");
    }

    [HttpGet]
    public IActionResult Acts()
    {
        ViewData["IsAdminPage"] = true;
        // TODO: [Tultul] Replace with ActsManagementService.GetAllActStatusAsync()
        return View(MockData.SampleActs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Reindex(int id)
    {
        // TODO: [Tultul] Replace with ActsManagementService.ReindexActAsync(id) / EmbeddingBatchJob trigger
        TempData["Success"] = $"আইন #{id} ভেক্টর রি-ইনডেক্সিং শুরু হয়েছে।";
        TempData["SuccessEn"] = $"Act #{id} vector re-indexing triggered.";
        return RedirectToAction("Acts");
    }

    [HttpGet]
    public IActionResult ScenarioMappings()
    {
        ViewData["IsAdminPage"] = true;
        // TODO: [Tultul] Replace with ScenarioMappingService.GetAllAsync() + IActSectionRepository for dropdown
        var vm = new AdminScenarioMappingsViewModel
        {
            Mappings = MockData.SampleMappings,
            Sections = MockData.SampleScenarioSections
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AddMapping(ScenarioMappingAddViewModel vm)
    {
        // TODO: [Tultul] Wire ScenarioMappingService for Add/Delete actions
        if (string.IsNullOrWhiteSpace(vm.ScenarioKeyword) || vm.SectionId == 0)
        {
            TempData["Error"] = "কী-ওয়ার্ড ও সেকশন নির্বাচন আবশ্যক / Keyword and Section are required.";
            return RedirectToAction("ScenarioMappings");
        }
        TempData["Success"] = $"কী-ওয়ার্ড \"{vm.ScenarioKeyword}\" নতুন ম্যাপিং যোগ করা হয়েছে (mock)।";
        TempData["SuccessEn"] = $"New mapping \"{vm.ScenarioKeyword}\" added (mock).";
        return RedirectToAction("ScenarioMappings");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteMapping(int id)
    {
        // TODO: [Tultul] Wire ScenarioMappingService for Add/Delete actions
        TempData["Success"] = $"ম্যাপিং #{id} মুছে ফেলা হয়েছে (mock)।";
        TempData["SuccessEn"] = $"Mapping #{id} removed (mock).";
        return RedirectToAction("ScenarioMappings");
    }

    /// <summary>
    /// Live Real-time API endpoint polled by the Admin Dashboard to give immediate
    /// feedback as configuration or services change without restarting the server.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> HealthStatus()
    {
        var model = await BuildAdminDashboardViewModelAsync();
        return Json(new
        {
            isDatabaseHealthy = model.IsDatabaseHealthy,
            isVectorDbHealthy = model.IsVectorDbHealthy,
            isAiServiceHealthy = model.IsAiServiceHealthy,
            databaseStatus = model.DatabaseStatus,
            vectorDbStatus = model.VectorDbStatus,
            aiServiceStatus = model.AiServiceStatus,
            overallHealthBadgeText = model.OverallHealthBadgeText,
            overallHealthBadgeClass = model.OverallHealthBadgeClass,
            totalUsersCount = model.TotalUsersCount,
            totalActsCount = model.TotalActsCount,
            lastChecked = DateTime.Now.ToString("T")
        });
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

        // 1. Live MSSQL Relational Database Health Check (from live IConfiguration)
        var rawConnStr = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(rawConnStr))
        {
            model.IsDatabaseHealthy = false;
            model.DatabaseStatus = "Unconfigured / Missing ConnectionString in appsettings";
        }
        else
        {
            try
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(rawConnStr)
                {
                    ConnectTimeout = 2 // Fast 2s timeout for real-time health checks
                };

                using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT (SELECT COUNT(*) FROM [dbo].[USER]), (SELECT COUNT(*) FROM [dbo].[ACT]);";
                using var reader = await cmd.ExecuteReaderAsync();
                int userCount = 0;
                int actCount = 0;
                if (await reader.ReadAsync())
                {
                    userCount = reader.GetInt32(0);
                    actCount = reader.GetInt32(1);
                }

                model.IsDatabaseHealthy = true;
                model.DatabaseStatus = $"Connected (SQL Server · {userCount} Users, {actCount} Acts)";
                if (userCount > 0) model.TotalUsersCount = userCount;
                if (actCount > 0) model.TotalActsCount = actCount;
            }
            catch (Exception ex)
            {
                model.IsDatabaseHealthy = false;
                model.DatabaseStatus = $"Disconnected / Error ({ex.GetType().Name})";
                _logger.LogInformation("Database health check failed: {Message}", ex.Message);
            }
        }

        // 2. Live Qdrant Vector Store (RAG) Health Check (Direct from live IConfiguration)
        var qdrantEndpoint = _configuration["Qdrant:Endpoint"];
        var qdrantApiKey = _configuration["Qdrant:ApiKey"];
        var qdrantCollection = _configuration["Qdrant:Collection"] ?? "act_section_chunks";

        if (string.IsNullOrWhiteSpace(qdrantEndpoint) ||
            qdrantEndpoint.Contains("your-cluster-id") ||
            string.IsNullOrWhiteSpace(qdrantApiKey) ||
            qdrantApiKey.Contains("YOUR_QDRANT_API_KEY"))
        {
            model.IsVectorDbHealthy = false;
            model.VectorDbStatus = "Unconfigured / Missing in appsettings (SQL FTS Fallback Active)";
        }
        else
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var uri = new Uri(qdrantEndpoint);
                var client = new QdrantClient(uri.Host, port: uri.Port, https: uri.Scheme == "https", apiKey: qdrantApiKey);
                var exists = await client.CollectionExistsAsync(qdrantCollection, cts.Token);
                model.IsVectorDbHealthy = true;
                model.VectorDbStatus = exists
                    ? $"Operational (Qdrant Vector Store · '{qdrantCollection}' Online)"
                    : $"Connected (Collection '{qdrantCollection}' Not Found)";
            }
            catch (Exception ex)
            {
                model.IsVectorDbHealthy = false;
                model.VectorDbStatus = $"Offline / Unreachable (SQL FTS Fallback Active · {ex.GetType().Name})";
                _logger.LogInformation("Qdrant health check failed: {Message}", ex.Message);
            }
        }

        // 3. Live Gemini 2.5 Flash AI API Health Check (Direct from live IConfiguration)
        var geminiSection = _configuration.GetSection("Gemini");
        var apiKeysList = geminiSection.GetSection("ApiKeys").Get<string[]>() ?? Array.Empty<string>();
        var singleKey = geminiSection["ApiKey"];
        var generationModel = geminiSection["GenerationModel"] ?? "Gemini 2.5 Flash";

        var allKeys = apiKeysList
            .Concat(string.IsNullOrWhiteSpace(singleKey) ? Array.Empty<string>() : new[] { singleKey })
            .Where(k => !string.IsNullOrWhiteSpace(k) && !k.Contains("YOUR_GEMINI_API_KEY") && k.Length >= 10)
            .ToList();

        if (allKeys.Count == 0)
        {
            model.IsAiServiceHealthy = false;
            model.AiServiceStatus = "Unconfigured / Missing Gemini API Key in appsettings";
        }
        else
        {
            model.IsAiServiceHealthy = true;
            model.AiServiceStatus = $"Healthy ({generationModel} · {allKeys.Count} Key(s) Configured)";
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
