namespace MuktoAin.Domain.Interfaces.Services;

// Payment gateway port (FR-24). The two calls mirror SSLCommerz's own two-step
// flow, so SimulatedGateway, SslCommerzGatewayClient and BkashGatewayClient are
// interchangeable: InitSessionAsync returns a hosted checkout URL to send the
// browser to; ValidateAsync is the server-to-server check made after the
// gateway sends the browser back to us (SSLCommerz val_id, bKash paymentID).
// Nothing is marked Paid on the strength of the browser callback alone.
public interface IPaymentGatewayClient
{
    Task<GatewaySessionResult> InitSessionAsync(
        string transactionId,
        decimal amount,
        string purposeLabel,
        string successUrl,
        string failUrl,
        string cancelUrl,
        GatewayCustomer? customer = null);

    Task<GatewayValidationResult> ValidateAsync(string valId);
}

// The paying citizen, as the gateway's customer. SSLCommerz keys its saved
// cards on this, so sending one shared customer for every user would show
// everyone the same saved-card list. Null (a guest order) falls back to a
// placeholder customer.
public record GatewayCustomer(string Name, string? Email, string? Phone);

// SessionId: the gateway's own id for the checkout (bKash paymentID), when the
// callback identifies the payment by it instead of by our tran_id.
public record GatewaySessionResult(
    bool Success, string? GatewayPageUrl, string? ErrorMessage, string? SessionId = null);

public record GatewayValidationResult(
    bool Success,
    string? TransactionId,
    string? BankTransactionId,
    string? Status,
    decimal? Amount,
    string? ErrorMessage);
