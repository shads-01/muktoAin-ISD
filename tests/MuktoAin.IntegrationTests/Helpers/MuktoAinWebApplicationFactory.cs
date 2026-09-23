using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;
using MuktoAin.Infrastructure.Data;

namespace MuktoAin.IntegrationTests.Helpers;

public class MuktoAinWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"MuktoAin-Tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: skips UseDeveloperExceptionPage + Razor runtime
        // compilation, and exercises the real UseExceptionHandler pipeline.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // EmbeddingBatchJob: false -> background job no-ops in tests
                ["Embedding:RunOnStartup"] = "false",
                // Program.cs dimension guard needs VectorSize; dimensionality unset -> guard skipped
                ["Qdrant:VectorSize"] = "3",
                ["Qdrant:Endpoint"] = "http://127.0.0.1:1",
                ["SeedAdmin:Password"] = "Test-Admin-Passw0rd!",
                // The Gemini key pool is constructed eagerly by controllers
                // that take it (AdminController), and throws without at least
                // one key. IAiService is stubbed below, so no key is ever used.
                ["Gemini:ApiKeys:0"] = "test-key-not-used",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap SSMS-backed SQL Server for EF InMemory (tests have no LocalDB).
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Chat quota/credits: the real store is raw SQL Server T-SQL.
            services.RemoveAll<IAiTurnReservationStore>();
            services.AddSingleton<InMemoryAiTurnReservationStore.TurnLog>();
            services.AddScoped<IAiTurnReservationStore, InMemoryAiTurnReservationStore>();

            // Register TestAuthHandler so X-Test-UserId / X-Test-Role authenticate seamlessly
            services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            // Deterministic offline AI seams: the real GeminiClient would make
            // HTTP calls (wrapped in Polly retries) — unacceptable in tests.
            services.AddSingleton<MuktoAin.Domain.Interfaces.IAiService, StubAiService>();
            services.AddSingleton<MuktoAin.Domain.Interfaces.IEmbeddingService, StubEmbeddingService>();
            services.RemoveAll<IVectorStore>();
            services.AddSingleton<IVectorStore, StubVectorStore>();
            services.RemoveAll<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch>();
            services.AddSingleton<MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch, StubKeywordSectionSearch>();

            services.RemoveAll<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
            services.AddSingleton<Microsoft.AspNetCore.Antiforgery.IAntiforgery, PassThroughAntiforgery>();
        });
    }

    public HttpClient CreateClientWithoutRedirects() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Returns a client that authenticates as the given user for every request.
    public HttpClient CreateAuthenticatedClient(int userId, UserRole role, bool allowAutoRedirect = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role.ToString());
        return client;
    }
}

// Plain-text answer — AiOrchestrationService treats the AI response as opaque
// content (verified: no JSON parsing of the explanation body), so a constant is enough.
public class StubAiService : MuktoAin.Domain.Interfaces.IAiService
{
    public Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult("স্টাব উত্তর: আপনার অধিকার বিশ্লেষণ / Stub analysis of your rights.");

    public Task<float[]> EmbedContentAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

    public Task<IReadOnlyList<float[]>> BatchEmbedContentAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(new List<float[]> { new float[] { 0.1f, 0.2f, 0.3f } });
}

public class StubEmbeddingService : MuktoAin.Domain.Interfaces.IEmbeddingService
{
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });
}

// Returns one high-score hit for any query so RagContextBuilder produces a
// deterministic citation (SimilaritySearchService then hydrates via the
// section repository — seed Act/ActSection id 1 in TestData when needed).
public class StubVectorStore : IVectorStore
{
    public Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload) => Task.CompletedTask;
    public Task DeleteAsync(string vectorId) => Task.CompletedTask;

    public Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK) =>
        Task.FromResult<IEnumerable<VectorSearchResult>>(new[]
        {
            new VectorSearchResult(
                VectorId: "sec_1_chk_1",
                Score: 0.95f,
                Payload: new Dictionary<string, string>
                {
                    ["SectionId"] = "1",
                    ["ChunkId"] = "1",
                    ["ActTitle"] = "Bangladesh Labour Act, 2006",
                    ["SectionNumber"] = "123",
                })
        });
}

public class StubKeywordSectionSearch : MuktoAin.Domain.Interfaces.Services.IKeywordSectionSearch
{
    public Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int maxResults = 8) =>
        Task.FromResult(Enumerable.Empty<RetrievedSection>());
}

public class PassThroughAntiforgery : Microsoft.AspNetCore.Antiforgery.IAntiforgery
{
    public Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet GetAndStoreTokens(Microsoft.AspNetCore.Http.HttpContext httpContext) =>
        new("test-token", "test-cookie", "__RequestVerificationToken", "RequestVerificationToken");

    public Microsoft.AspNetCore.Antiforgery.AntiforgeryTokenSet GetTokens(Microsoft.AspNetCore.Http.HttpContext httpContext) =>
        new("test-token", "test-cookie", "__RequestVerificationToken", "RequestVerificationToken");

    public Task<bool> IsRequestValidAsync(Microsoft.AspNetCore.Http.HttpContext httpContext) => Task.FromResult(true);

    public Task ValidateRequestAsync(Microsoft.AspNetCore.Http.HttpContext httpContext) => Task.CompletedTask;

    public void SetCookieTokenAndHeader(Microsoft.AspNetCore.Http.HttpContext httpContext) { }
}


