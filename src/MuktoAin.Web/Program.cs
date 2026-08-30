using Microsoft.AspNetCore.Identity;
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

var builder = WebApplication.CreateBuilder(args);

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

// Schema is authored and controlled directly in SSMS via scripts/*.sql (T-1.6) --
// this context only maps onto that predefined schema. No EF migrations by design.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MuktoAin.Auth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
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

// NOTE: fully qualified on purpose -- Domain.Interfaces.* and
// Domain.Interfaces.Services.* both define IAiService/IEmbeddingService after the
// T-1.13 merge; GeminiClient/GeminiEmbeddingService implement the former.
builder.Services.AddSingleton<MuktoAin.Domain.Interfaces.IAiService>(
    sp => sp.GetRequiredService<GeminiClient>());

builder.Services.AddSingleton<GeminiEmbeddingService>();

builder.Services.AddSingleton<MuktoAin.Domain.Interfaces.IEmbeddingService>(
    sp => sp.GetRequiredService<GeminiEmbeddingService>());

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
builder.Services.AddDataProtection();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();

// S-1.8: Embedding batch job — indexes un-embedded chunks into Qdrant.
// Controlled by Embedding:RunOnStartup config flag.
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

// S-3.6: Admin user management (FR-18)
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

// A-2.2 & A-2.3: Document generation engine and templates
builder.Services.AddScoped<IDocumentTemplate, LabourComplaintTemplate>();
builder.Services.AddScoped<DocumentGenerator>();

// A-2.4: Document lifecycle service
builder.Services.AddScoped<DocumentService>();

// A-2.6: Lawyer verification service
builder.Services.AddScoped<LawyerVerificationService>();

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

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

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

    await ActImportService.SeedAsync(
        context,
        app.Environment.ContentRootPath,
        logger);

    await LegalChunkingService.ChunkAsync(
        context,
        logger);

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
    }

    var vectorStore = scope.ServiceProvider.GetRequiredService<QdrantVectorStore>();

    try
    {
        await vectorStore.EnsureCollectionAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning("Qdrant collection check failed -- vector search will be unavailable until the Qdrant endpoint is reachable. Error: {Error}", ex.Message);
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// S-1.1: authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();