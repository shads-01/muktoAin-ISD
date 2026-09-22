using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Resilience pipeline for outbound TokenRouter HTTP calls.
/// Same composition as GeminiResiliencePolicies: timeout -> circuit breaker -> retry.
/// </summary>
public static class TokenRouterResiliencePolicies
{
    private static readonly PredicateBuilder<HttpResponseMessage> TransientFault =
        new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>()
            .HandleResult(response =>
                !response.IsSuccessStatusCode
                && ((int)response.StatusCode == 408 || (int)response.StatusCode >= 500));

    public static ResiliencePipeline<HttpResponseMessage> Build(TokenRouterOptions options)
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
