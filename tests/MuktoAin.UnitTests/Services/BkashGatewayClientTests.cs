using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.UnitTests.Services;

// The bKash tokenized-checkout sandbox adapter, against a fake HTTP handler
// standing in for tokenized.sandbox.bka.sh.
public class BkashGatewayClientTests
{
    private const string Base = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly BkashTokenCache _tokens;

    public BkashGatewayClientTests() => _tokens = new BkashTokenCache(_time);

    private BkashGatewayClient NewClient(RoutedHandler handler) =>
        new(new HttpClient(handler), Options.Create(new BkashOptions
        {
            BaseUrl = Base,
            Username = "user",
            Password = "pass",
            AppKey = "app-key",
            AppSecret = "app-secret",
        }), _tokens);

    private static RoutedHandler WithToken(RoutedHandler handler) =>
        handler.On("/token/grant", new { statusCode = "0000", id_token = "tok-1", expires_in = 3600 });

    private static object Completed(string paymentId = "TR001", string amount = "500.00", string invoice = "MA-1-x") => new
    {
        statusCode = "0000",
        statusMessage = "Successful",
        paymentID = paymentId,
        trxID = "BKX9",
        transactionStatus = "Completed",
        amount,
        merchantInvoiceNumber = invoice,
    };

    [Fact]
    public async Task InitSessionAsync_CreatesPayment_ReturnsBkashUrlAndPaymentId()
    {
        var handler = WithToken(new RoutedHandler()).On("/checkout/create", new
        {
            statusCode = "0000",
            paymentID = "TR001",
            bkashURL = "https://sandbox.payment.bkash.com/?paymentId=TR001",
        });

        var result = await NewClient(handler).InitSessionAsync(
            "MA-1-x", 500m, "Honorarium", "https://app/Payment/BkashCallback/1", "unused", "unused");

        Assert.True(result.Success);
        Assert.Equal("https://sandbox.payment.bkash.com/?paymentId=TR001", result.GatewayPageUrl);
        Assert.Equal("TR001", result.SessionId);

        var grant = handler.Requests.Single(r => r.Uri.EndsWith("/token/grant"));
        Assert.Equal("user", grant.Headers["username"]);
        Assert.Contains("\"app_secret\":\"app-secret\"", grant.Body);

        var create = handler.Requests.Single(r => r.Uri == Base + "/tokenized/checkout/create");
        Assert.Equal("tok-1", create.Headers["Authorization"]);
        Assert.Equal("app-key", create.Headers["X-APP-Key"]);
        Assert.Contains("\"merchantInvoiceNumber\":\"MA-1-x\"", create.Body);
        Assert.Contains("\"amount\":\"500.00\"", create.Body);
        Assert.Contains("\"callbackURL\":\"https://app/Payment/BkashCallback/1\"", create.Body);
    }

    [Fact]
    public async Task InitSessionAsync_BkashRejects_ReturnsFailureWithMessage()
    {
        var handler = WithToken(new RoutedHandler())
            .On("/checkout/create", new { statusCode = "2023", statusMessage = "Insufficient Balance" });

        var result = await NewClient(handler).InitSessionAsync("MA-2", 500m, "TopUp", "https://x/cb", "", "");

        Assert.False(result.Success);
        Assert.Equal("Insufficient Balance", result.ErrorMessage);
    }

    [Fact]
    public async Task InitSessionAsync_TokenGrantFails_ReturnsFailure_WithoutCreating()
    {
        var handler = new RoutedHandler()
            .On("/token/grant", new { statusCode = "2079", statusMessage = "Invalid app token" });

        var result = await NewClient(handler).InitSessionAsync("MA-3", 500m, "TopUp", "https://x/cb", "", "");

        Assert.False(result.Success);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.EndsWith("/checkout/create"));
    }

    [Fact]
    public async Task Token_IsReused_UntilNearExpiry()
    {
        var handler = WithToken(new RoutedHandler())
            .On("/checkout/create", new { statusCode = "0000", paymentID = "TR1", bkashURL = "https://b/1" });
        var client = NewClient(handler);

        await client.InitSessionAsync("MA-a", 1m, "TopUp", "https://x/cb", "", "");
        _time.Advance(TimeSpan.FromMinutes(50));
        await client.InitSessionAsync("MA-b", 1m, "TopUp", "https://x/cb", "", "");
        Assert.Single(handler.Requests, r => r.Uri.EndsWith("/token/grant"));

        _time.Advance(TimeSpan.FromMinutes(6)); // past the 55-minute renewal point
        await client.InitSessionAsync("MA-c", 1m, "TopUp", "https://x/cb", "", "");
        Assert.Equal(2, handler.Requests.Count(r => r.Uri.EndsWith("/token/grant")));
    }

    [Fact]
    public async Task ValidateAsync_ExecuteCompleted_ReturnsInvoiceTrxIdAndAmount()
    {
        var handler = WithToken(new RoutedHandler()).On("/checkout/execute", Completed());

        var result = await NewClient(handler).ValidateAsync("TR001");

        Assert.True(result.Success);
        Assert.Equal("MA-1-x", result.TransactionId);
        Assert.Equal("BKX9", result.BankTransactionId);
        Assert.Equal(500m, result.Amount);
        Assert.Contains("\"paymentID\":\"TR001\"", handler.Requests.Single(r => r.Uri.EndsWith("/execute")).Body);
    }

    [Fact]
    public async Task ValidateAsync_AlreadyExecuted_FallsBackToStatusQuery()
    {
        var handler = WithToken(new RoutedHandler())
            .On("/checkout/execute", new { statusCode = "2117", statusMessage = "Payment execution already been called before" })
            .On("/payment/status", Completed());

        var result = await NewClient(handler).ValidateAsync("TR001");

        Assert.True(result.Success);
        Assert.Equal("MA-1-x", result.TransactionId);
    }

    [Fact]
    public async Task ValidateAsync_PaymentNotCompleted_ReturnsFailureWithStatus()
    {
        var handler = WithToken(new RoutedHandler())
            .On("/checkout/execute", new { statusCode = "2056", statusMessage = "Invalid Payment State" })
            .On("/payment/status", new { statusCode = "0000", transactionStatus = "Initiated", merchantInvoiceNumber = "MA-1-x" });

        var result = await NewClient(handler).ValidateAsync("TR001");

        Assert.False(result.Success);
        Assert.NotNull(result.Status); // a definite answer: the order is failed
    }

    [Fact]
    public async Task ValidateAsync_Unreachable_ReturnsNoStatus_SoOrderStaysPending()
    {
        var handler = WithToken(new RoutedHandler()).Throw("/checkout/execute", new HttpRequestException("reset"));

        var result = await NewClient(handler).ValidateAsync("TR001");

        Assert.False(result.Success);
        Assert.Null(result.Status);
    }

    private record SentRequest(string Uri, string Body, IReadOnlyDictionary<string, string> Headers);

    // Answers by URL suffix; records every request.
    private class RoutedHandler : HttpMessageHandler
    {
        private readonly List<(string Suffix, object? Body, Exception? Error)> _routes = new();
        public List<SentRequest> Requests { get; } = new();

        public RoutedHandler On(string suffix, object body)
        {
            _routes.Add((suffix, body, null));
            return this;
        }

        public RoutedHandler Throw(string suffix, Exception error)
        {
            _routes.Add((suffix, null, error));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(uri, body,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

            var route = _routes.First(r => uri.EndsWith(r.Suffix));
            if (route.Error != null) throw route.Error;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(route.Body), Encoding.UTF8, "application/json"),
            };
        }
    }

    private class ManualTime : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTime(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
