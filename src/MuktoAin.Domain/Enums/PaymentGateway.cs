namespace MuktoAin.Domain.Enums;

// Which gateway an order was sent to. Stored on PAYMENT_ORDER so the payment
// is validated by the same gateway even if Payments:Mode changes meanwhile.
public enum PaymentGateway
{
    Simulator = 0,
    SslCommerz = 1,
    Bkash = 2
}
