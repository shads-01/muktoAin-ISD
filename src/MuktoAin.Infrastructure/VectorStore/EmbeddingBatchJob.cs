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

public static class EmbeddingProgressState
{
    public static bool IsRunning { get; set; }
    public static int TotalProcessed { get; set; }
    public static int TotalSkipped { get; set; }
    public static string LastStatus { get; set; } = "Idle";
    public static DateTime? StartedAt { get; set; }
    public static DateTime? LastUpdatedAt { get; set; }
}

/// <summary>
/// S-1.8: One-shot startup background worker that embeds un-indexed ActSectionChunk rows
/// into the Qdrant vector store via the Gemini embedding API.
/// Runs asynchronously in BackgroundService so the web server starts immediately.
/// </summary>
public class EmbeddingBatchJob : BackgroundService
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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var runOnStartup = _configuration.GetValue<bool>("Embedding:RunOnStartup");
        if (!runOnStartup)
        {
            EmbeddingProgressState.IsRunning = false;
            EmbeddingProgressState.LastStatus = "Disabled in appsettings (Embedding:RunOnStartup is false)";
            _logger.LogInformation("EmbeddingBatchJob: Embedding:RunOnStartup is false — skipping.");
            return;
        }

        EmbeddingProgressState.IsRunning = true;
        EmbeddingProgressState.StartedAt = DateTime.UtcNow;
        var startupDelayMs = _configuration.GetValue<int?>("Embedding:StartupDelayMs") ?? 3000;
        if (startupDelayMs > 0)
        {
            EmbeddingProgressState.LastStatus = "Waiting for app startup...";
            _logger.LogInformation("EmbeddingBatchJob: Waiting for app to finish startup ({DelayMs}ms)...", startupDelayMs);
            await Task.Delay(startupDelayMs, ct);
        }

        EmbeddingProgressState.LastStatus = "Running";
        _logger.LogInformation("EmbeddingBatchJob: Starting embedding background job...");

        int totalProcessed = 0;
        int totalSkipped = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Scoped because IActSectionChunkRepository is Scoped (EF DbContext).
                using var scope = _scopeFactory.CreateScope();
                var chunkRepo = scope.ServiceProvider
                    .GetRequiredService<IActSectionChunkRepository>();

                _logger.LogInformation("EmbeddingBatchJob: Fetching next batch of {BatchSize} unembedded chunks...", BatchSize);
                var chunks = (await chunkRepo.GetUnembeddedChunksAsync(BatchSize)).ToList();
                _logger.LogInformation("EmbeddingBatchJob: Fetched {Count} chunks from DB.", chunks.Count);

                if (chunks.Count == 0)
                {
                    EmbeddingProgressState.LastStatus = "Completed (All chunks indexed)";
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

                        // Skip if content hasn't changed
                        if (chunk.ContentHash == hash && chunk.VectorId != null)
                        {
                            totalSkipped++;
                            EmbeddingProgressState.TotalSkipped = totalSkipped;
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
                        EmbeddingProgressState.TotalProcessed = totalProcessed;
                        EmbeddingProgressState.LastUpdatedAt = DateTime.UtcNow;

                        if (totalProcessed % 50 == 0)
                        {
                            _logger.LogInformation(
                                "EmbeddingBatchJob: Progress — {Processed} embedded, {Skipped} skipped.",
                                totalProcessed, totalSkipped);
                        }

                        // Rate-limit to stay within Gemini free-tier RPM.
                        await Task.Delay(DelayBetweenCallsMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "EmbeddingBatchJob: Failed to embed chunk {ChunkId}. Will retry on next run.",
                            chunk.ChunkId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            EmbeddingProgressState.LastStatus = "Cancelled / Stopped";
        }
        catch (Exception ex)
        {
            EmbeddingProgressState.LastStatus = $"Error: {ex.Message}";
            _logger.LogError(ex, "EmbeddingBatchJob: Fatal error in background loop.");
        }
        finally
        {
            EmbeddingProgressState.IsRunning = false;
        }
    }

    public static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
