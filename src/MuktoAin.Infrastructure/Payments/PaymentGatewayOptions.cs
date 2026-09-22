namespace MuktoAin.Infrastructure.Payments;

// Payments:Mode (see PaymentGatewayResolver).
public class PaymentGatewayOptions
{
    public const string SectionName = "Payments";

    public const string Simulator = "Simulator";
    public const string Sandbox = "Sandbox";

    // "Simulator" (default, built-in, offline): every method goes to
    // /GatewaySim. "Sandbox": bKash goes to the bKash tokenized-checkout
    // sandbox (Bkash section), card goes to the SSLCommerz sandbox
    // (SslCommerz section).
    public string Mode { get; set; } = Simulator;
}

public class SslCommerzOptions
{
    public const string SectionName = "SslCommerz";

    public string StoreId { get; set; } = "";

    public string StorePassword { get; set; } = "";

    // Sandbox. Live merchants use https://securepay.sslcommerz.com, which this
    // project never does.
    public string BaseUrl { get; set; } = "https://sandbox.sslcommerz.com";

    public double RequestTimeoutSeconds { get; set; } = 30;
}

public class BkashOptions
{
    public const string SectionName = "Bkash";

    // Tokenized checkout sandbox. The credentials are bKash's public sandbox
    // merchant (developer docs); live merchants use a different host, which
    // this project never does.
    public string BaseUrl { get; set; } = "https://tokenized.sandbox.bka.sh/v1.2.0-beta";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string AppKey { get; set; } = "";

    public string AppSecret { get; set; } = "";

    public double RequestTimeoutSeconds { get; set; } = 30;
}
