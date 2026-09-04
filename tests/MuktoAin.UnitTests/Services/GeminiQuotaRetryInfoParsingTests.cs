using System.Text.Json;
using System.Text.Json.Nodes;
using MuktoAin.Infrastructure.Ai;

namespace MuktoAin.UnitTests.Services;

public class GeminiQuotaRetryInfoParsingTests
{
    // C# anonymous types can't emit a literal "@type" JSON property (the @ is an
    // identifier escape), so payloads are built as JsonObject with exact names.
    private static GeminiApiException Make429(object? errorPayload)
    {
        var body = errorPayload is null
            ? JsonSerializer.Serialize(new { })
            : JsonSerializer.Serialize(errorPayload);
        return new GeminiApiException("Gemini key error (429).", 429, body);
    }

    private static JsonObject RetryInfo(string retryDelay) => new()
    {
        ["@type"] = "type.googleapis.com/google.rpc.RetryInfo",
        ["retryDelay"] = retryDelay
    };

    private static JsonObject QuotaFailure(params string[] quotaIds) => new()
    {
        ["@type"] = "type.googleapis.com/google.rpc.QuotaFailure",
        ["violations"] = new JsonArray(
            quotaIds.Select(id => new JsonObject { ["quotaId"] = id }).ToArray())
    };

    private static JsonObject ErrorBody(params JsonNode[] details) => new()
    {
        ["code"] = 429,
        ["details"] = new JsonArray(details)
    };

    [Fact]
    public void ParseQuotaRetryInfo_WithRetryDelayAndPerMinuteQuota_ParsesBoth()
    {
        var ex = Make429(new JsonObject
        {
            ["error"] = ErrorBody(
                RetryInfo("37s"),
                QuotaFailure("EmbeddingRequestsPerMinutePerProjectPerModel"))
        });

        var (retryAfter, isPerMinute) = GeminiClient.ParseQuotaRetryInfo(ex);

        Assert.Equal(TimeSpan.FromSeconds(37), retryAfter);
        Assert.True(isPerMinute);
    }

    [Fact]
    public void ParseQuotaRetryInfo_WithPerDayQuotaId_ReportsDailyScope()
    {
        var ex = Make429(new JsonObject
        {
            ["error"] = ErrorBody(QuotaFailure("GenerateRequestsPerDayPerProjectPerModel"))
        });

        var (retryAfter, isPerMinute) = GeminiClient.ParseQuotaRetryInfo(ex);

        Assert.False(isPerMinute);
        Assert.Null(retryAfter);
    }

    [Fact]
    public void ParseQuotaRetryInfo_WithMalformedBody_FallsBackToDefaults()
    {
        var ex = new GeminiApiException("Gemini key error (429).", 429, "not-json");

        var (retryAfter, isPerMinute) = GeminiClient.ParseQuotaRetryInfo(ex);

        Assert.Null(retryAfter);
        Assert.True(isPerMinute);
    }

    [Fact]
    public void ParseQuotaRetryInfo_WithNoBody_FallsBackToDefaults()
    {
        var ex = new GeminiApiException("Gemini key error (429).", 429, null);

        var (retryAfter, isPerMinute) = GeminiClient.ParseQuotaRetryInfo(ex);

        Assert.Null(retryAfter);
        Assert.True(isPerMinute);
    }

    [Fact]
    public void ParseQuotaRetryInfo_WithNonGeminiException_FallsBackToDefaults()
    {
        var (retryAfter, isPerMinute) = GeminiClient.ParseQuotaRetryInfo(new Exception("unrelated"));

        Assert.Null(retryAfter);
        Assert.True(isPerMinute);
    }

    [Fact]
    public void ParseQuotaRetryInfo_FractionalRetryDelay_RoundsUpToOneSecondMinimum()
    {
        var ex = Make429(new JsonObject
        {
            ["error"] = ErrorBody(RetryInfo("0.5s"))
        });

        var (retryAfter, _) = GeminiClient.ParseQuotaRetryInfo(ex);

        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);
    }
}
