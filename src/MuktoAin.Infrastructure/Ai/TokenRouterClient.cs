using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces;
using Polly;
using Polly.CircuitBreaker;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// OpenAI-compatible HTTP client for the TokenRouter API (generation only).
/// Uses Bearer token authentication and the /chat/completions endpoint.
/// Embeddings remain on Gemini via GeminiEmbeddingService.
/// </summary>
public class TokenRouterClient : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly TokenRouterOptions _options;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TokenRouterClient(
        IOptions<TokenRouterOptions> options,
        IHttpClientFactory httpClientFactory,
        ResiliencePipeline<HttpResponseMessage> resiliencePipeline)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "No TokenRouter API key configured. Add 'TokenRouter:ApiKey' in appsettings.Development.json.");
        }

        _pipeline = resiliencePipeline;
        _httpClient = httpClientFactory.CreateClient(nameof(TokenRouterClient));
    }

    public async Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)
    {
        var body = new
        {
            model = _options.GenerationModel,
            messages = new[]
            {
                new { role = "user", content = prompt },
            },
        };

        var json = await SendAsync(body, ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    public Task<float[]> EmbedContentAsync(string text, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "TokenRouter does not support embeddings. Use GeminiEmbeddingService (IEmbeddingService) instead.");
    }

    public Task<IReadOnlyList<float[]>> BatchEmbedContentAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "TokenRouter does not support embeddings. Use GeminiEmbeddingService (IEmbeddingService) instead.");
    }

    private async Task<string> SendAsync(object body, CancellationToken ct)
    {
        var uri = new Uri($"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);

        HttpResponseMessage result;
        try
        {
            result = await _pipeline.ExecuteAsync(async token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");
                return await _httpClient.SendAsync(request, token);
            }, ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new InvalidOperationException(
                "TokenRouter API is temporarily unavailable (circuit breaker open). Try again shortly.", ex);
        }

        var content = await result.Content.ReadAsStringAsync(ct);

        if (!result.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"TokenRouter API call failed ({(int)result.StatusCode} {result.StatusCode}). {content}");
        }

        return content;
    }
}
