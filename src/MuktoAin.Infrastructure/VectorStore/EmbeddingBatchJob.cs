using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Infrastructure.Ai;
using IEmbeddingService = MuktoAin.Domain.Interfaces.IEmbeddingService;

namespace MuktoAin.Infrastructure.VectorStore;

public static class EmbeddingProgressState
{
    public static bool IsRunning { get; set; }
    public static int TotalProcessed { get; set; }
    public static int TotalSkipped { get; set; }
    public static string LastStatus { get; set; } = "Idle";
    public static string? LastStatusEn { get; set; }
    public static DateTime? StartedAt { get; set; }
    public static DateTime? LastUpdatedAt { get; set; }
    public static double RequestsPerMinuteBudget { get; set; }
    public static string EstimatedCompletion { get; set; } = string.Empty;
    public static string? EstimatedCompletionEn { get; set; }

    /// <summary>Sets the status in both languages (dashboard follows mkt-lang).</summary>
    public static void SetStatus(string bn, string en)
    {
        LastStatus = bn;
        LastStatusEn = en;
    }
}

/// <summary>
/// S-1.8: One-shot startup background worker that embeds un-indexed ActSectionChunk rows
/// into the Qdrant vector store via the Gemini embedding API.
/// Runs asynchronously in BackgroundService so the web server starts immediately.
///
/// Free-tier throughput pipeline (v3):
///   1. Token-packed batches — pack chunks until ~Embedding:BatchMaxTokens estimated
///      tokens or Embedding:BatchMaxTexts texts, whichever binds first. The
///      batchEmbedContents API hard-caps at 100 texts/request (400 above that),
///      so BatchMaxTexts must stay <= 100 — the token ceiling binds only for
///      unusually long chunks.
///   2. GLOBAL dedupe — ALL pending chunk rows are hashed up front (one keyset-paged
///      SQL scan), and duplicate texts are collapsed to ONE embedding for the whole
///      run, not just within a single fetch window. Follower rows are stamped with
///      the leader's VectorId and never touch the API.
///   3. Adaptive quota pacing (EmbeddingQuotaState) — an AIMD budget over a rolling
///      60s window; 429s halve it and freeze sending for exactly Google's
///      RetryInfo.retryDelay, clean minutes earn it back. A PerDay quotaId parks
///      the job until the midnight-Pacific reset instead of burning requests.
///   4. Parallel in-flight batches (Embedding:MaxConcurrentBatches, default 2) —
///      the loop is latency-bound otherwise (one embed -> Qdrant -> SQL round trip
///      at a time); concurrent workers share the quota gate so the total rate
///      never exceeds the pacing budget. Failed batches are re-enqueued so no
///      chunk is lost from the queue.
///   5. Real ETA — GetUnembeddedCountAsync + measured throughput feed the admin
///      dashboard, so "how long is this going to take" is observable, not a guess.
/// </summary>
public class EmbeddingBatchJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<EmbeddingBatchJob> _logger;
    private readonly IConfiguration _configuration;
    private readonly EmbeddingQuotaState _quota;

    private int _charsPerToken;
    private int _maxTokensPerRequest;
    private int _maxTextsPerRequest;
    private int _maxConcurrentBatches;
    private int _consecutiveTransientFailures;

    // Total processed/skipped counters shared across concurrent batch workers.
    private int _totalProcessed;
    private int _totalSkipped;

    // A batch that keeps failing (poisoned rows, dead downstream) is dropped after
    // this many consecutive transient failures so the worker can exit and the
    // one-shot job terminates instead of retrying forever.
    private const int MaxConsecutiveTransientFailures = 5;

    public EmbeddingBatchJob(
        IServiceScopeFactory scopeFactory,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<EmbeddingBatchJob> logger,
        IConfiguration configuration)
        : this(scopeFactory, embeddingService, vectorStore, logger, configuration, new EmbeddingQuotaState())
    {
    }

    public EmbeddingBatchJob(
        IServiceScopeFactory scopeFactory,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<EmbeddingBatchJob> logger,
        IConfiguration configuration,
        EmbeddingQuotaState quotaState)
    {
        _scopeFactory = scopeFactory;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
        _configuration = configuration;
        _quota = quotaState;
    }

    private sealed record WorkItem(
        ActSectionChunk Chunk,
        string Hash,
        string VectorId,
        Dictionary<string, string> Payload);

    private sealed record PackedBatch(
        List<WorkItem> WorkItems,
        List<string> TextsToEmbed,
        int SkippedStale);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var runOnStartup = _configuration.GetValue<bool>("Embedding:RunOnStartup");
        if (!runOnStartup)
        {
            EmbeddingProgressState.IsRunning = false;
            EmbeddingProgressState.SetStatus("Disabled in appsettings (Embedding:RunOnStartup is false)", "Disabled in appsettings (Embedding:RunOnStartup is false)");
            _logger.LogInformation("EmbeddingBatchJob: Embedding:RunOnStartup is false — skipping.");
            return;
        }

        EmbeddingProgressState.IsRunning = true;
        EmbeddingProgressState.StartedAt = DateTime.UtcNow;
        var startupDelayMs = _configuration.GetValue<int?>("Embedding:StartupDelayMs") ?? 10000;
        if (startupDelayMs > 0)
        {
            EmbeddingProgressState.SetStatus("অ্যাপ চালু হওয়ার অপেক্ষায়...", "Waiting for app startup...");
            _logger.LogInformation("EmbeddingBatchJob: Waiting for app to finish startup ({DelayMs}ms)...", startupDelayMs);
            await Task.Delay(startupDelayMs, ct);
        }

        _maxTokensPerRequest = _configuration.GetValue<int?>("Embedding:BatchMaxTokens") ?? 25000;
        _maxTextsPerRequest = _configuration.GetValue<int?>("Embedding:BatchMaxTexts") ?? 100;
        if (_maxTextsPerRequest > GeminiClient.MaxTextsPerBatchRequest)
        {
            // batchEmbedContents hard-caps at 100 texts/request (400 INVALID_ARGUMENT
            // above that) — clamp misconfigured values instead of failing every batch.
            _logger.LogWarning(
                "EmbeddingBatchJob: Embedding:BatchMaxTexts ({Configured}) exceeds the batchEmbedContents API cap of {Cap} — clamping to {Cap}.",
                _maxTextsPerRequest, GeminiClient.MaxTextsPerBatchRequest, GeminiClient.MaxTextsPerBatchRequest);
            _maxTextsPerRequest = GeminiClient.MaxTextsPerBatchRequest;
        }
        _charsPerToken = _configuration.GetValue<int?>("Embedding:CharsPerToken") ?? 3;
        _maxConcurrentBatches = Math.Max(1, _configuration.GetValue<int?>("Embedding:MaxConcurrentBatches") ?? 2);

        EmbeddingProgressState.SetStatus("চলছে", "Running");
        _logger.LogInformation(
            "EmbeddingBatchJob: Starting batch embedding (adaptive quota pacing, {Workers} concurrent batches, {MaxTexts} texts/{MaxTokens} tokens per request)...",
            _maxConcurrentBatches, _maxTextsPerRequest, _maxTokensPerRequest);

        var runStartedAt = DateTime.UtcNow;
        var runStartRemaining = -1;

        try
        {
            // ---- Phase 1: global dedupe pass ----
            // Hash ALL pending rows once and collapse duplicate texts across the
            // WHOLE run (not per fetch window): each distinct text is embedded
            // exactly once no matter where its duplicate rows sit in ChunkId order.
            EmbeddingProgressState.SetStatus("ডুপ্লিকেট সনাক্তকরণ...", "Scanning for duplicate chunk texts...");
            var dedupePlan = await BuildGlobalDedupeAsync(ct);
            if (dedupePlan.Leaders.Count == 0 && dedupePlan.FollowerRows.Count == 0)
            {
                EmbeddingProgressState.SetStatus("সম্পন্ন (সকল চাঙ্ক ইনডেক্সকৃত)", "Completed (All chunks indexed)");
                EmbeddingProgressState.EstimatedCompletion = "সম্পন্ন"; EmbeddingProgressState.EstimatedCompletionEn = "Done";
                _logger.LogInformation("EmbeddingBatchJob: Nothing to embed.");
                return;
            }

            _logger.LogInformation(
                "EmbeddingBatchJob: Global dedupe pass complete — {Leaders} distinct texts to embed, {Followers} duplicate rows collapsed (each costs ONE embedding now).",
                dedupePlan.Leaders.Count, dedupePlan.FollowerRows.Count);

            // ---- Phase 2: embed leaders in token-packed, quota-paced, parallel batches ----
            var leaderQueue = new ConcurrentQueue<ActSectionChunk>(dedupePlan.Leaders);

            using var scope = _scopeFactory.CreateScope();
            var chunkRepo = scope.ServiceProvider.GetRequiredService<IActSectionChunkRepository>();

            // Retried read: under RAM pressure even this COUNT can time out, and
            // it only feeds the ETA — a stale-but-plausible fallback is better
            // than a fatal job.
            runStartRemaining = await ReadWithRetryAsync(
                () => chunkRepo.GetUnembeddedCountAsync(),
                phase: "un-embedded count",
                fallback: dedupePlan.Leaders.Count + dedupePlan.FollowerRows.Count,
                ct);
            _logger.LogInformation("EmbeddingBatchJob: {Remaining} chunk rows pending at run start.", runStartRemaining);

            var workers = Enumerable.Range(0, _maxConcurrentBatches)
                .Select(_ => Task.Run(() => WorkerLoopAsync(
                    leaderQueue, runStartedAt, runStartRemaining, ct), ct))
                .ToArray();

            await Task.WhenAll(workers);

            // ---- Phase 3: stamp follower rows (duplicates) with their leader's vector ----
            // Safe point: the queue only drains when every leader batch fully
            // succeeded, so every follower's leader is guaranteed embedded.
            if (dedupePlan.FollowerRows.Count > 0)
            {
                EmbeddingProgressState.SetStatus("ডুপ্লিকেট রো স্ট্যাম্প করা হচ্ছে...", "Stamping duplicate rows with shared vectors...");
                await StampFollowersWithRetryAsync(chunkRepo, dedupePlan, ct);
            }

            EmbeddingProgressState.SetStatus("সম্পন্ন (সকল চাঙ্ক ইনডেক্সকৃত)", "Completed (All chunks indexed)");
            EmbeddingProgressState.EstimatedCompletion = "সম্পন্ন"; EmbeddingProgressState.EstimatedCompletionEn = "Done";
            _logger.LogInformation(
                "EmbeddingBatchJob: Done. Total processed: {Processed}, skipped: {Skipped}.",
                _totalProcessed, _totalSkipped);
        }
        catch (OperationCanceledException)
        {
            EmbeddingProgressState.SetStatus("বাতিল / বন্ধ", "Cancelled / Stopped");
        }
        catch (Exception ex)
        {
            EmbeddingProgressState.SetStatus($"ত্রুটি: {ex.Message}", $"Error: {ex.Message}");
            _logger.LogError(ex, "EmbeddingBatchJob: Fatal error in background loop.");
        }
        finally
        {
            EmbeddingProgressState.IsRunning = false;
        }
    }

    /// <summary>
    /// One shared worker loop: drain the leader queue, packing each iteration into
    /// a token-capped batch, gated by the shared quota state. All workers share the
    /// same quota window, so raising worker count never raises the total request rate.
    /// A batch that fails at ANY stage (embed, Qdrant upsert, SQL stamp) is put back
    /// on the queue so no chunk is silently lost from this run.
    ///
    /// FIX-EMB-6: each worker resolves its OWN scoped IActSectionChunkRepository
    /// (own AppDbContext) — DbContext is not thread-safe, so sharing one repository
    /// across concurrent workers made simultaneous UpdateBatchEmbeddingInfoAsync
    /// calls throw "A second operation was started on this context instance".
    /// </summary>
    private async Task WorkerLoopAsync(
        ConcurrentQueue<ActSectionChunk> leaderQueue,
        DateTime runStartedAt,
        int runStartRemaining,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var chunkRepo = scope.ServiceProvider.GetRequiredService<IActSectionChunkRepository>();

        while (!ct.IsCancellationRequested)
        {
            PackedBatch? packed = null;
            try
            {
                packed = PackBatchFromQueue(leaderQueue);
                if (packed == null)
                {
                    return; // queue drained — worker exits
                }

                if (packed.WorkItems.Count == 0)
                {
                    if (packed.SkippedStale > 0)
                    {
                        Interlocked.Add(ref _totalSkipped, packed.SkippedStale);
                        EmbeddingProgressState.TotalSkipped = _totalSkipped;
                    }
                    continue;
                }

                var batchRows = packed.WorkItems.Count;
                var batchStale = packed.SkippedStale;

                await _quota.WaitAsync(packed.TextsToEmbed.Count, ct);
                var embeddings = await _embeddingService.GetBatchEmbeddingsAsync(packed.TextsToEmbed, ct);
                _quota.RecordSuccess(packed.TextsToEmbed.Count, EstimateTokens(string.Concat(packed.TextsToEmbed)));

                var points = new List<(string vectorId, float[] embedding, Dictionary<string, string> payload)>(packed.WorkItems.Count);
                for (var i = 0; i < packed.WorkItems.Count; i++)
                {
                    var item = packed.WorkItems[i];
                    points.Add((item.VectorId, embeddings[i], item.Payload));
                }

                await _vectorStore.UpsertBatchAsync(points);

                var dbUpdates = packed.WorkItems
                    .Select(w => (w.Chunk.ChunkId, w.VectorId, w.Hash))
                    .ToList();
                await chunkRepo.UpdateBatchEmbeddingInfoAsync(dbUpdates, ct);

                // Success: the batch is consumed; nothing goes back on the queue.
                packed = null;

                var processed = Interlocked.Add(ref _totalProcessed, batchRows);
                Interlocked.Add(ref _totalSkipped, batchStale);
                EmbeddingProgressState.TotalProcessed = processed;
                EmbeddingProgressState.TotalSkipped = _totalSkipped;
                EmbeddingProgressState.LastUpdatedAt = DateTime.UtcNow;
                EmbeddingProgressState.RequestsPerMinuteBudget = _quota.Snapshot().RequestBudgetPerMinute;
                UpdateEta(processed, runStartedAt, runStartRemaining);

                _logger.LogInformation(
                    "EmbeddingBatchJob: Batch embedded {Rows} chunks. Total processed: {Processed}, skipped: {Skipped}.",
                    batchRows, processed, _totalSkipped);

                _consecutiveTransientFailures = 0;
                _quota.ClearFourTwentyNineStreakIfClean();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (GeminiQuotaExhaustedException qex)
            {
                // The batch's chunks go back on the queue for retry after backoff.
                Reenqueue(packed, leaderQueue);

                if (qex.IsPerMinuteQuota)
                {
                    _quota.RecordQuotaExceeded(qex.RetryAfter);
                    var budget = _quota.Snapshot().RequestBudgetPerMinute;
                    var backoff = qex.RetryAfter ?? TimeSpan.FromSeconds(30);
                    EmbeddingProgressState.SetStatus(
                        $"থ্রটলড: প্রতি-মিনিট কোটা শেষ — {Math.Ceiling(backoff.TotalSeconds)} সেকেন্ড ব্যাকঅফ (Google retryDelay), পেসিং বাজেট এখন {budget:F0} req/min",
                        $"Throttled: per-minute quota hit — backing off {Math.Ceiling(backoff.TotalSeconds)}s (Google retryDelay), pacing budget now {budget:F0} req/min");
                    _logger.LogWarning(
                        "EmbeddingBatchJob: Per-minute quota exhausted (429). Backing off {Seconds:F0}s per RetryInfo, halving pacing budget to {Budget:F0} req/min.",
                        backoff.TotalSeconds, budget);
                    await Task.Delay(backoff, ct);
                }
                else
                {
                    // Daily quota: sleep until the midnight-Pacific reset, then
                    // resume automatically — the free tier resets while this
                    // machine idles overnight, so chunks embed unattended.
                    var resetUtc = NextPacificMidnightUtc();
                    var localReset = TimeZoneInfo.ConvertTimeFromUtc(resetUtc, TimeZoneInfo.Local);
                    EmbeddingProgressState.SetStatus(
                        $"স্থগিত: সব কী-তে দৈনিক কোটা শেষ — স্থানীয় {localReset:HH:mm} এ স্বয়ংক্রিয়ভাবে পুনরায় চালু হবে (প্যাসিফিক মধ্যরাত রিসেট)",
                        $"Paused: Gemini DAILY quota exhausted across all keys — auto-resumes at {localReset:HH:mm} local (Pacific midnight reset)");
                    EmbeddingProgressState.EstimatedCompletion = $"স্বয়ংক্রিয় পুনরায় চালু {localReset:HH:mm}";
                    EmbeddingProgressState.EstimatedCompletionEn = $"Auto-resume {localReset:HH:mm} local";

                    _logger.LogWarning(
                        "EmbeddingBatchJob: Daily quota exhausted (429, PerDay quotaId). Sleeping until midnight-Pacific reset ({ResetUtc:u} UTC) then resuming.",
                        resetUtc);

                    while (DateTime.UtcNow < resetUtc)
                    {
                        // Cancellable sleep in 30s slices so shutdown stays snappy
                        // and the status timestamp stays fresh for the dashboard.
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        EmbeddingProgressState.LastUpdatedAt = DateTime.UtcNow;
                    }

                    _quota.ResetForNewDay();
                    EmbeddingProgressState.SetStatus("দৈনিক কোটা রিসেটের পর পুনরায় চালু", "Resumed after daily quota reset");
                    _logger.LogInformation("EmbeddingBatchJob: Daily quota window reset — resuming.");
                }
            }
            catch (GeminiApiException gex) when (gex.StatusCode is >= 400 and < 500 and not 408 and not 429)
            {
                // Permanent client error (400 INVALID_ARGUMENT, 404, 422...):
                // retrying the SAME request body can never succeed — no backoff,
                // no re-enqueue. Drop the batch so the job can continue; the rows
                // stay VectorId=NULL for investigation/next startup. This is a bug
                // signal (bad request shape, oversized batch, unsupported param),
                // not a transient failure.
                Interlocked.Add(ref _totalSkipped, packed?.WorkItems.Count ?? 0);
                EmbeddingProgressState.TotalSkipped = _totalSkipped;
                EmbeddingProgressState.SetStatus($"স্থায়ী ত্রুটি — ব্যাচ বাদ: {gex.Message}", $"Permanent error — batch dropped: {gex.Message}");
                _logger.LogError(
                    gex,
                    "EmbeddingBatchJob: Permanent {Status} from Gemini — dropping {Rows} chunk rows WITHOUT retry (fix the request; rows stay un-embedded).",
                    gex.StatusCode, packed?.WorkItems.Count ?? 0);
                // Do NOT return: the worker keeps draining other batches.
            }
            catch (Exception ex)
            {
                _consecutiveTransientFailures++;
                var backoffSeconds = Math.Min(5 * Math.Pow(2, _consecutiveTransientFailures - 1), 120);
                EmbeddingProgressState.SetStatus($"ত্রুটির পর পুনরায় চেষ্টা ({_consecutiveTransientFailures}x): {ex.Message}", $"Retrying after error ({_consecutiveTransientFailures}x): {ex.Message}");
                _logger.LogWarning(ex, "EmbeddingBatchJob: Transient error in background batch processing. Backing off {Seconds:F0}s (attempt {Attempt}).", backoffSeconds, _consecutiveTransientFailures);

                if (_consecutiveTransientFailures >= MaxConsecutiveTransientFailures)
                {
                    // Bounded retry: a persistently failing batch (poisoned rows,
                    // dead SQL Server) must not re-enqueue forever — the worker only
                    // exits when the queue drains, so drop the batch, count its rows
                    // as skipped, and let the job terminate. The rows stay
                    // VectorId=NULL and are picked up by the next app restart.
                    Interlocked.Add(ref _totalSkipped, packed?.WorkItems.Count ?? 0);
                    EmbeddingProgressState.TotalSkipped = _totalSkipped;
                    _logger.LogError(
                        "EmbeddingBatchJob: Batch failed {Failures}x consecutively — dropping {Rows} chunk rows to let the job finish. They remain un-embedded and will be retried on next startup.",
                        _consecutiveTransientFailures, packed?.WorkItems.Count ?? 0);
                    return;
                }

                Reenqueue(packed, leaderQueue);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            }
        }
    }

    private static void Reenqueue(PackedBatch? packed, ConcurrentQueue<ActSectionChunk> leaderQueue)
    {
        if (packed == null)
        {
            return;
        }

        // Items that were embedded but failed upsert/stamp will be re-embedded on
        // retry — same trade-off as the pre-queue design; correctness over quota.
        foreach (var item in packed.WorkItems)
        {
            leaderQueue.Enqueue(item.Chunk);
        }
    }

    /// <summary>
    /// Packs up to one request's worth of work items from the shared leader queue.
    /// Rows whose hash + vector are already current are skipped, not re-sent.
    /// Returns null when the queue is drained.
    /// </summary>
    private PackedBatch? PackBatchFromQueue(ConcurrentQueue<ActSectionChunk> leaderQueue)
    {
        var skippedStale = 0;
        var workItems = new List<WorkItem>(_maxTextsPerRequest);
        // Token budget: the per-request ceiling, further capped by what's left
        // in the quota tracker's rolling token window (FIX-EMB-5: this gate
        // existed but was never wired in — MaxTokensPerMinute was dead config).
        // Floor at 1 so a tight window shrinks the batch instead of stalling it.
        var tokenBudget = Math.Min(_maxTokensPerRequest, Math.Max(1, _quota.GetRemainingTokenBudget()));
        var packedTokens = 0;

        while (workItems.Count < _maxTextsPerRequest && leaderQueue.TryDequeue(out var chunk))
        {
            var hash = ComputeSha256(chunk.ChunkText);

            if (chunk.ContentHash == hash && chunk.VectorId != null)
            {
                skippedStale++;
                continue;
            }

            var chunkTokens = EstimateTokens(chunk.ChunkText);
            if (workItems.Count > 0 && packedTokens + chunkTokens > tokenBudget)
            {
                // Won't fit — put it back for the next batch. Re-enqueueing at the
                // tail is fine: embedding order doesn't matter.
                leaderQueue.Enqueue(chunk);
                break;
            }

            workItems.Add(new WorkItem(
                chunk,
                hash,
                chunk.VectorId ?? Guid.NewGuid().ToString(),
                new Dictionary<string, string>
                {
                    ["ChunkId"] = chunk.ChunkId.ToString(),
                    ["SectionId"] = chunk.SectionId.ToString(),
                    ["ChunkOrder"] = chunk.ChunkOrder.ToString(),
                }));
            packedTokens += chunkTokens;
        }

        if (workItems.Count == 0 && leaderQueue.IsEmpty && skippedStale == 0)
        {
            return null;
        }

        return new PackedBatch(
            workItems,
            workItems.Select(w => w.Chunk.ChunkText).ToList(),
            skippedStale);
    }

    // ---- Global dedupe plan ----

    private sealed record GlobalDedupePlan(
        List<ActSectionChunk> Leaders,
        List<(int ChunkId, string Hash)> FollowerRows,
        Dictionary<string, string> VectorIdByHash);

    /// <summary>
    /// Scans ALL un-embedded rows once (keyset-paged, constant memory), computes
    /// their content hashes, and splits them into: Leaders (first row of each
    /// distinct text — these get embedded) and FollowerRows (duplicates — stamped
    /// with the leader's vector id after the leader is embedded, never sent to the
    /// API). The leader's shared VectorId is assigned HERE so the packer, the
    /// Qdrant upsert and the follower stamping all agree on one id per text.
    /// </summary>
    private async Task<GlobalDedupePlan> BuildGlobalDedupeAsync(CancellationToken ct)
    {
        var leaders = new List<ActSectionChunk>();
        var followerRows = new List<(int ChunkId, string Hash)>();
        var vectorIdByHash = new Dictionary<string, string>();
        var seenHashes = new HashSet<string>();

        using var scope = _scopeFactory.CreateScope();
        var chunkRepo = scope.ServiceProvider.GetRequiredService<IActSectionChunkRepository>();

        // Stream in pages ordered by ChunkId, so the leader of each duplicate
        // group is deterministic (lowest ChunkId). An EMPTY page ends the scan —
        // not a partial page, so rows inserted mid-scan are still picked up.
        // Each page read is retried with backoff (bounded): on this dev box SQL
        // Express intermittently times out under RAM pressure (FIX-SQL-2), and a
        // single timeout at page 10 of 15 must not kill the whole job.
        const int pageSize = 2000;
        int lastSeenId = 0;
        List<ActSectionChunk> page;
        do
        {
            page = await ReadDedupePageWithRetryAsync(chunkRepo, lastSeenId, pageSize, ct);
            foreach (var chunk in page)
            {
                lastSeenId = chunk.ChunkId;
                var hash = ComputeSha256(chunk.ChunkText);
                if (seenHashes.Add(hash))
                {
                    chunk.VectorId = chunk.VectorId ?? Guid.NewGuid().ToString();
                    vectorIdByHash[hash] = chunk.VectorId;
                    leaders.Add(chunk);
                }
                else
                {
                    followerRows.Add((chunk.ChunkId, hash));
                }
            }
        }
        while (page.Count > 0);

        return new GlobalDedupePlan(leaders, followerRows, vectorIdByHash);
    }

    /// <summary>
    /// One dedupe page read with bounded retry: transient SQL failures
    /// (timeouts under RAM pressure, deadlocks) back off and try again; a
    /// permanent failure after MaxConsecutiveTransientFailures attempts throws,
    /// which fails the job cleanly (nothing has been written yet at scan time,
    /// so a restart is always safe).
    /// </summary>
    private async Task<List<ActSectionChunk>> ReadDedupePageWithRetryAsync(
        IActSectionChunkRepository chunkRepo,
        int afterChunkId,
        int pageSize,
        CancellationToken ct)
    {
        const string phase = "global dedupe scan";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return (await chunkRepo.GetUnembeddedChunksAfterAsync(afterChunkId, pageSize, ct)).ToList();
            }
            catch (Exception ex) when (attempt < MaxConsecutiveTransientFailures && ex is not OperationCanceledException)
            {
                var backoffSeconds = Math.Min(5 * Math.Pow(2, attempt - 1), 120);
                EmbeddingProgressState.SetStatus(
                    $"ডেডুপ স্ক্যানে ত্রুটি ({attempt}x): {ex.Message} — পুনরায় চেষ্টা হচ্ছে",
                    $"Dedupe scan error ({attempt}x): {ex.Message} — retrying");
                _logger.LogWarning(ex,
                    "EmbeddingBatchJob: Transient error during {Phase} (page after ChunkId {AfterId}), attempt {Attempt}/{Max}. Backing off {Seconds:F0}s.",
                    phase, afterChunkId, attempt, MaxConsecutiveTransientFailures, backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            }
        }
    }

    /// <summary>
    /// Stamps every duplicate (follower) row with the leader's VectorId + hash so
    /// SQL stays consistent with the shared Qdrant point. Batched, no API calls.
    /// Each update batch is retried with backoff (bounded): SQL timeouts under
    /// RAM pressure must not kill the run at the last step.
    /// </summary>
    private async Task StampFollowersWithRetryAsync(
        IActSectionChunkRepository chunkRepo,
        GlobalDedupePlan plan,
        CancellationToken ct)
    {
        foreach (var batch in plan.FollowerRows.Chunk(250))
        {
            var updates = batch
                .Where(f => plan.VectorIdByHash.ContainsKey(f.Hash))
                .Select(f => (f.ChunkId, plan.VectorIdByHash[f.Hash], f.Hash))
                .ToList();
            if (updates.Count > 0)
            {
                await WriteWithRetryAsync(
                    () => chunkRepo.UpdateBatchEmbeddingInfoAsync(updates, ct),
                    phase: "follower stamping",
                    ct);
                Interlocked.Add(ref _totalProcessed, updates.Count);
                EmbeddingProgressState.TotalProcessed = _totalProcessed;
            }
        }

        _logger.LogInformation(
            "EmbeddingBatchJob: Stamped {Followers} duplicate rows with shared vectors (zero extra API calls).",
            plan.FollowerRows.Count);
    }

    /// <summary>
    /// Bounded-retry wrapper for one-off reads around the worker loop (count,
    /// follower page). Falls back to <paramref name="fallback"/> when SQL stays
    /// dead — the ETA prefers honest data but a fatal job helps nobody.
    /// </summary>
    private async Task<int> ReadWithRetryAsync(
        Func<Task<int>> read,
        string phase,
        int fallback,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await read();
            }
            catch (Exception ex) when (attempt < MaxConsecutiveTransientFailures && ex is not OperationCanceledException)
            {
                var backoffSeconds = Math.Min(5 * Math.Pow(2, attempt - 1), 120);
                EmbeddingProgressState.SetStatus(
                    $"{phase} ত্রুটি ({attempt}x): {ex.Message} — পুনরায় চেষ্টা হচ্ছে",
                    $"{phase} error ({attempt}x): {ex.Message} — retrying");
                _logger.LogWarning(ex,
                    "EmbeddingBatchJob: Transient error during {Phase}, attempt {Attempt}/{Max}. Backing off {Seconds:F0}s.",
                    phase, attempt, MaxConsecutiveTransientFailures, backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            }
            catch (Exception ex) when (attempt >= MaxConsecutiveTransientFailures && ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "EmbeddingBatchJob: {Phase} failed {Max}x — falling back to estimate {Fallback}.",
                    phase, MaxConsecutiveTransientFailures, fallback);
                return fallback;
            }
        }
    }

    /// <summary>
    /// Bounded-retry wrapper for one-off writes around the worker loop (follower
    /// stamping pages). Throws after MaxConsecutiveTransientFailures attempts —
    /// callers that reach this point have already burned the API quota for the
    /// leaders, so the safest failure is a clean job error + restart.
    /// </summary>
    private async Task WriteWithRetryAsync(Func<Task> write, string phase, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await write();
                return;
            }
            catch (Exception ex) when (attempt < MaxConsecutiveTransientFailures && ex is not OperationCanceledException)
            {
                var backoffSeconds = Math.Min(5 * Math.Pow(2, attempt - 1), 120);
                EmbeddingProgressState.SetStatus(
                    $"{phase} ত্রুটি ({attempt}x): {ex.Message} — পুনরায় চেষ্টা হচ্ছে",
                    $"{phase} error ({attempt}x): {ex.Message} — retrying");
                _logger.LogWarning(ex,
                    "EmbeddingBatchJob: Transient error during {Phase}, attempt {Attempt}/{Max}. Backing off {Seconds:F0}s.",
                    phase, attempt, MaxConsecutiveTransientFailures, backoffSeconds);
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            }
        }
    }

    private int EstimateTokens(string text) => Math.Max(1, text.Length / _charsPerToken);

    private static void UpdateEta(int processed, DateTime startedAt, int runStartRemaining)
    {
        if (processed < 20 || runStartRemaining <= 0)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - startedAt;
        if (elapsed.TotalMinutes < 1)
        {
            return;
        }

        var ratePerMinute = processed / elapsed.TotalMinutes;
        if (ratePerMinute <= 0)
        {
            return;
        }

        var remaining = Math.Max(0, runStartRemaining - processed);
        var etaMinutes = remaining / ratePerMinute;
        EmbeddingProgressState.EstimatedCompletionEn =
            etaMinutes > 60 * 24
                ? $"{etaMinutes / 60.0 / 24.0:F1} days"
                : $"{TimeSpan.FromMinutes(etaMinutes):hh\\h\\ mm\\m}";
        EmbeddingProgressState.EstimatedCompletion =
            etaMinutes > 60 * 24
                ? $"{etaMinutes / 60.0 / 24.0:F1} দিন"
                : $"{TimeSpan.FromMinutes(etaMinutes):hh\\h\\ mm\\m}";
    }

    public static string ComputeSha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Next 00:00 America/Los_Angeles expressed in UTC — when Google resets
    /// per-day quotas. Handles DST via a Pacific timezone lookup so the wake
    /// time is right year-round; falls back to a fixed -8h offset if the zone
    /// is unavailable on the host.
    /// </summary>
    private static DateTime NextPacificMidnightUtc()
    {
        var nowUtc = DateTime.UtcNow;

        TimeZoneInfo pacific;
        try
        {
            pacific = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles");
        }
        catch (TimeZoneNotFoundException)
        {
            pacific = TimeZoneInfo.CreateCustomTimeZone("PST-fallback", TimeSpan.FromHours(-8), "PST-fallback", "PST-fallback");
        }

        var nowPacific = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, pacific);
        var nextMidnightPacific = nowPacific.Date.AddDays(1);
        var resetUtc = TimeZoneInfo.ConvertTimeToUtc(nextMidnightPacific, pacific);

        // Guard against pathological zone data — never wait more than ~25h.
        if (resetUtc - nowUtc > TimeSpan.FromHours(25) || resetUtc <= nowUtc)
        {
            resetUtc = nowUtc + TimeSpan.FromHours(8);
        }

        return resetUtc;
    }
}
