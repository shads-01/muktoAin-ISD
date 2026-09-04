namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Thrown when every configured Gemini key returned 429 RESOURCE_EXHAUSTED.
/// Carries Google's own RetryInfo so callers can back off the exact required
/// time instead of guessing, plus whether the exhausted quota is per-minute
/// (retryable soon) or per-day (stop until midnight Pacific reset).
/// </summary>
public class GeminiQuotaExhaustedException : GeminiApiException
{
    /// <summary>
    /// Google's mandated wait (RetryInfo.retryDelay from the 429 body, e.g. "37s").
    /// Null when the body carried no RetryInfo — caller falls back to its own pacing.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// True when the failing quota was a per-minute quota (RPM/TPM style id,
    /// e.g. "GenerateRequestsPerMinutePerProjectPerModel" or "EmbeddingRequestsPerMinute").
    /// False when it was a daily quota (PerDay) — the job should stop, not spin.
    /// </summary>
    public bool IsPerMinuteQuota { get; }

    public GeminiQuotaExhaustedException(
        string message,
        TimeSpan? retryAfter,
        bool isPerMinuteQuota,
        Exception? inner = null)
        : base(message, 429, null, inner)
    {
        RetryAfter = retryAfter;
        IsPerMinuteQuota = isPerMinuteQuota;
    }
}
