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
            EmbeddingModel = "text-embedding-004",
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

        var ex = await Assert.ThrowsAsync<GeminiApiException>(() =>
            client.GenerateContentAsync("Test prompt"));

        Assert.Equal(429, ex.StatusCode);
        Assert.Contains("All Gemini API keys are exhausted", ex.Message);
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
}
