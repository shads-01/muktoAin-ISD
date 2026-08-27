using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.Infrastructure.VectorStore;

/// <summary>
/// S-1.8: One-shot startup job that embeds all un-indexed ActSectionChunk rows
/// into the Qdrant vector store via the Gemini embedding API.
/// Activated by setting Embedding:RunOnStartup = true in appsettings.
/// </summary>
public class EmbeddingBatchJob : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<EmbeddingBatchJob> _logger;
    private readonly IConfiguration _configuration;

    private const int BatchSize = 100;
    private const int DelayBetweenCallsMs = 200;

    public EmbeddingBatchJob(
        IServiceScopeFactory scopeFactory,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<EmbeddingBatchJob> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var runOnStartup = _configuration.GetValue<bool>("Embedding:RunOnStartup");
        if (!runOnStartup)
        {
            _logger.LogInformation(
                "EmbeddingBatchJob: Embedding:RunOnStartup is false — skipping.");
            return;
        }

        _logger.LogInformation("EmbeddingBatchJob: Starting embedding batch job...");

        int totalProcessed = 0;
        int totalSkipped = 0;

        while (!ct.IsCancellationRequested)
        {
            // Scoped because IActSectionChunkRepository is Scoped (EF DbContext).
            using var scope = _scopeFactory.CreateScope();
            var chunkRepo = scope.ServiceProvider
                .GetRequiredService<IActSectionChunkRepository>();

            var chunks = (await chunkRepo.GetUnembeddedChunksAsync(BatchSize)).ToList();
            if (chunks.Count == 0)
            {
                _logger.LogInformation(
                    "EmbeddingBatchJob: No more un-embedded chunks. " +
                    "Total processed: {Processed}, skipped: {Skipped}.",
                    totalProcessed, totalSkipped);
                break;
            }

            foreach (var chunk in chunks)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // SHA-256 content hash for incremental re-indexing.
                    var hash = ComputeSha256(chunk.ChunkText);

                    // Skip if content hasn't changed (shouldn't happen for
                    // VectorId IS NULL rows, but guards against re-runs after
                    // partial schema updates).
                    if (chunk.ContentHash == hash && chunk.VectorId != null)
                    {
                        totalSkipped++;
                        continue;
                    }

                    // Call Gemini embedding API.
                    var embedding = await _embeddingService
                        .GetEmbeddingAsync(chunk.ChunkText, ct);

                    // Generate a stable vector ID from the chunk ID.
                    var vectorId = chunk.VectorId
                        ?? Guid.NewGuid().ToString();

                    // Build Qdrant payload for downstream RAG retrieval.
                    var payload = new Dictionary<string, string>
                    {
                        ["ChunkId"] = chunk.ChunkId.ToString(),
                        ["SectionId"] = chunk.SectionId.ToString(),
                        ["ChunkOrder"] = chunk.ChunkOrder.ToString(),
                        ["ChunkText"] = chunk.ChunkText,
                    };

                    // Include Act/Section metadata if navigation is loaded.
                    if (chunk.Section != null)
                    {
                        payload["SectionNumber"] = chunk.Section.SectionNumber
                                                   ?? string.Empty;
                        if (chunk.Section.Act != null)
                        {
                            payload["ActTitle"] = chunk.Section.Act.Title;
                        }
                    }

                    // Upsert into Qdrant.
                    await _vectorStore.UpsertAsync(vectorId, embedding, payload);

                    // Stamp the chunk row in SQL Server.
                    await chunkRepo.UpdateEmbeddingInfoAsync(
                        chunk.ChunkId, vectorId, hash);

                    totalProcessed++;

                    if (totalProcessed % 100 == 0)
                    {
                        _logger.LogInformation(
                            "EmbeddingBatchJob: Progress — {Processed} embedded, " +
                            "{Skipped} skipped.",
                            totalProcessed, totalSkipped);
                    }

                    // Rate-limit to stay within Gemini free-tier RPM.
                    await Task.Delay(DelayBetweenCallsMs, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // Let cancellation propagate.
                }
                catch (Exception ex)
                {
                    // Log and continue — resumability means we'll retry
                    // this chunk on the next run.
                    _logger.LogWarning(ex,
                        "EmbeddingBatchJob: Failed to embed chunk {ChunkId}. " +
                        "Will retry on next run.",
                        chunk.ChunkId);
                }
            }
        }

        _logger.LogInformation(
            "EmbeddingBatchJob: Completed. " +
            "Total processed: {Processed}, skipped: {Skipped}.",
            totalProcessed, totalSkipped);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
