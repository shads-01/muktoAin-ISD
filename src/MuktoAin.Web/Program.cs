using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MuktoAin.Application.Documents;
using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Infrastructure.Ai;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Data.Seeding;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.Infrastructure.Search;
using MuktoAin.Infrastructure.Security;
using MuktoAin.Infrastructure.VectorStore;
using MuktoAin.Web.Auth;
using MuktoAin.Web.Localization;
using MuktoAin.Web.Middleware;
using MuktoAin.Web.Resources;
using MuktoAin.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Development port watchdog: reclaims port 5250 from any stale instances and monitors parent process
DevProcessWatchdog.Initialize(builder.Environment);

// Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// S-3.5: server-side localization. The client-side data-bn/data-en toggle in
// main.js REMAINS the primary UI translation mechanism — this only drives
// SERVER-rendered strings (validation errors, identity errors) via .resx.
builder.Services.AddLocalization();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultureNames = new[] { "bn-BD", "en" };

    options.SetDefaultCulture("bn-BD")
        .AddSupportedCultures(supportedCultureNames)
        .AddSupportedUICultures(supportedCultureNames);

    // Replace the defaults (querystring/header/AspNetCore.Culture cookie) with
    // ONLY our provider: the mkt-lang cookie main.js already maintains. No new
    // cookie, no SetLanguage endpoint — one language state, one toggle.
    options.RequestCultureProviders.Clear();
    options.RequestCultureProviders.Add(new MuktoAinLanguageCookieProvider());
});

// Schema is authored and controlled directly in SSMS via scripts/*.sql (T-1.6) --
// this context only maps onto that predefined schema. No EF migrations by design.
// Real-time notifications: every saved NOTIFICATION change is pushed to its
// owner over SignalR (see NotificationPushInterceptor / Hubs/NotificationHub).
builder.Services.AddSignalR();
builder.Services.AddScoped<MuktoAin.Web.Services.NotificationPushInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.UseCompatibilityLevel(120);
            sqlOptions.CommandTimeout(60);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        })
        .AddInterceptors(
            sp.GetRequiredService<MuktoAin.Web.Services.NotificationPushInterceptor>()));

// S-1.1: ASP.NET Core Identity against the manually-authored [dbo].[USER] table.
// Role tables do not exist in the SSMS schema by design -- authorization runs off
// the User.Role enum via UserRoleClaimsTransformation (see Auth/ folder).
builder.Services.AddIdentityCore<User>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager<SignInManager<User>>()
.AddDefaultTokenProviders()
// Works around AspNetUserClaims not existing in the SSMS schema -- see
// NoClaimsStoreUserClaimsPrincipalFactory for why the default factory breaks real
// sign-in without this.
.AddClaimsPrincipalFactory<NoClaimsStoreUserClaimsPrincipalFactory>();

// AddIdentityCore does NOT wire cookie authentication -- done explicitly here.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim("IsSuperAdmin", "true"));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MuktoAin.Auth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// AUD-5: per-route throttling (audit: nothing limits Login/Register brute
// force, Chat/Ask metered-model spam, or payment endpoints). Fixed windows,
// partitioned per IP for auth and per user (falling back to IP for guests)
// for chat/payment. Rejections return the default 429 via UseStatusCodePages.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("chat", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("payment", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddTransient<
    Microsoft.AspNetCore.Authentication.IClaimsTransformation,
    UserRoleClaimsTransformation>();

// S-1.3 / S-1.4 / S-2.6: Gemini client (key rotation inside), shared Polly
// resilience pipeline (timeout -> circuit breaker -> retry). Singleton so the
// round-robin key index persists across requests.
builder.Services.Configure<GeminiOptions>(
    builder.Configuration.GetSection(GeminiOptions.SectionName));

builder.Services.AddHttpClient(nameof(GeminiClient));

builder.Services.AddSingleton(sp =>
    GeminiResiliencePolicies.Build(
        sp.GetRequiredService<IOptions<GeminiOptions>>().Value));

builder.Services.AddSingleton<GeminiClient>();

// NOTE: fully qualified on purpose -- IEmbeddingService is defined in both
// Domain.Interfaces.* and Domain.Interfaces.Services.*; GeminiEmbeddingService
// implements the former. (The old Services/IAiService duplicate was deleted
// with the conversational chat redesign — only Domain.Interfaces.IAiService remains.)
builder.Services.AddSingleton<MuktoAin.Domain.Interfaces.IAiService>(
    sp => sp.GetRequiredService<GeminiClient>());

builder.Services.AddSingleton<GeminiEmbeddingService>();

builder.Services.AddSingleton<MuktoAin.Domain.Interfaces.IEmbeddingService>(
    sp => sp.GetRequiredService<GeminiEmbeddingService>());

// FR-24 payment gateways. PaymentGatewayResolver picks one per order from
// the citizen's method and Payments:Mode: Simulator (default) sends every
// method to the built-in SimulatedGateway (singleton, it holds the checkout
// sessions its GatewaySimulatorController pages drive); Sandbox sends bKash to
// the bKash sandbox and card to the SSLCommerz sandbox (typed HttpClients,
// credentials in the Bkash / SslCommerz sections).
builder.Services.Configure<MuktoAin.Infrastructure.Payments.PaymentGatewayOptions>(
    builder.Configuration.GetSection(MuktoAin.Infrastructure.Payments.PaymentGatewayOptions.SectionName));
builder.Services.Configure<MuktoAin.Infrastructure.Payments.SslCommerzOptions>(
    builder.Configuration.GetSection(MuktoAin.Infrastructure.Payments.SslCommerzOptions.SectionName));
builder.Services.Configure<MuktoAin.Infrastructure.Payments.BkashOptions>(
    builder.Configuration.GetSection(MuktoAin.Infrastructure.Payments.BkashOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<MuktoAin.Infrastructure.Payments.SslCommerzGatewayClient>();
builder.Services.AddHttpClient<MuktoAin.Infrastructure.Payments.BkashGatewayClient>();
builder.Services.AddSingleton<MuktoAin.Infrastructure.Payments.BkashTokenCache>();
builder.Services.AddSingleton<MuktoAin.Infrastructure.Payments.SimulatedGateway>();
builder.Services.AddScoped<MuktoAin.Domain.Interfaces.Services.IPaymentGatewayResolver,
    MuktoAin.Infrastructure.Payments.PaymentGatewayResolver>();

// T-1.11: Qdrant vector store. Registered as both the concrete type (so Program.cs can
// call EnsureCollectionAsync below) and the IVectorStore interface (so consumers like
// SimilaritySearchService depend on the Domain abstraction, not Infrastructure).
builder.Services.Configure<QdrantOptions>(
    builder.Configuration.GetSection("Qdrant"));

builder.Services.AddSingleton<QdrantVectorStore>();

builder.Services.AddSingleton<IVectorStore>(
    sp => sp.GetRequiredService<QdrantVectorStore>());

// T-1.13: Repositories (T-1.12). Generic IRepository<T> covers entities with no custom
// query needs (District, CaseCategory, ActFootnote, GeneratedDocument, LawyerProfile,
// LawyerReview, AiLog, CaseActReference); the rest have dedicated interfaces below.
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

builder.Services.AddScoped<IActRepository, ActRepository>();
builder.Services.AddScoped<IActSectionRepository, ActSectionRepository>();
builder.Services.AddScoped<ICaseRepository, CaseRepository>();
builder.Services.AddScoped<IActSectionChunkRepository, ActSectionChunkRepository>();
builder.Services.AddScoped<IScenarioMappingRepository, ScenarioMappingRepository>();
builder.Services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();

// Case lifecycle service
builder.Services.AddScoped<CaseService>();

// T-2.1: Qdrant vector similarity search (FR-3 primary retrieval path).
builder.Services.AddScoped<IVectorSectionSearch, SimilaritySearchService>();

// T-2.2: SQL Server FTS keyword search (FR-7 standalone search + FR-3 vector fallback).
builder.Services.AddScoped<IKeywordSectionSearch, KeywordSearchService>();

// T-2.3: Vector-primary/keyword-fallback context retrieval for FR-3 (PromptAssembler's
// upstream seam).
builder.Services.AddScoped<IRagContextBuilder, RagContextBuilder>();

// T-2.4: Standalone Acts search (FR-7).
builder.Services.AddScoped<SearchService>();

// T-2.5: Category browsing (FR-6).
builder.Services.AddScoped<CategoryService>();

// S-1.6: Disclaimer injector (surface 2 of 3 — AI output disclaimer).
builder.Services.AddSingleton<DisclaimerInjector>();

// S-1.7: Data Protection + field-level PII encryption.
// AUD-2: persist the key ring under ContentRootPath/keys and pin the
// application name. Without this, every container redeploy rotates the key
// (the default path is ephemeral in the shipped Dockerfile) and encrypted
// Case.Title/Description become permanently unreadable ciphertext.
// DataProtection:KeysPath moves the ring outside the content root for hosts
// that replace it on deploy (Azure App Service: /home/data/keys).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(DataProtectionKeysPath.Resolve(
        builder.Configuration["DataProtection:KeysPath"],
        builder.Environment.ContentRootPath)))
    .SetApplicationName("MuktoAin.Web");
builder.Services.AddScoped<IEncryptionService, EncryptionService>();

// S-1.8: Embedding batch job — indexes un-embedded chunks into Qdrant.
// Controlled by Embedding:RunOnStartup config flag. The quota state is
// config-driven (Embedding:MaxRequestsPerMinute / MaxTokensPerMinute) so the
// pacing budgets match the REAL free-tier limits read from 429 quotaIds —
// keys under one Google Cloud project share ONE quota pool, so key count
// does not multiply these numbers.
builder.Services.AddSingleton(sp => new EmbeddingQuotaState(
    maxRequestsPerMinute: sp.GetRequiredService<IConfiguration>()
        .GetValue<int?>("Embedding:MaxRequestsPerMinute") ?? 100,
    maxTokensPerMinute: sp.GetRequiredService<IConfiguration>()
        .GetValue<int?>("Embedding:MaxTokensPerMinute") ?? 100_000));
builder.Services.AddHostedService<EmbeddingBatchJob>();

// S-2.1: Prompt assembly from retrieved sections + scenario mappings
builder.Services.AddScoped<IPromptAssembler, PromptAssembler>();

// S-2.4 + S-2.7: AI audit logging with PII redaction
builder.Services.AddScoped<IAiLogService, AiLogService>();

// S-2.2: Central AI orchestration pipeline
builder.Services.AddScoped<IAiOrchestrationService>(sp =>
    new AiOrchestrationService(
        sp.GetRequiredService<IRagContextBuilder>(),
        sp.GetRequiredService<IPromptAssembler>(),
        sp.GetRequiredService<MuktoAin.Domain.Interfaces.IAiService>(),
        sp.GetRequiredService<DisclaimerInjector>(),
        sp.GetRequiredService<IAiLogService>(),
        sp.GetRequiredService<IRepository<AiLog>>(),
        sp.GetRequiredService<IRepository<CaseActReference>>(),
        sp.GetRequiredService<IOptions<GeminiOptions>>().Value.GenerationModel));

// S-2.3: Rights explanation facade (FR-4)
builder.Services.AddScoped<IRightsExplanationService, RightsExplanationService>();

// S-3.1 + S-3.2: QA benchmark harness (dataset loader + zero-shot/few-shot
// evaluation runner; S-3.3 reuses the same runner with a prompt-variant option).
builder.Services.AddSingleton<IBenchmarkLoader, BenchmarkLoaderService>();
builder.Services.AddScoped<IBenchmarkRunner, BenchmarkRunnerService>();

// S-3.6: Admin user management (FR-18)
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

// T-3.1: admin Acts CRUD + SHA-256 content-hash re-indexing (FR-17, wires to E-3.2).
builder.Services.AddScoped<IActsManagementService, ActsManagementService>();

// T-3.2: admin CRUD over FR-18 scenario keyword boosts (wires to E-3.2).
builder.Services.AddScoped<IScenarioMappingService, ScenarioMappingService>();

// A-3.2: Admin analytics service (FR-16)
builder.Services.AddScoped<IAdminAnalyticsService, AdminAnalyticsService>();
builder.Services.AddScoped<AdminAnalyticsService>();

// A-3.5 (Step 3.5): Submission content moderation service
builder.Services.AddScoped<IModerationService, ModerationService>();
builder.Services.AddScoped<ModerationService>();

// A-2.2, A-2.3 & A-3.1 (Steps 3.1-3.3): Document generation engine and all 4 templates
builder.Services.AddScoped<IDocumentTemplate, LabourComplaintTemplate>();
builder.Services.AddScoped<IDocumentTemplate, GeneralDiaryTemplate>();
builder.Services.AddScoped<IDocumentTemplate, RtiRequestTemplate>();
builder.Services.AddScoped<IDocumentTemplate, ConsumerComplaintTemplate>();
builder.Services.AddScoped<DocumentGenerator>();

// A-2.4: Document lifecycle service
builder.Services.AddScoped<DocumentService>();

// A-2.5: QuestPDF export (Bangla font). Fonts live in wwwroot/fonts (E-1.3);
// registered once per process inside PdfExportService (idempotent).
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var fontsDir = Path.Combine(env.WebRootPath ?? env.ContentRootPath, "fonts");
    return new MuktoAin.Infrastructure.Documents.PdfExportService(
        Path.Combine(fontsDir, "NotoSansBengali-Regular.ttf"),
        Path.Combine(fontsDir, "NotoSansBengali-Bold.ttf"));
});
builder.Services.AddSingleton<MuktoAin.Domain.Interfaces.Services.IPdfExporter>(
    sp => sp.GetRequiredService<MuktoAin.Infrastructure.Documents.PdfExportService>());

// A-2.6: Lawyer verification service
builder.Services.AddScoped<LawyerVerificationService>();

// Frontend redesign 2026-09: chat-first home + AI budget
// AUD-3: atomic chat-turn reservation store (single-statement SQL with
// UPDLOCK/HOLDLOCK — see AiTurnReservationStore for the TOCTOU rationale).
builder.Services.AddScoped<IAiTurnReservationStore, AiTurnReservationStore>();
builder.Services.AddScoped<AiBudgetService>();
builder.Services.AddScoped<ChatService>();

// Part B: lawyer review flow
builder.Services.AddScoped<LawyerReviewService>();
builder.Services.AddScoped<PaymentService>();
// AUD-7: administrative audit trail (fail-safe writer).
builder.Services.AddScoped<IAdminAuditService, AdminAuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<LawyerQueueNotifier>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/ServerError");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

// S-3.5: must run before anything that renders or resolves culture (static
// files, routing, controllers) so CultureInfo.CurrentUICulture is correct
// whenever a server-rendered string is produced.
app.UseRequestLocalization();

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

// AUD-6: security headers on every response (incl. static files + error pages).
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseSession();

// Do NOT call Database.MigrateAsync() -- see the "No EF migrations" note above.
// Seeders assume the SSMS scripts have already been executed.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await SeedDistricts.SeedAsync(
        context,
        app.Environment.ContentRootPath);

    await SeedCategories.SeedAsync(
        context,
        app.Environment.ContentRootPath);

    if (!app.Environment.IsEnvironment("Testing"))
    {
        await ActImportService.SeedAsync(
            context,
            app.Environment.ContentRootPath,
            logger);

        await LegalChunkingService.ChunkAsync(
            context,
            logger);
    }

    await SeedScenarioMappings.SeedAsync(
        context,
        app.Environment.ContentRootPath,
        logger);

    // S-1.2: bootstrap the first admin account (idempotent).
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    await SeedAdminUser.SeedAsync(
        userManager,
        builder.Configuration,
        logger);

    // Dev-only demo data (citizens, lawyers, cases, documents, a review) so the app
    // has something to click through end-to-end. Never runs outside Development.
    if (app.Environment.IsDevelopment())
    {
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        await SeedDemoData.SeedAsync(context, userManager, encryptionService, logger);

        await SeedDemoUsers.SeedAsync(
            userManager,
            scope.ServiceProvider.GetRequiredService<IRepository<LawyerProfile>>(),
            logger);

        // Finalized, unpaid cases for citizen@muktoain.bd to try the honorarium payment on.
        await SeedDemoPaymentCases.SeedAsync(context, userManager, encryptionService, logger);

        // One approved lawyer per specialization + one queued case per category,
        // for the queue's "My field" filter.
        await SeedSpecializationDemo.SeedAsync(context, userManager, encryptionService, logger);
    }

    var vectorStore = scope.ServiceProvider.GetRequiredService<QdrantVectorStore>();

    // Fail fast on dimension mismatch: Gemini:EmbeddingOutputDimensionality (when
    // set) MUST equal Qdrant:VectorSize, or upserts/searches silently corrupt.
    var geminiOpts = app.Services.GetRequiredService<IOptions<GeminiOptions>>().Value;
    var qdrantOpts = app.Services.GetRequiredService<IOptions<QdrantOptions>>().Value;
    if (geminiOpts.EmbeddingOutputDimensionality is { } dims && dims != qdrantOpts.VectorSize)
    {
        throw new InvalidOperationException(
            $"Gemini:EmbeddingOutputDimensionality ({dims}) != Qdrant:VectorSize ({qdrantOpts.VectorSize}). " +
            "Fix appsettings so embedding output and the Qdrant collection agree.");
    }

    try
    {
        await vectorStore.EnsureCollectionAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning("Qdrant collection check failed -- vector search will be unavailable until the Qdrant endpoint is reachable. Error: {Error}", ex.Message);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

// S-1.1: authentication must run before authorization.
app.UseAuthentication();

// AUD-5: endpoint-scoped limiter policies require UseRouting to have matched
// the endpoint first. Runs AFTER authentication so PartitionKey can read the
// signed-in user's claims — placing it before UseAuthentication would leave
// HttpContext.User empty and silently degrade the chat/payment policies to
// per-IP (every user behind shared NAT/CGNAT would share one budget).
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<MuktoAin.Web.Hubs.NotificationHub>(MuktoAin.Web.Hubs.NotificationHub.Path);

app.Run();

// AUD-5: per-user partition for the chat/payment policies; guests fall back
// to their remote IP so anonymous floods are still bounded.
static string PartitionKey(HttpContext httpContext) =>
    httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

// E-3.4 / A-3.7: WebApplicationFactory<Program> requires a compilable Program type.
// Top-level statements don't emit one unless a partial class is declared.
public partial class Program { }