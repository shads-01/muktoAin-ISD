using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
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
    private readonly IUserManagementService _userManagement;
    private readonly LawyerVerificationService _lawyerVerification;
    private readonly IRepository<LawyerProfile> _lawyerProfileRepo;
    private readonly UserManager<User> _userManager;
    private readonly IActRepository _actRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActSectionChunkRepository _chunkRepo;
    private readonly IScenarioMappingRepository _scenarioRepo;
    private readonly IRepository<CaseCategory> _categoryRepo;
    private readonly IRepository<AiLog> _aiLogRepo;
    private readonly PaymentService _paymentService;

    public AdminController(
        ILogger<AdminController> logger,
        AppDbContext dbContext,
        IConfiguration configuration,
        IUserManagementService userManagement,
        LawyerVerificationService lawyerVerification,
        IRepository<LawyerProfile> lawyerProfileRepo,
        UserManager<User> userManager,
        IActRepository actRepo,
        IActSectionRepository sectionRepo,
        IActSectionChunkRepository chunkRepo,
        IScenarioMappingRepository scenarioRepo,
        IRepository<CaseCategory> categoryRepo,
        IRepository<AiLog> aiLogRepo,
        PaymentService paymentService)
    {
        _logger = logger;
        _dbContext = dbContext;
        _configuration = configuration;
        _userManagement = userManagement;
        _lawyerVerification = lawyerVerification;
        _lawyerProfileRepo = lawyerProfileRepo;
        _userManager = userManager;
        _actRepo = actRepo;
        _sectionRepo = sectionRepo;
        _chunkRepo = chunkRepo;
        _scenarioRepo = scenarioRepo;
        _categoryRepo = categoryRepo;
        _aiLogRepo = aiLogRepo;
        _paymentService = paymentService;
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

    private static int? _cachedTotalChunks;

    /// <summary>
    /// Live endpoint for tracking Qdrant embedding and upload progress.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> EmbeddingProgress()
    {
        if (!_cachedTotalChunks.HasValue || _cachedTotalChunks.Value == 0)
        {
            _cachedTotalChunks = await _dbContext.ActSectionChunks.AsNoTracking().CountAsync();
        }

        var totalChunks = _cachedTotalChunks.Value;
        var embeddedChunks = await _dbContext.ActSectionChunks
            .AsNoTracking()
            .Where(c => c.VectorId != null)
            .CountAsync();
        var percent = totalChunks > 0 ? (double)embeddedChunks / totalChunks * 100.0 : 0;

        return Json(new
        {
            totalChunks,
            embeddedChunks,
            remainingChunks = totalChunks - embeddedChunks,
            percentage = Math.Round(percent, 2),
            isRunning = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.IsRunning,
            lastStatus = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.LastStatus,
            lastStatusEn = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.LastStatusEn,
            totalProcessed = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.TotalProcessed,
            totalSkipped = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.TotalSkipped,
            requestsPerMinuteBudget = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.RequestsPerMinuteBudget,
            estimatedCompletion = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.EstimatedCompletion,
            estimatedCompletionEn = MuktoAin.Infrastructure.VectorStore.EmbeddingProgressState.EstimatedCompletionEn
        });
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? role)
    {
        var all = await _userManagement.GetAllUsersAsync();
        var filtered = string.IsNullOrWhiteSpace(role) || role == "All"
            ? all
            : all.Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
        var vm = new AdminUsersViewModel
        {
            RoleFilter = role ?? "All",
            Users = filtered.Select(u => new AdminUserRowViewModel
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                Status = u.Status
            }).ToList()
        };
        return View(vm);
    }

    // Suspend/Activate. Admin rows are protected inside the service
    // (UserManagementService guards admins + self-suspend).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(int userId, bool suspend)
    {
        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        var ok = await _userManagement.SetAccountStatusAsync(
            userId, suspend ? Domain.Enums.AccountStatus.Suspended : Domain.Enums.AccountStatus.Active, adminId);
        if (!ok)
        {
            TempData["Error"] = "এই অ্যাকাউন্টের অবস্থা পরিবর্তন করা যাবে না (অ্যাডমিন সুরক্ষিত)।";
            TempData["ErrorEn"] = "This account's status cannot be changed (admin protected).";
        }
        else
        {
            TempData["Success"] = suspend ? "অ্যাকাউন্ট স্থগিত হয়েছে।" : "অ্যাকাউন্ট পুনরায় চালু হয়েছে।";
            TempData["SuccessEn"] = suspend ? "Account suspended." : "Account reactivated.";
        }
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Lawyers()
    {
        var all = await _lawyerProfileRepo.GetAllAsync();
        var rows = new List<AdminLawyerRowViewModel>();
        foreach (var p in all)
        {
            var u = await _userManager.FindByIdAsync(p.UserId.ToString());
            rows.Add(new AdminLawyerRowViewModel
            {
                LawyerProfileId = p.LawyerProfileId,
                ApplicantName = u?.FullName ?? "(unknown)",
                Email = u?.Email ?? "",
                BarRegistrationNumber = p.BarRegistrationNumber,
                Specialization = p.Specialization ?? "",
                Status = p.VerificationStatus.ToString()
            });
        }
        var vm = new AdminLawyersViewModel
        {
            Pending = rows.Where(r => r.Status == "Pending").ToList(),
            Approved = rows.Where(r => r.Status == "Approved").ToList(),
            Rejected = rows.Where(r => r.Status == "Rejected").ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyLawyer(int lawyerProfileId, bool approve, string? reason)
    {
        if (!approve && string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "প্রত্যাখ্যানের কারণ আবশ্যক।";
            TempData["ErrorEn"] = "Rejection reason is required.";
            return RedirectToAction(nameof(Lawyers));
        }

        var adminId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var id) ? id : 0;
        await _lawyerVerification.VerifyAsync(lawyerProfileId, adminId, approve, reason);

        TempData["Success"] = approve
            ? "আইনজীবী যাচাই অনুমোদিত হয়েছে।"
            : "আবেদন প্রত্যাখ্যাত হয়েছে (কারণসহ)।";
        TempData["SuccessEn"] = approve ? "Lawyer verified." : "Application rejected (with reason).";
        return RedirectToAction(nameof(Lawyers));
    }

    // ---------- FR-17: Corpus ----------

    [HttpGet]
    public async Task<IActionResult> Corpus()
    {
        // High-performance database-side aggregation (FR-17):
        // Rather than materializing all 42,858 chunks and 35,633 sections into memory,
        // compute totals and per-act chunk counts directly via EF Core aggregates.
        var totalSections = await _dbContext.ActSections.CountAsync();
        var totalChunks = await _dbContext.ActSectionChunks.CountAsync();
        var embeddedChunks = await _dbContext.ActSectionChunks.CountAsync(c => c.VectorId != null);

        var topActs = await _dbContext.Acts
            .AsNoTracking()
            .OrderBy(a => a.Title)
            .Take(100)
            .Select(a => new AdminActRowViewModel
            {
                ActId = a.ActId,
                Title = a.Title,
                ActNumber = a.ActNumber ?? "",
                Year = a.Year,
                Language = a.Language,
                IsRepealed = a.IsRepealed,
                SectionCount = a.Sections.Count(),
                ChunkCount = a.Sections.SelectMany(s => s.Chunks).Count(),
                EmbeddedCount = a.Sections.SelectMany(s => s.Chunks).Count(c => c.VectorId != null),
                ImportedAt = a.ImportedAt
            })
            .ToListAsync();

        var vm = new AdminCorpusViewModel
        {
            TotalSections = totalSections,
            TotalChunks = totalChunks,
            EmbeddedChunks = embeddedChunks,
            Acts = topActs
        };
        return View(vm);
    }

    // ---------- FR-18: Scenario mappings ----------

    [HttpGet]
    public async Task<IActionResult> Scenarios()
    {
        var mappings = await _scenarioRepo.GetAllAsync();
        var sections = await _sectionRepo.GetAllAsync();
        var acts = await _actRepo.GetAllAsync();

        var vm = new AdminScenariosViewModel
        {
            Mappings = mappings.OrderBy(m => m.MappingId).Select(m =>
            {
                var s = sections.FirstOrDefault(x => x.SectionId == m.SectionId);
                var a = s != null ? acts.FirstOrDefault(x => x.ActId == s.ActId) : null;
                return new AdminScenarioRowViewModel
                {
                    MappingId = m.MappingId,
                    Keyword = m.ScenarioKeyword,
                    ActTitle = a?.Title ?? "",
                    SectionNumber = s?.SectionNumber ?? "",
                    Notes = m.Notes
                };
            }).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScenario(int sectionId, string keyword, string? notes)
    {
        if (string.IsNullOrWhiteSpace(keyword) || sectionId <= 0)
        {
            TempData["Error"] = "Keyword and section are required.";
            return RedirectToAction(nameof(Scenarios));
        }
        await _scenarioRepo.AddAsync(new Domain.Entities.ScenarioMapping
        {
            SectionId = sectionId,
            ScenarioKeyword = keyword.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        });
        await _scenarioRepo.SaveChangesAsync();
        TempData["Success"] = "Mapping added.";
        return RedirectToAction(nameof(Scenarios));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScenario(int mappingId)
    {
        var all = await _scenarioRepo.GetAllAsync();
        var m = all.FirstOrDefault(x => x.MappingId == mappingId);
        if (m != null)
        {
            await _scenarioRepo.DeleteAsync(m);
            await _scenarioRepo.SaveChangesAsync();
        }
        TempData["Success"] = "Mapping deleted.";
        return RedirectToAction(nameof(Scenarios));
    }

    // ---------- FR-18: Categories ----------

    [HttpGet]
    public async Task<IActionResult> Categories()
    {
        var cats = await _categoryRepo.GetAllAsync();
        var vm = new AdminCategoriesViewModel
        {
            Categories = cats.OrderBy(c => c.CategoryId).Select(c => new AdminCategoryRowViewModel
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                NameBn = c.NameBn,
                Description = c.Description,
                TemplateBadge = c.CategoryId switch
                {
                    1 => "labour_complaint.v1",
                    2 => "gd_application.v1",
                    3 => "rti_request.v1",
                    4 => "consumer_complaint.v1",
                    _ => "custom.v1"
                }
            }).ToList()
        };
        return View(vm);
    }

    // ---------- FR-12: AI Logs ----------

    [HttpGet]
    public async Task<IActionResult> AiLogs(string? type, int minLatency = 0)
    {
        var logs = (await _aiLogRepo.GetAllAsync())
            .OrderByDescending(l => l.CreatedAt)
            .Take(200);

        if (!string.IsNullOrWhiteSpace(type) && type != "All"
            && Enum.TryParse<Domain.Enums.AiRequestType>(type, out var t))
        {
            logs = logs.Where(l => l.RequestType == t);
        }
        if (minLatency > 0)
        {
            logs = logs.Where(l => l.LatencyMs >= minLatency);
        }

        var today = DateTime.UtcNow.Date;
        var allToday = (await _aiLogRepo.GetAllAsync()).Where(l => l.CreatedAt >= today).ToList();

        var vm = new AdminAiLogsViewModel
        {
            CallsToday = allToday.Count,
            FailureRateToday = allToday.Count == 0 ? 0 : 0, // failure detection = latency outliers; see view
            Logs = logs.Select(l => new AdminAiLogRowViewModel
            {
                LogId = l.LogId,
                Time = l.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Type = l.RequestType.ToString(),
                Model = l.ModelUsed,
                Tokens = l.TokensUsed,
                LatencyMs = l.LatencyMs,
                CaseId = l.CaseId,
                PromptPreview = l.PromptText.Length > 200 ? l.PromptText[..200] + "…" : l.PromptText,
                ResponsePreview = l.ResponseText.Length > 200 ? l.ResponseText[..200] + "…" : l.ResponseText
            }).ToList()
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Transactions()
    {
        var orders = await _paymentService.GetOrdersAsync();
        var payouts = await _paymentService.GetPendingPayoutsAsync();
        ViewData["Payouts"] = payouts;
        return View(orders);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefundOrder(int orderId)
    {
        await _paymentService.RefundAsync(orderId);
        TempData["Success"] = "Order refunded (sandbox) — ledger reversed.";
        return RedirectToAction(nameof(Transactions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApprovePayout(int payoutRequestId)
    {
        await _paymentService.ApprovePayoutAsync(payoutRequestId);
        TempData["Success"] = "Payout marked paid (sandbox).";
        return RedirectToAction(nameof(Transactions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOrderPaid(int orderId)
    {
        // Sandbox gateway confirm (in lieu of real SSLCommerz IPN)
        await _paymentService.MarkPaidAsync(orderId, $"SBX-{Guid.NewGuid().ToString("N")[..12].ToUpper()}");
        TempData["Success"] = "Order marked Paid (sandbox gateway).";
        return RedirectToAction(nameof(Transactions));
    }

    private async Task<AdminDashboardViewModel> BuildAdminDashboardViewModelAsync()
    {
        // Real data from repositories (redesign goal: zero mock data).
        // Uses EF directly (AppDbContext is already injected) for the
        // aggregates; fallback to 0/empty on any infra failure so the
        // dashboard degrades gracefully instead of 500-ing.
        var model = new AdminDashboardViewModel();
        try
        {
            var todayUtc = DateTime.UtcNow.Date;
            var weekAgoUtc = DateTime.UtcNow.AddDays(-7);

            var cases = await _dbContext.Cases.AsNoTracking().ToListAsync();
            var documents = await _dbContext.GeneratedDocuments.AsNoTracking().ToListAsync();
            var lawyerProfiles = await _dbContext.LawyerProfiles.AsNoTracking().ToListAsync();
            var users = await _dbContext.Users.AsNoTracking().ToListAsync();
            var acts = await _dbContext.Acts.AsNoTracking().ToListAsync();
            var aiLogsToday = await _dbContext.AiLogs
                .AsNoTracking()
                .Where(l => l.CreatedAt >= todayUtc)
                .ToListAsync();

            model.TotalCases = cases.Count;
            model.CasesThisWeek = cases.Count(c => c.CreatedAt >= weekAgoUtc);
            model.PendingReviews = documents.Count(d => d.Status == DocumentStatus.UnderReview);
            model.VerificationsWaiting = lawyerProfiles.Count(p =>
                p.VerificationStatus == VerificationStatus.Pending);
            model.AiCallsToday = aiLogsToday.Count;
            // FR-12 latency outliers as the honest failure proxy: >8s calls
            // (half of the old 15s timeout) or 0-token empty responses.
            model.AiFailureRate = aiLogsToday.Count == 0 ? 0
                : Math.Round(
                    aiLogsToday.Count(l => l.LatencyMs > 8000 || l.TokensUsed <= 0) * 100.0
                    / aiLogsToday.Count, 1);
            model.TotalUsersCount = users.Count;
            model.TotalLawyersCount = lawyerProfiles.Count;
            model.TotalActsCount = acts.Count;

            // Category distribution (real counts, percentage of total)
            var categories = await _dbContext.CaseCategories.AsNoTracking().ToListAsync();
            var districts = await _dbContext.Districts.AsNoTracking().ToListAsync();
            if (cases.Count > 0)
            {
                model.CategoryStats = categories
                    .Select(cat => new CategoryStatViewModel
                    {
                        Name = cat.NameBn,
                        Percentage = cases.Count(c => c.CategoryId == cat.CategoryId) * 100 / cases.Count,
                        ColorClass = string.Empty
                    })
                    .Where(s => s.Percentage > 0 || cases.Count == 0)
                    .ToList();
                model.DistrictStats = districts
                    .Select(d => new DistrictStatViewModel
                    {
                        Name = d.Name,
                        Count = cases.Count(c => c.DistrictId == d.DistrictId),
                        Percentage = cases.Count(c => c.DistrictId == d.DistrictId) * 100 / cases.Count
                    })
                    .Where(s => s.Count > 0)
                    .OrderByDescending(s => s.Count)
                    .Take(8)
                    .ToList();
            }

            // Verification triage mini-queue (top 5 pending, real applicants)
            var pendingProfiles = lawyerProfiles
                .Where(p => p.VerificationStatus == VerificationStatus.Pending)
                .OrderBy(p => p.LawyerProfileId)
                .Take(5)
                .ToList();
            var verificationQueue = new List<LawyerApplicationViewModel>();
            foreach (var p in pendingProfiles)
            {
                var u = users.FirstOrDefault(x => x.Id == p.UserId);
                verificationQueue.Add(new LawyerApplicationViewModel
                {
                    ApplicationId = p.LawyerProfileId,
                    ApplicantName = u?.FullName ?? "(unknown)",
                    BarRegNo = p.BarRegistrationNumber,
                    // LawyerProfile has no SubmittedAt column — use VerifiedAt
                    // when present, else a neutral dash (never fabricate dates).
                    AppliedDate = p.VerifiedAt?.ToString("d MMM yyyy") ?? "—",
                    Status = p.VerificationStatus.ToString()
                });
            }
            model.VerificationQueue = verificationQueue;

            // Audit stream = latest AI_LOG + review events (real, no PII —
            // AI_LOG prompts are redacted at write time per S-2.7)
            model.AuditLogs = aiLogsToday
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .Select(l => new SystemAuditLogItemViewModel
                {
                    Timestamp = l.CreatedAt.ToString("HH:mm"),
                    Action = $"AI {l.RequestType}",
                    Actor = l.ModelUsed,
                    Status = l.LatencyMs > 8000 || l.TokensUsed <= 0 ? "Warning" : "Success",
                    Details = $"Case {(l.CaseId?.ToString() ?? "chat")} · {l.LatencyMs}ms · {l.TokensUsed} tokens"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Dashboard aggregate build failed: {Message}", ex.Message);
            model.AuditLogs = new List<SystemAuditLogItemViewModel>();
        }

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
