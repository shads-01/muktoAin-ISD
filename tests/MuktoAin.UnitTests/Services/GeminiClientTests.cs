using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using MuktoAin.Infrastructure.Ai;
using Polly;

namespace MuktoAin.UnitTests.Services;

public class GeminiClientTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();

    private GeminiClient CreateClient(
        HttpMessageHandler handler,
        string[]? apiKeys = null,
        ResiliencePipeline<HttpResponseMessage>? pipeline = null)
    {
        var httpClient = new HttpClient(handler);
        _httpClientFactoryMock
            .Setup(f => f.CreateClient(nameof(GeminiClient)))
            .Returns(httpClient);

        var options = Options.Create(new GeminiOptions
        {
            ApiKeys = apiKeys ?? ["key-1", "key-2", "key-3"],
            GenerationModel = "gemini-2.5-flash",
            EmbeddingModel = "gemini-embedding-001",
        });

        var resPipeline = pipeline ?? new ResiliencePipelineBuilder<HttpResponseMessage>().Build();

        return new GeminiClient(options, _httpClientFactoryMock.Object, resPipeline);
    }

    [Fact]
    public async Task GenerateContentAsync_WhenFirstKeyReturns429_RotatesToNextKeyAndSucceeds()
    {
        var attempts = 0;
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    Assert.Contains("key=key-1", req.RequestUri!.Query);
                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent("{\"error\":\"RESOURCE_EXHAUSTED\"}")
                    });
                }

                Assert.Contains("key=key-2", req.RequestUri!.Query);
                var responseJson = JsonSerializer.Serialize(new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[] { new { text = "This is the generated legal advice." } }
                            }
                        }
                    }
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            });

        var client = CreateClient(handlerMock.Object);

        var result = await client.GenerateContentAsync("Explain my rights");

        Assert.Equal("This is the generated legal advice.", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ConsecutiveRequests_StripeRoundRobinAcrossKeys()
    {
        // The core FIX-EMB-3 behavior: with independent quota pools per key,
        // consecutive requests must spread across the ring (key-1, key-2,
        // key-3, key-1, ...) instead of hammering one key until it 429s.
        var usedKeys = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                var key = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["key"];
                usedKeys.Enqueue(key!);

                var responseJson = JsonSerializer.Serialize(new
                {
                    embedding = new { values = new[] { 0.1f } }
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            });

        var client = CreateClient(handlerMock.Object);

        for (var i = 0; i < 6; i++)
        {
            await client.EmbedContentAsync($"query {i}");
        }

        Assert.Equal(
            new[] { "key-1", "key-2", "key-3", "key-1", "key-2", "key-3" },
            usedKeys.ToList());
    }

    [Fact]
    public async Task WhenOneKey429s_OnlyThatKeyIsParked_OthersContinueStriping()
    {
        // A 429 parks ONE key for the retryDelay; the next request goes to the
        // NEXT key immediately (no waiting), and the parked key is skipped
        // until its cooldown elapses — the other keys keep serving.
        var key1Call = 0;
        var usedKeys = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                var key = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["key"];
                usedKeys.Enqueue(key!);

                // key-1 429s on its FIRST use with a 2s RetryInfo, then succeeds.
                if (key == "key-1" && key1Call++ == 0)
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        error = new
                        {
                            code = 429,
                            details = new object[]
                            {
                                new { @__type = "type.googleapis.com/google.rpc.RetryInfo", retryDelay = "2s" }
                            }
                        }
                    }).Replace("__type", "@type");

                    return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    });
                }

                var responseJson = JsonSerializer.Serialize(new
                {
                    embedding = new { values = new[] { 0.1f } }
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                });
            });

        var client = CreateClient(handlerMock.Object);

        // Request 1: key-1 (ring start) -> 429 -> parked; key-2 succeeds.
        await client.EmbedContentAsync("query 1");
        // Request 2: key-3 (ring advanced past parked key-1).
        await client.EmbedContentAsync("query 2");
        // Request 3: key-2 again (key-1 still parked, key-3 just used)...
        await client.EmbedContentAsync("query 3");
        // ...after key-1's 2s cooldown elapses it's back in rotation: request 4
        // takes key-3 (ring position), request 5 takes the unparked key-1.
        await Task.Delay(2500);
        await client.EmbedContentAsync("query 4");
        await client.EmbedContentAsync("query 5");

        var keys = usedKeys.ToList();

        // First request: key-1 attempted (429), then key-2 succeeded.
        Assert.Equal("key-1", keys[0]);
        Assert.Equal("key-2", keys[1]);
        // While key-1 was parked, subsequent requests only used key-2/key-3.
        Assert.Equal("key-3", keys[2]);
        Assert.Equal("key-2", keys[3]);
        // After the cooldown, key-1 is servable again.
        Assert.Equal("key-3", keys[4]);
        Assert.Equal("key-1", keys[5]);
        // key-1 was hit exactly twice: initial 429 + post-cooldown success.
        Assert.Equal(2, keys.Count(k => k == "key-1"));
    }

    [Fact]
    public async Task WhenAllKeys429_ThrowsQuotaExhausted_AndRetryAfterReflectsSoonestPark()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                var body = JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        code = 429,
                        details = new object[]
                        {
                            new { @__type = "type.googleapis.com/google.rpc.RetryInfo", retryDelay = "37s" }
                        }
                    }
                }).Replace("__type", "@type");

                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            });

        var client = CreateClient(handlerMock.Object, apiKeys: ["key-1", "key-2"]);

        var ex = await Assert.ThrowsAsync<GeminiQuotaExhaustedException>(() =>
            client.EmbedContentAsync("Test query"));

        Assert.IsAssignableFrom<GeminiApiException>(ex);
        Assert.Equal(429, ex.StatusCode);
        // RetryAfter is derived from the soonest parkedUntil across keys, which
        // for a 37s retryDelay on all keys is <= 37s (parked staggered as each
        // key was tried, not all at once).
        Assert.NotNull(ex.RetryAfter);
        Assert.True(ex.RetryAfter!.Value <= TimeSpan.FromSeconds(37));
        Assert.True(ex.RetryAfter!.Value > TimeSpan.Zero);
    }

    [Fact]
    public async Task GenerateContentAsync_WhenAllKeysReturn429_ThrowsGeminiApiExceptionWithExhaustionMessage()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"error\":\"RESOURCE_EXHAUSTED\"}")
            });

        var client = CreateClient(handlerMock.Object, apiKeys: ["key-1", "key-2"]);

        var ex = await Assert.ThrowsAsync<GeminiQuotaExhaustedException>(() =>
            client.GenerateContentAsync("Test prompt"));

        // GeminiQuotaExhaustedException : GeminiApiException — the richer type
        // carries RetryInfo (RetryAfter) + quota scope for the batch job's
        // adaptive backoff; the base contract still holds for web callers.
        Assert.IsAssignableFrom<GeminiApiException>(ex);
        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("All Gemini API keys are", ex.Message);
    }

    [Fact]
    public async Task BatchEmbedContentAsync_WithMoreThan100Texts_ThrowsImmediatelyWithoutHttpCall()
    {
        // Regression: the API hard-caps batchEmbedContents at 100 requests per
        // batch (400 INVALID_ARGUMENT above it) — the client must refuse
        // locally instead of sending a request destined to fail.
        var handlerMock = new Mock<HttpMessageHandler>();
        var client = CreateClient(handlerMock.Object);

        var texts = Enumerable.Range(0, 101).Select(i => $"Chunk {i}").ToList();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.BatchEmbedContentAsync(texts));

        Assert.Contains("at most 100", ex.Message);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GenerateContentAsync_WhenNon429ClientErrorOccurs_FailsFastWithoutRotatingKeys()
    {
        var attempts = 0;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"INVALID_ARGUMENT\"}")
                });
            });

        var client = CreateClient(handlerMock.Object, apiKeys: ["key-1", "key-2", "key-3"]);

        var ex = await Assert.ThrowsAsync<GeminiApiException>(() =>
            client.GenerateContentAsync("Invalid prompt"));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task EmbedContentAsync_WhenSuccessful_ReturnsParsedFloatArray()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var responseJson = JsonSerializer.Serialize(new
        {
            embedding = new
            {
                values = new[] { 0.123f, -0.456f, 0.789f }
            }
        });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handlerMock.Object);

        var result = await client.EmbedContentAsync("Sample query text");

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(0.123f, result[0], precision: 3);
        Assert.Equal(-0.456f, result[1], precision: 3);
        Assert.Equal(0.789f, result[2], precision: 3);
    }

    [Fact]
    public async Task BatchEmbedContentAsync_WhenSuccessful_ReturnsParsedFloatArrays()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var responseJson = JsonSerializer.Serialize(new
        {
            embeddings = new[]
            {
                new { values = new[] { 0.1f, 0.2f } },
                new { values = new[] { 0.3f, 0.4f } }
            }
        });

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handlerMock.Object);

        var result = await client.BatchEmbedContentAsync(new[] { "Chunk 1", "Chunk 2" });

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(0.1f, result[0][0], precision: 2);
        Assert.Equal(0.2f, result[0][1], precision: 2);
        Assert.Equal(0.3f, result[1][0], precision: 2);
        Assert.Equal(0.4f, result[1][1], precision: 2);
    }
}
