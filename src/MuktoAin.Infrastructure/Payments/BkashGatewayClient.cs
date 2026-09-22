using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Payments;

// bKash tokenized checkout sandbox adapter
// (https://developer.bka.sh/docs/tokenized-checkout-process-overview):
// grant token -> create payment (browser goes to bkashURL, the real bKash
// wallet/OTP/PIN screens) -> bKash redirects to callbackURL with paymentID and
// status -> execute payment (our server-to-server confirmation).
// ValidateAsync(paymentID) is the execute call; if bKash says the payment was
// already executed, the payment-status query answers instead.
//
// Sandbox test wallets: 01619777282 / 01619777283, OTP 123456, PIN 12121.
public class BkashGatewayClient : IPaymentGatewayClient
{
    // Shown to the citizen in Sandbox mode (_PaymentMethodPicker), since the
    // real bKash checkout page cannot carry our hint.
    public const string SandboxWallet = "01619777282";
    public const string SandboxOtp = "123456";
    public const string SandboxPin = "12121";

    private const string Success = "0000";

    private readonly HttpClient _httpClient;
    private readonly BkashOptions _options;
    private readonly BkashTokenCache _tokens;

    public BkashGatewayClient(HttpClient httpClient, IOptions<BkashOptions> options, BkashTokenCache tokens)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _tokens = tokens;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    // bKash has one callback URL for every outcome (status=success|failure|
    // cancel), so only successUrl is used.
    public async Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl,
        GatewayCustomer? customer = null)
    {
        var (payload, error) = await CallAsync<CreateResponse>("/tokenized/checkout/create", new Dictionary<string, string>
        {
            ["mode"] = "0011", // checkout without a saved agreement
            ["payerReference"] = "MuktoAin",
            ["callbackURL"] = successUrl,
            ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "BDT",
            ["intent"] = "sale",
            ["merchantInvoiceNumber"] = transactionId,
        });
        if (payload is null)
        {
            return new GatewaySessionResult(false, null, error);
        }

        if (payload.statusCode != Success
            || string.IsNullOrWhiteSpace(payload.bkashURL)
            || string.IsNullOrWhiteSpace(payload.paymentID))
        {
            return new GatewaySessionResult(false, null, payload.statusMessage ?? "bKash payment could not be created");
        }

        return new GatewaySessionResult(true, payload.bkashURL, null, payload.paymentID);
    }

    public async Task<GatewayValidationResult> ValidateAsync(string valId)
    {
        var body = new Dictionary<string, string> { ["paymentID"] = valId };

        var (payment, error) = await CallAsync<PaymentResponse>("/tokenized/checkout/execute", body);
        if (payment is null)
        {
            return new GatewayValidationResult(false, null, null, null, null, error);
        }

        // Executed already (a replayed callback, or a lost response to an
        // earlier execute): ask for the payment's state instead.
        if (payment.statusCode != Success)
        {
            var (status, statusError) = await CallAsync<PaymentResponse>("/tokenized/checkout/payment/status", body);
            if (status is null)
            {
                return new GatewayValidationResult(false, null, null, null, null, statusError);
            }
            if (status.statusCode == Success) payment = status;
        }

        if (payment.statusCode != Success || payment.transactionStatus != "Completed")
        {
            return new GatewayValidationResult(
                false, payment.merchantInvoiceNumber, payment.trxID,
                payment.transactionStatus ?? payment.statusCode ?? "FAILED", null,
                payment.statusMessage ?? "Payment not completed");
        }

        if (!decimal.TryParse(payment.amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return new GatewayValidationResult(
                false, payment.merchantInvoiceNumber, payment.trxID, payment.transactionStatus, null,
                "Gateway amount unreadable");
        }

        return new GatewayValidationResult(
            true, payment.merchantInvoiceNumber, payment.trxID, payment.transactionStatus, amount, null);
    }

    // Authorized POST. Returns the parsed body, or null plus an error message
    // for transport failures, timeouts, non-2xx statuses and unreadable JSON.
    private async Task<(T? Payload, string? Error)> CallAsync<T>(string path, Dictionary<string, string> body)
        where T : class
    {
        try
        {
            var token = await _tokens.GetAsync(GrantTokenAsync);
            if (token is null) return (null, "bKash token grant failed");

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl + path)
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.TryAddWithoutValidation("Authorization", token);
            request.Headers.TryAddWithoutValidation("X-APP-Key", _options.AppKey);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"Gateway returned HTTP {(int)response.StatusCode}");
            }
            var payload = await response.Content.ReadFromJsonAsync<T>();
            return payload is null ? (null, "Gateway response unreadable") : (payload, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Gateway unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, "Gateway request timed out");
        }
        catch (JsonException)
        {
            return (null, "Gateway response unreadable");
        }
    }

    private async Task<BkashToken?> GrantTokenAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl + "/tokenized/checkout/token/grant")
        {
            Content = JsonContent.Create(new Dictionary<string, string>
            {
                ["app_key"] = _options.AppKey,
                ["app_secret"] = _options.AppSecret,
            }),
        };
        request.Headers.TryAddWithoutValidation("username", _options.Username);
        request.Headers.TryAddWithoutValidation("password", _options.Password);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (payload?.statusCode != Success || string.IsNullOrEmpty(payload.id_token)) return null;

        return new BkashToken(payload.id_token, TimeSpan.FromSeconds(payload.expires_in > 0 ? payload.expires_in : 3600));
    }

    // Property names match bKash's JSON fields.
    private class TokenResponse
    {
        public string? statusCode { get; set; }
        public string? id_token { get; set; }
        public int expires_in { get; set; }
    }

    private class CreateResponse
    {
        public string? statusCode { get; set; }
        public string? statusMessage { get; set; }
        public string? paymentID { get; set; }
        public string? bkashURL { get; set; }
    }

    private class PaymentResponse
    {
        public string? statusCode { get; set; }
        public string? statusMessage { get; set; }
        public string? paymentID { get; set; }
        public string? trxID { get; set; }
        public string? transactionStatus { get; set; }
        public string? amount { get; set; }
        public string? merchantInvoiceNumber { get; set; }
    }
}

public record BkashToken(string IdToken, TimeSpan Lifetime);

// The bKash id_token lives an hour; one per app, renewed five minutes early.
// Singleton, because BkashGatewayClient is a transient typed HttpClient.
public class BkashTokenCache
{
    private static readonly TimeSpan RenewEarly = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public BkashTokenCache(TimeProvider time) => _time = time;

    public async Task<string?> GetAsync(Func<Task<BkashToken?>> grant)
    {
        await _lock.WaitAsync();
        try
        {
            if (_token is null || _time.GetUtcNow() >= _expiresAt)
            {
                var fresh = await grant();
                if (fresh is null) return null;
                _token = fresh.IdToken;
                _expiresAt = _time.GetUtcNow() + fresh.Lifetime - RenewEarly;
            }
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }
}
