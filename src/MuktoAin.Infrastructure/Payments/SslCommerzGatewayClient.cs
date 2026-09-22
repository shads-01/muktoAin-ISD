using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Payments;

// SSLCommerz sandbox adapter (https://developer.sslcommerz.com/doc/v4/):
// session-init + validation APIs. Selected with Payments:Gateway=SslCommerz.
// SSLCommerz simulates bKash, Rocket, Nagad and card checkouts behind one
// integration. No Polly pipeline (unlike GeminiClient): this is a
// user-facing redirect, so a failed call surfaces at once as an error.
public class SslCommerzGatewayClient : IPaymentGatewayClient
{
    // SSLCommerz's published sandbox test card (Mastercard 5111111111111111
    // and Amex 371111111111111 work too). Shown to the citizen in Sandbox
    // mode (_PaymentMethodPicker), next to the bKash test wallet.
    public const string SandboxCard = "4111 1111 1111 1111";
    public const string SandboxCardCvv = "111";
    public const string SandboxOtp = "111111";

    private readonly HttpClient _httpClient;
    private readonly SslCommerzOptions _options;

    public SslCommerzGatewayClient(HttpClient httpClient, IOptions<SslCommerzOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    public async Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl,
        GatewayCustomer? customer = null)
    {
        var form = new Dictionary<string, string>
        {
            ["store_id"] = _options.StoreId,
            ["store_passwd"] = _options.StorePassword,
            ["total_amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["currency"] = "BDT",
            ["tran_id"] = transactionId,
            ["success_url"] = successUrl,
            ["fail_url"] = failUrl,
            ["cancel_url"] = cancelUrl,
            ["cus_name"] = string.IsNullOrWhiteSpace(customer?.Name) ? "MuktoAin Citizen" : customer.Name,
            ["cus_email"] = string.IsNullOrWhiteSpace(customer?.Email) ? "citizen@muktoain.local" : customer.Email,
            ["cus_add1"] = "Dhaka",
            ["cus_city"] = "Dhaka",
            ["cus_country"] = "Bangladesh",
            ["cus_phone"] = string.IsNullOrWhiteSpace(customer?.Phone) ? "01700000000" : customer.Phone,
            ["shipping_method"] = "NO",
            ["product_name"] = purposeLabel,
            ["product_category"] = "Legal Service",
            ["product_profile"] = "general",
        };

        var (response, error) = await SendAsync(() => _httpClient.PostAsync(
            $"{_options.BaseUrl}/gwprocess/v4/api.php", new FormUrlEncodedContent(form)));
        if (response is null)
        {
            return new GatewaySessionResult(false, null, error);
        }

        var payload = await ReadJsonAsync<InitResponse>(response);
        if (payload is null
            || !string.Equals(payload.status, "SUCCESS", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload.GatewayPageURL))
        {
            return new GatewaySessionResult(false, null, payload?.failedreason ?? "Gateway session init failed");
        }

        return new GatewaySessionResult(true, payload.GatewayPageURL, null);
    }

    public async Task<GatewayValidationResult> ValidateAsync(string valId)
    {
        var url = $"{_options.BaseUrl}/validator/api/validationserverAPI.php" +
                  $"?val_id={Uri.EscapeDataString(valId)}" +
                  $"&store_id={Uri.EscapeDataString(_options.StoreId)}" +
                  $"&store_passwd={Uri.EscapeDataString(_options.StorePassword)}" +
                  "&format=json";

        var (response, error) = await SendAsync(() => _httpClient.GetAsync(url));
        if (response is null)
        {
            return new GatewayValidationResult(false, null, null, null, null, error);
        }

        var payload = await ReadJsonAsync<ValidationResponse>(response);
        var isValid = payload is not null &&
            (string.Equals(payload.status, "VALID", StringComparison.OrdinalIgnoreCase)
             || string.Equals(payload.status, "VALIDATED", StringComparison.OrdinalIgnoreCase));
        if (!isValid)
        {
            return new GatewayValidationResult(
                false, payload?.tran_id, payload?.bank_tran_id, payload?.status, null, "Payment not valid");
        }

        if (!decimal.TryParse(payload!.amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return new GatewayValidationResult(
                false, payload.tran_id, payload.bank_tran_id, payload.status, null, "Gateway amount unreadable");
        }

        return new GatewayValidationResult(true, payload.tran_id, payload.bank_tran_id, payload.status, amount, null);
    }

    // Returns the response, or null plus an error message for transport
    // failures, timeouts, and non-2xx statuses.
    private static async Task<(HttpResponseMessage? Response, string? Error)> SendAsync(
        Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var response = await send();
            return response.IsSuccessStatusCode
                ? (response, null)
                : (null, $"Gateway returned HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return (null, $"Gateway unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, "Gateway request timed out");
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Property names match SSLCommerz's JSON fields.
    private class InitResponse
    {
        public string? status { get; set; }
        public string? GatewayPageURL { get; set; }
        public string? failedreason { get; set; }
    }

    private class ValidationResponse
    {
        public string? status { get; set; }
        public string? tran_id { get; set; }
        public string? bank_tran_id { get; set; }
        public string? amount { get; set; }
    }
}
