using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces;
using Polly;
using Polly.CircuitBreaker;

namespace MuktoAin.Infrastructure.Ai;

/// <summary>
/// Thin HTTP wrapper around the Gemini REST API with round-robin multi-key striping.
///
/// Key strategy (FIX-EMB-3): each API key lives in its OWN Google Cloud project
/// (default AI Studio project per Google account = one independent free-tier
/// quota pool per key). To turn 7 pools into ~7x aggregate throughput, requests
/// are STRIPED round-robin across keys — key index advances on every request,
/// not on failure — so all pools spend at the same rate. On 429, only the
/// throttled key is parked for Google's RetryInfo.retryDelay (or a fallback
/// backoff); the very next key in the ring serves immediately. The job-level
/// EmbeddingQuotaState AIMD pacing still bounds the TOTAL rate, so striping
/// never exceeds the aggregate budget.
///
/// Transient network errors, 408/5xx, timeout and circuit breaking are handled
/// by the shared Polly pipeline (S-2.6, see GeminiResiliencePolicies).
/// </summary>
public class GeminiClient : IAiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";

    // Hard API limit: batchEmbedContents rejects more than 100 requests per
    // batch with 400 INVALID_ARGUMENT ("at most 100 requests can be in one
    // batch" — verified against the live API).
    internal const int MaxTextsPerBatchRequest = 100;

    private readonly HttpClient _httpClient;
    private readonly string[] _apiKeys;
    private readonly string _generationModel;
    private readonly string _embeddingModel;
    private readonly int? _outputDimensionality;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    // Round-robin ring position — advances on EVERY request so quota spend is
    // striped evenly across independent key pools.
    private int _keyIndex;

    // Per-key cooldown: parkedUntil[i] > UtcNow means key i is throttled and is
    // skipped during key selection. A 429 parks ONE key, never the whole ring.
    private readonly DateTime[] _parkedUntil;

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
        _outputDimensionality = opts.EmbeddingOutputDimensionality;
        _pipeline = resiliencePipeline;
        _parkedUntil = new DateTime[_apiKeys.Length];
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
        // taskType matters: documents are indexed with RETRIEVAL_DOCUMENT and
        // user queries MUST use RETRIEVAL_QUERY, or retrieval quality measurably
        // drops (the model optimizes each task type differently).
        var body = new Dictionary<string, object?>
        {
            ["content"] = new { parts = new[] { new { text } } },
            ["taskType"] = "RETRIEVAL_QUERY"
        };
        if (_outputDimensionality is { } dims)
        {
            // MRL truncation: gemini-embedding-001 natively supports 768/1536/3072.
            body["outputDimensionality"] = dims;
        }

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

    public async Task<IReadOnlyList<float[]>> BatchEmbedContentAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        if (texts.Count > MaxTextsPerBatchRequest)
        {
            // API hard cap: "at most 100 requests can be in one batch" (verified
            // against the live API — a larger request fails 400 INVALID_ARGUMENT,
            // and this class can't see the caller's packing config).
            throw new ArgumentOutOfRangeException(
                nameof(texts),
                $"batchEmbedContents accepts at most {MaxTextsPerBatchRequest} texts per request; got {texts.Count}. " +
                "Lower Embedding:BatchMaxTexts accordingly.");
        }

        var modelResourceName = _embeddingModel.StartsWith("models/") ? _embeddingModel : $"models/{_embeddingModel}";
        var body = new Dictionary<string, object?>
        {
            ["requests"] = texts.Select(t =>
            {
                var request = new Dictionary<string, object?>
                {
                    ["model"] = modelResourceName,
                    ["content"] = new { parts = new[] { new { text = t } } },
                    ["taskType"] = "RETRIEVAL_DOCUMENT"
                };
                if (_outputDimensionality is { } dims)
                {
                    request["outputDimensionality"] = dims;
                }
                return (object)request;
            }).ToArray()
        };

        var json = await SendAsync(_embeddingModel, ":batchEmbedContents", body, ct);

        using var doc = JsonDocument.Parse(json);
        var embeddingsElement = doc.RootElement.GetProperty("embeddings");
        var result = new List<float[]>(embeddingsElement.GetArrayLength());

        foreach (var item in embeddingsElement.EnumerateArray())
        {
            var values = item.GetProperty("values");
            var embedding = new float[values.GetArrayLength()];
            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] = values[i].GetSingle();
            }
            result.Add(embedding);
        }

        return result;
    }

    /// <summary>
    /// Sends with round-robin key striping. Key selection advances per REQUEST;
    /// a 429 parks only the throttled key and the next key in the ring is tried
    /// immediately. Fails with GeminiQuotaExhaustedException only when EVERY key
    /// is parked/throttled. Polly handles transient network/5xx retries; a fresh
    /// HttpRequestMessage is built per attempt. Non-429 client errors (4xx other
    /// than 408) fail fast — the request body is at fault, not the key.
    /// </summary>
    private async Task<string> SendAsync(string model, string methodSuffix, object body, CancellationToken ct)
    {
        var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
        Exception? lastError = null;

        // At most one pass per key: every key gets exactly one try (the ring
        // advances per attempt), then the loop stops when it comes back around.
        var firstKey = NextKeyOrNull();
        if (firstKey == null)
        {
            throw BuildQuotaException(lastError);
        }

        var firstKeyIndex = firstKey.Value;

        for (var attempt = 0; attempt < _apiKeys.Length; attempt++)
        {
            var keyIndex = (firstKeyIndex + attempt) % _apiKeys.Length;
            if (IsParked(keyIndex))
            {
                continue;
            }

            var uri = BuildUri(model + methodSuffix, _apiKeys[keyIndex]);

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

            if (result.IsSuccessStatusCode)
            {
                // Ring hygiene: the next request must start at the key AFTER the
                // one that just served, even when this request rotated keys
                // mid-flight (429/5xx) — otherwise the last successful key gets
                // re-selected and serves twice in a row.
                AdvancePast(keyIndex);

                using (result)
                {
                    return await result.Content.ReadAsStringAsync(ct);
                }
            }

            var status = (int)result.StatusCode;
            string? errorBody = null;

            if (status == 429 || status == 401 || status == 403)
            {
                if (status == 429)
                {
                    // The 429 body carries Google's RetryInfo (mandatory backoff)
                    // and the quotaId (minute vs daily scope) — keep it for the
                    // exhaustion exception AND park this key for the retryDelay.
                    errorBody = await result.Content.ReadAsStringAsync(ct);
                    var (retryAfter, _) = ParseQuotaRetryInfo(
                        new GeminiApiException($"Gemini key error (429).", 429, errorBody));
                    ParkKey(keyIndex, retryAfter);
                }
                else
                {
                    // 401/403: bad or revoked key — park it LONG so one dead key
                    // doesn't waste a retry slot on every request.
                    ParkKey(keyIndex, TimeSpan.FromMinutes(10));
                }

                result.Dispose();
                lastError = new GeminiApiException($"Gemini key error ({status}).", status, errorBody);
                continue;
            }

            if (status < 500 && status != 408)
            {
                // Permanent client error — a different key won't help and retrying
                // the same body never will. Include Google's message (e.g. the
                // batch-size cap) in the exception text so it's visible in logs.
                using (result)
                {
                    var body4xx = await result.Content.ReadAsStringAsync(ct);
                    throw new GeminiApiException(
                        $"Gemini API returned {status}: {TruncateForMessage(body4xx)}", status, body4xx);
                }
            }

            // 5xx / 408: try the next key in the ring.
            lastError = new GeminiApiException(
                $"Gemini API returned {status}.", status, null);
            result.Dispose();
        }

        // Every key was either parked or failed this round.
        throw BuildQuotaException(lastError);
    }

    private GeminiQuotaExhaustedException BuildQuotaException(Exception? lastError)
    {
        var (retryAfter, isPerMinute) = ParseQuotaRetryInfo(lastError);

        // When every key is parked, the soonest any pool frees up is the
        // earliest parkedUntil — that's a better backoff than the last 429's
        // retryDelay (which describes only ONE pool).
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var earliest = _parkedUntil.Where(t => t > now).DefaultIfEmpty(DateTime.MaxValue).Min();
            if (earliest != DateTime.MaxValue)
            {
                var wait = earliest - now;
                if (retryAfter == null || wait > retryAfter)
                {
                    retryAfter = wait;
                }
            }
        }

        return new GeminiQuotaExhaustedException(
            "All Gemini API keys are throttled. Wait for quota reset.",
            retryAfter,
            isPerMinute,
            lastError);
    }

    /// <summary>
    /// Advances the ring and returns the next un-parked key's index, or null
    /// when every key is currently parked (nothing sendable).
    /// </summary>
    private int? NextKeyOrNull()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            for (var step = 0; step < _apiKeys.Length; step++)
            {
                var candidate = (_keyIndex + step) % _apiKeys.Length;
                if (_parkedUntil[candidate] <= now)
                {
                    // Consume this ring slot: the index itself moves to the
                    // NEXT key so consecutive calls stripe across the ring.
                    _keyIndex = (candidate + 1) % _apiKeys.Length;
                    return candidate;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Moves the ring start to the key AFTER <paramref name="keyIndex"/> without
    /// consuming a slot — used after a successful send (possibly mid-rotation)
    /// so the next request never re-selects the key that just served.
    // ponytail: two concurrent sends can both call this and the later-finishing
    // one rewinds _keyIndex — a benign race (worst case one key serves twice in
    // a row); fix with a ring generation counter only if striping fairness
    /// measurably degrades.
    /// </summary>
    private void AdvancePast(int keyIndex)
    {
        lock (_lock)
        {
            _keyIndex = (keyIndex + 1) % _apiKeys.Length;
        }
    }

    private bool IsParked(int keyIndex)
    {
        lock (_lock)
        {
            return _parkedUntil[keyIndex] > DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Parks a throttled key until <paramref name="retryAfter"/> elapses (from
    /// Google's RetryInfo when present, else an escalating fallback: repeated
    /// 429s on the same key wait progressively longer).
    /// </summary>
    private void ParkKey(int keyIndex, TimeSpan? retryAfter)
    {
        lock (_lock)
        {
            var wait = retryAfter ?? TimeSpan.FromSeconds(30);
            var until = DateTime.UtcNow + wait;

            // Escalate if this key was parked recently (back-to-back 429s).
            if (_parkedUntil[keyIndex] > DateTime.UtcNow)
            {
                until = _parkedUntil[keyIndex] + wait;
            }

            _parkedUntil[keyIndex] = until;
        }
    }

    /// <summary>
    /// Extracts RetryInfo.retryDelay and the quota scope from the last 429 error body.
    /// Shape (Google REST error details):
    ///   error.details[] = [
    ///     { "@type": "type.googleapis.com/google.rpc.RetryInfo",
    ///       "retryDelay": "37s" },
    ///     { "@type": "...QuotaFailure",
    ///       "violations[]": { "quotaId": "...PerDay..." | "...PerMinute..." } }
    ///   ]
    /// Daily vs minute scope decides whether the batch job should pause briefly or
    /// stop entirely until the midnight-Pacific reset.
    /// </summary>
    internal static (TimeSpan? RetryAfter, bool IsPerMinuteQuota) ParseQuotaRetryInfo(Exception? lastError)
    {
        if (lastError is not GeminiApiException { ResponseBody: { } body })
        {
            return (null, IsPerMinuteQuota: true);
        }

        TimeSpan? retryAfter = null;
        var isPerMinute = true;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object ||
                !error.TryGetProperty("details", out var details) ||
                details.ValueKind != JsonValueKind.Array)
            {
                return (null, isPerMinute);
            }

            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("@type", out var type))
                {
                    continue;
                }

                var typeName = type.GetString() ?? string.Empty;
                if (typeName.EndsWith("RetryInfo", StringComparison.Ordinal) &&
                    detail.TryGetProperty("retryDelay", out var delay) &&
                    delay.GetString() is { } delayText)
                {
                    // Format: "37s" / "2.5s" — Google always sends seconds here.
                    if (delayText.EndsWith('s') &&
                        double.TryParse(delayText[..^1], System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                    {
                        retryAfter = TimeSpan.FromSeconds(Math.Max(1, seconds));
                    }
                }
                else if (typeName.EndsWith("QuotaFailure", StringComparison.Ordinal) &&
                         detail.TryGetProperty("violations", out var violations))
                {
                    foreach (var violation in violations.EnumerateArray())
                    {
                        if (violation.TryGetProperty("quotaId", out var quotaId))
                        {
                            var id = quotaId.GetString() ?? string.Empty;
                            if (id.Contains("PerDay", StringComparison.OrdinalIgnoreCase) ||
                                id.Contains("Daily", StringComparison.OrdinalIgnoreCase))
                            {
                                isPerMinute = false;
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed body — keep defaults, caller paces itself.
        }

        return (retryAfter, isPerMinute);
    }

    private Uri BuildUri(string modelPath, string apiKey) =>
        new UriBuilder(BaseUrl + modelPath) { Query = $"key={Uri.EscapeDataString(apiKey)}" }.Uri;

    /// <summary>
    /// Extracts the human-readable error.message from a Google error body (or
    /// returns a generic fallback) so 4xx messages are actionable in logs.
    /// </summary>
    private static string TruncateForMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no response body";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String &&
                message.GetString() is { } text)
            {
                return text.Length > 300 ? text[..300] : text;
            }
        }
        catch (JsonException)
        {
            // fall through to raw truncation
        }

        return body.Length > 300 ? body[..300] : body;
    }
}
