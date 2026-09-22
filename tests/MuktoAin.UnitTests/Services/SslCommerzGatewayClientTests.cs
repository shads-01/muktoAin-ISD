using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.UnitTests.Services;

// SSL-1/SSL-5: the SSLCommerz sandbox adapter, against a fake HTTP handler
// standing in for sandbox.sslcommerz.com.
public class SslCommerzGatewayClientTests
{
    private static SslCommerzGatewayClient NewClient(FakeHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SslCommerzOptions
        {
            StoreId = "testbox",
            StorePassword = "testpass",
            BaseUrl = "https://sandbox.sslcommerz.com",
        }));

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    [Fact]
    public async Task InitSessionAsync_GatewayReturnsSuccess_ReturnsGatewayPageUrl()
    {
        var handler = new FakeHandler(Json(new
        {
            status = "SUCCESS",
            GatewayPageURL = "https://sandbox.sslcommerz.com/EasyCheckOut/testabc123",
        }));

        var result = await NewClient(handler).InitSessionAsync(
            "MA-1-20260912", 500m, "Honorarium",
            "https://localhost/Payment/Success?orderId=1",
            "https://localhost/Payment/Fail?orderId=1",
            "https://localhost/Payment/Cancel?orderId=1");

        Assert.True(result.Success);
        Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/testabc123", result.GatewayPageUrl);
        Assert.Equal("https://sandbox.sslcommerz.com/gwprocess/v4/api.php", handler.LastRequestUri);
        Assert.Contains("store_id=testbox", handler.LastRequestBody);
        Assert.Contains("tran_id=MA-1-20260912", handler.LastRequestBody);
        Assert.Contains("total_amount=500.00", handler.LastRequestBody);
    }

    // SSLCommerz keys saved cards on cus_email/cus_phone, so each citizen must
    // be sent as themselves, not as one shared placeholder customer.
    [Fact]
    public async Task InitSessionAsync_SendsThePayingCustomer()
    {
        var handler = new FakeHandler(Json(new { status = "SUCCESS", GatewayPageURL = "https://x/pay" }));

        await NewClient(handler).InitSessionAsync(
            "MA-6", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c",
            new GatewayCustomer("Rahim Uddin", "rahim@example.com", "01811111111"));

        Assert.Contains("cus_name=Rahim+Uddin", handler.LastRequestBody);
        Assert.Contains("cus_email=rahim%40example.com", handler.LastRequestBody);
        Assert.Contains("cus_phone=01811111111", handler.LastRequestBody);
    }

    [Fact]
    public async Task InitSessionAsync_NoCustomerDetails_FallsBackToPlaceholders()
    {
        var handler = new FakeHandler(Json(new { status = "SUCCESS", GatewayPageURL = "https://x/pay" }));

        await NewClient(handler).InitSessionAsync(
            "MA-7", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c",
            new GatewayCustomer("", null, null));

        Assert.Contains("cus_name=MuktoAin+Citizen", handler.LastRequestBody);
        Assert.Contains("cus_email=citizen%40muktoain.local", handler.LastRequestBody);
        Assert.Contains("cus_phone=01700000000", handler.LastRequestBody);
    }

    [Fact]
    public async Task InitSessionAsync_GatewayReturnsFailed_ReturnsFailureWithReason()
    {
        var handler = new FakeHandler(Json(new { status = "FAILED", failedreason = "Invalid Store Id" }));

        var result = await NewClient(handler).InitSessionAsync(
            "MA-2-20260912", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Null(result.GatewayPageUrl);
        Assert.Equal("Invalid Store Id", result.ErrorMessage);
    }

    [Fact]
    public async Task InitSessionAsync_HttpError_ReturnsFailure()
    {
        var handler = new FakeHandler(Json(new { }), HttpStatusCode.BadGateway);

        var result = await NewClient(handler).InitSessionAsync(
            "MA-3", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Contains("502", result.ErrorMessage);
    }

    [Fact]
    public async Task InitSessionAsync_Unreachable_ReturnsFailure()
    {
        var handler = new FakeHandler(new HttpRequestException("no route"));

        var result = await NewClient(handler).InitSessionAsync(
            "MA-4", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Contains("unreachable", result.ErrorMessage);
    }

    [Fact]
    public async Task InitSessionAsync_Timeout_ReturnsFailure()
    {
        var handler = new FakeHandler(new TaskCanceledException());

        var result = await NewClient(handler).InitSessionAsync(
            "MA-5", 500m, "TopUp", "https://x/s", "https://x/f", "https://x/c");

        Assert.False(result.Success);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateAsync_StatusValid_ReturnsSuccessWithAmountAndBankTranId()
    {
        var handler = new FakeHandler(Json(new
        {
            status = "VALID",
            tran_id = "MA-1-20260912",
            bank_tran_id = "BKS99881",
            amount = "500.00",
        }));

        var result = await NewClient(handler).ValidateAsync("val-123");

        Assert.True(result.Success);
        Assert.Equal("MA-1-20260912", result.TransactionId);
        Assert.Equal("BKS99881", result.BankTransactionId);
        Assert.Equal(500.00m, result.Amount);
        Assert.Contains("val_id=val-123", handler.LastRequestUri);
        Assert.Contains("store_id=testbox", handler.LastRequestUri);
    }

    [Fact]
    public async Task ValidateAsync_StatusFailed_ReturnsFailure()
    {
        var handler = new FakeHandler(Json(new { status = "FAILED" }));

        var result = await NewClient(handler).ValidateAsync("val-999");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_UnparseableAmount_ReturnsFailure()
    {
        var handler = new FakeHandler(Json(new { status = "VALID", tran_id = "MA-1", amount = "abc" }));

        var result = await NewClient(handler).ValidateAsync("val-1");

        Assert.False(result.Success);
    }

    // Captures the outgoing request and returns a fixed response (or throws),
    // standing in for the real SSLCommerz endpoint.
    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpContent? _response;
        private readonly HttpStatusCode _status;
        private readonly Exception? _throw;
        public string LastRequestBody { get; private set; } = "";
        public string LastRequestUri { get; private set; } = "";

        public FakeHandler(HttpContent response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public FakeHandler(Exception toThrow) => _throw = toThrow;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri!.ToString();
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            if (_throw != null) throw _throw;
            return new HttpResponseMessage(_status) { Content = _response };
        }
    }
}
