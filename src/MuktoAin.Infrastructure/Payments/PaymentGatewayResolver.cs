using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Infrastructure.Payments;

// Payments:Mode = Simulator sends every method to the built-in simulator;
// Sandbox sends bKash to the bKash sandbox and card to SSLCommerz. Scoped:
// the typed HttpClients it hands out are transient and must not be captured
// by the root provider.
public class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IServiceProvider _services;
    private readonly bool _sandbox;

    public PaymentGatewayResolver(IServiceProvider services, IOptions<PaymentGatewayOptions> options)
    {
        _services = services;
        _sandbox = string.Equals(options.Value.Mode, PaymentGatewayOptions.Sandbox, StringComparison.OrdinalIgnoreCase);
    }

    public PaymentGateway ForMethod(PaymentMethod method) =>
        !_sandbox ? PaymentGateway.Simulator
        : method == PaymentMethod.Bkash ? PaymentGateway.Bkash
        : PaymentGateway.SslCommerz;

    public IPaymentGatewayClient Get(PaymentGateway gateway) => gateway switch
    {
        PaymentGateway.SslCommerz => _services.GetRequiredService<SslCommerzGatewayClient>(),
        PaymentGateway.Bkash => _services.GetRequiredService<BkashGatewayClient>(),
        _ => _services.GetRequiredService<SimulatedGateway>(),
    };
}
