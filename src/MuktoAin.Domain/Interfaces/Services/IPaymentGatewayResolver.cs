using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Interfaces.Services;

// Maps the citizen's payment method to a gateway (depends on Payments:Mode)
// and hands out that gateway's client.
public interface IPaymentGatewayResolver
{
    PaymentGateway ForMethod(PaymentMethod method);

    IPaymentGatewayClient Get(PaymentGateway gateway);
}
