using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// S-2.6: shared Polly v8 resilience pipeline for outbound Gemini HTTP calls
/// (FR-10). Composition order is outermost-first:
///
///   1. Timeout        -- bounds every attempt (hung sockets must not pin threads).
///   2. Circuit breaker -- when Gemini keeps failing, fail fast instead of burning
///      the retry budget and quota on a dead endpoint.
///   3. Retry          -- transient faults only (network errors, 408, 5xx).
///
/// NOT handled here (deliberately): 429 RESOURCE_EXHAUSTED and key rotation live
/// in GeminiClient.SendWithRotationAsync -- rotation is per-key business logic,
/// not a transient-fault concern. The final fallback (all keys exhausted) also
/// surfaces from GeminiClient as GeminiApiException with a clear message.
/// </summary>
public static class GeminiResiliencePolicies
{
    // NOTE: BrokenCircuitException is intentionally NOT handled anywhere here.
    // The breaker sits outside the retry stage, so an open circuit short-circuits
    // the whole call before the retry loop runs -- no wasted attempts, and the
    // breaker must never count its own exceptions as new failures.
    private static readonly PredicateBuilder<HttpResponseMessage> TransientFault =
        new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .HandleResult(response =>
                !response.IsSuccessStatusCode
                && ((int)response.StatusCode == 408 || (int)response.StatusCode >= 500));

    public static ResiliencePipeline<HttpResponseMessage> Build(GeminiOptions options)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.8,
                MinimumThroughput = options.CircuitBreakerFailureThreshold,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = TransientFault,
            })
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                ShouldHandle = TransientFault,
            })
            .Build();
    }
}
