namespace MuktoAin.Infrastructure.Ai;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string[] ApiKeys { get; set; } = [];

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    public string GenerationModel { get; set; } = "gemini-2.0-flash";

    // S-2.6 resilience knobs (see GeminiResiliencePolicies)
    public int RetryCount { get; set; } = 3;

    public double RetryBaseDelaySeconds { get; set; } = 1;

    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    public double CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    public double RequestTimeoutSeconds { get; set; } = 60;

    // ---- Embedding batching knobs (free-tier quota survival) ----

    // Per batchEmbedContents request the TOTAL token count of all texts is
    // billed against the quota, so the number of chunks that fits in one
    // request depends on chunk sizes. The job packs chunks up to this token
    // ceiling, subject to the 100-texts-per-request API cap above.
    public int EmbeddingBatchMaxTokens { get; set; } = 25000;

    // Hard cap on texts per batchEmbedContents request. The API itself rejects
    // more than 100 requests per batch with 400 INVALID_ARGUMENT ("at most 100
    // requests can be in one batch" — verified live), so this must stay <= 100;
    // EmbeddingBatchJob clamps misconfigured values at startup. FIX-EMB-5:
    // Google counts EACH TEXT as one quota request, so this is also the
    // per-batch share of one project's 100/min free-tier budget — 25 = 25%.
    public int EmbeddingBatchMaxTexts { get; set; } = 25;

    // Token-count heuristic for mixed Bangla/English legal text: chars-per-token.
    // Used to pack batches and estimate quota headroom without shipping a tokenizer.
    // Conservative on purpose: Bangla runs denser than English, and UNDER-estimating
    // tokens causes TPM overshoot (guaranteed 429s), while over-estimating only
    // shrinks batches slightly.
    public int EmbeddingCharsPerToken { get; set; } = 3;

    // MRL output dimensionality for gemini-embedding-001 (null = model default 3072).
    // 768 is 4x smaller responses/upserts at ~1-2% retrieval quality loss. If output
    // tokens count toward the embedding TPM quota, this is also a 4x quota win.
    // MUST match Qdrant:VectorSize in appsettings.
    public int? EmbeddingOutputDimensionality { get; set; }

    // Admin dashboard key-usage tracker: tokens actually spent by GENERATION
    // calls (generateContent) in the trailing 60 seconds, checked against
    // Gemini's free-tier TPM cap — one fixed quota assumed for EVERY configured
    // key (all keys are free-tier AI Studio projects today — see GeminiClient's
    // per-key striping comment). Default 250,000 verified 2026-09-09 against
    // AI Studio's own quota page for gemini-3.6-flash; Google doesn't guarantee
    // published/observed limits stay fixed, so re-check per-project via
    // aistudio.google.com > Usage and billing > Rate limits if this drifts.
    // Embedding calls are NOT counted — batchEmbedContents/embedContent don't
    // return usageMetadata — so this undercounts true per-project TPM since
    // embeddings are most of this app's traffic (see FIX-EMB-5). Purely a
    // display budget for GeminiClient.Snapshot() — does not affect
    // striping/parking behavior or gate real requests.
    public int GenerationTokenLimitPerMinute { get; set; } = 250_000;
}
