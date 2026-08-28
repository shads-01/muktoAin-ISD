using System.Net;
using MuktoAin.Infrastructure.Ai;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace MuktoAin.UnitTests.Services;

public class GeminiResiliencePoliciesTests
{
    [Fact]
    public void Build_WithCustomOptions_BuildsValidResiliencePipeline()
    {
        var options = new GeminiOptions
        {
            RetryCount = 3,
            RetryBaseDelaySeconds = 0.1,
            CircuitBreakerFailureThreshold = 4,
            CircuitBreakerBreakDurationSeconds = 1,
            RequestTimeoutSeconds = 5
        };

        var pipeline = GeminiResiliencePolicies.Build(options);

        Assert.NotNull(pipeline);
    }

    [Fact]
    public async Task Pipeline_ExecutesSuccessfulCall_ReturnsResponse()
    {
        var options = new GeminiOptions
        {
            RetryCount = 2,
            RetryBaseDelaySeconds = 0.01,
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerBreakDurationSeconds = 1,
            RequestTimeoutSeconds = 5
        };

        var pipeline = GeminiResiliencePolicies.Build(options);

        var response = await pipeline.ExecuteAsync(async _ =>
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Pipeline_OnTransient500Error_RetriesSpecifiedTimes()
    {
        var options = new GeminiOptions
        {
            RetryCount = 2,
            RetryBaseDelaySeconds = 0.01,
            CircuitBreakerFailureThreshold = 10,
            CircuitBreakerBreakDurationSeconds = 1,
            RequestTimeoutSeconds = 5
        };

        var pipeline = GeminiResiliencePolicies.Build(options);
        var attempts = 0;

        var response = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            await Task.Yield();
            if (attempts <= 2)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(3, attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
