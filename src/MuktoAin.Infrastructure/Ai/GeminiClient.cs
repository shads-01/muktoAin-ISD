using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces;
using Polly;
using Polly.CircuitBreaker;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Thin HTTP wrapper around the Gemini REST API with multi-key rotation.
/// 429 (RESOURCE_EXHAUSTED) is handled by rotating to the next API key;
/// transient network errors, 408/5xx, timeout and circuit breaking are handled
/// by the shared Polly pipeline (S-2.6, see GeminiResiliencePolicies).
/// </summary>
public class GeminiClient : IAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    private readonly HttpClient _httpClient;
    private readonly string[] _apiKeys;
    private readonly string _generationModel;
    private readonly string _embeddingModel;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private int _currentKeyIndex;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GeminiClient(
        IOptions<GeminiOptions> options,
        IHttpClientFactory httpClientFactory,
        ResiliencePipeline<HttpResponseMessage> resiliencePipeline)
    {
        var opts = options.Value;
        _apiKeys = opts.ApiKeys ?? [];
        if (_apiKeys.Length == 0)
        {
            throw new InvalidOperationException(
                "No Gemini API keys configured. Add keys under 'Gemini:ApiKeys' in appsettings.Development.json.");
        }

        _generationModel = opts.GenerationModel;
        _embeddingModel = opts.EmbeddingModel;
        _pipeline = resiliencePipeline;
        _httpClient = httpClientFactory.CreateClient(nameof(GeminiClient));
    }

    public async Task<string> GenerateContentAsync(string prompt, CancellationToken ct = default)
    {
        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } },
            },
        };

        var json = await SendAsync(_generationModel, ":generateContent", body, ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    public async Task<float[]> EmbedContentAsync(string text, CancellationToken ct = default)
    {
        var body = new
        {
            content = new { parts = new[] { new { text } } },
        };

        var json = await SendAsync(_embeddingModel, ":embedContent", body, ct);

        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("embedding").GetProperty("values");
        var embedding = new float[values.GetArrayLength()];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = values[i].GetSingle();
        }

        return embedding;
    }

    /// <summary>
    /// Rotates through every configured key on 429 before giving up.
    /// Polly handles transient network/5xx retries; a fresh HttpRequestMessage is built per attempt.
    /// Non-429 client errors (4xx other than 408) fail fast without burning the remaining keys.
    /// </summary>
    private async Task<string> SendAsync(string model, string methodSuffix, object body, CancellationToken ct)
    {
        using var response = await SendWithRotationAsync(model, methodSuffix, body, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new GeminiApiException(
                $"Gemini API call failed ({(int)response.StatusCode} {response.StatusCode}).",
                (int)response.StatusCode,
                content);
        }

        return content;
    }

    private async Task<HttpResponseMessage> SendWithRotationAsync(
        string model, string methodSuffix, object body, CancellationToken ct)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < _apiKeys.Length; attempt++)
        {
            var uri = BuildUri(model + methodSuffix, GetCurrentKey());
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
                    return await _httpClient.SendAsync(request, token);
                }, ct);
            }
            catch (BrokenCircuitException ex)
            {
                // S-2.6: the circuit breaker is open -- fail fast with a clean,
                // user-surfaceable error instead of a raw Polly exception.
                throw new GeminiApiException(
                    "Gemini API is temporarily unavailable (circuit breaker open). Try again shortly.",
                    503, null, ex);
            }

            if ((int)result.StatusCode == 429)
            {
                result.Dispose();
                lastError = new GeminiApiException("Gemini quota exhausted (429).", 429, null);
                RotateKey();
                await Task.Delay(200, ct);
                continue;
            }

            if (result.IsSuccessStatusCode)
            {
                return result;
            }

            if ((int)result.StatusCode < 500 && (int)result.StatusCode != 408)
            {
                // Permanent client error — rotation won't help. Dispose and throw.
                using (result)
                {
                    var errorBody = await result.Content.ReadAsStringAsync(ct);
                    throw new GeminiApiException(
                        $"Gemini API returned {(int)result.StatusCode}.", (int)result.StatusCode, errorBody);
                }
            }

            lastError = new GeminiApiException(
                $"Gemini API returned {(int)result.StatusCode}.", (int)result.StatusCode, null);
            result.Dispose();
        }

        throw new GeminiApiException(
            "All Gemini API keys are exhausted. Wait for quota reset.", 429, null, lastError);
    }

    private Uri BuildUri(string modelPath, string apiKey) =>
        new UriBuilder(BaseUrl + modelPath) { Query = $"key={Uri.EscapeDataString(apiKey)}" }.Uri;

    private string GetCurrentKey()
    {
        lock (_lock)
        {
            return _apiKeys[_currentKeyIndex % _apiKeys.Length];
        }
    }

    private void RotateKey()
    {
        lock (_lock)
        {
            _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Length;
        }
    }
}
