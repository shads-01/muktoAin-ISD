using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Payments;

namespace MuktoAin.UnitTests.Services;

// Payments:Mode routing. Simulator sends every method to the built-in
// gateway; Sandbox splits bKash and card between the two real sandboxes.
// Get() must hand back the concrete client the stored gateway names, so an
// order created under one mode still validates against the gateway that
// actually took the payment.
public class PaymentGatewayResolverTests
{
    private static PaymentGatewayResolver NewResolver(string mode)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SimulatedGateway(TimeProvider.System));
        services.AddSingleton(sp => new SslCommerzGatewayClient(
            new HttpClient { BaseAddress = new Uri("https://sandbox.sslcommerz.com") },
            Options.Create(new SslCommerzOptions { StoreId = "store", StorePassword = "pass" })));
        services.AddSingleton(sp => new BkashGatewayClient(
            new HttpClient { BaseAddress = new Uri("https://tokenized.sandbox.bka.sh") },
            Options.Create(new BkashOptions { Username = "u", Password = "p", AppKey = "k", AppSecret = "s" }),
            new BkashTokenCache(TimeProvider.System)));

        return new PaymentGatewayResolver(
            services.BuildServiceProvider(),
            Options.Create(new PaymentGatewayOptions { Mode = mode }));
    }

    [Theory]
    [InlineData(PaymentMethod.Bkash)]
    [InlineData(PaymentMethod.Card)]
    public void SimulatorMode_SendsEveryMethodToTheSimulator(PaymentMethod method)
    {
        Assert.Equal(PaymentGateway.Simulator, NewResolver(PaymentGatewayOptions.Simulator).ForMethod(method));
    }

    [Fact]
    public void SandboxMode_SendsBkashToBkash_AndCardToSslCommerz()
    {
        var resolver = NewResolver(PaymentGatewayOptions.Sandbox);

        Assert.Equal(PaymentGateway.Bkash, resolver.ForMethod(PaymentMethod.Bkash));
        Assert.Equal(PaymentGateway.SslCommerz, resolver.ForMethod(PaymentMethod.Card));
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("SANDBOX")]
    public void ModeMatching_IsCaseInsensitive(string mode)
    {
        Assert.Equal(PaymentGateway.Bkash, NewResolver(mode).ForMethod(PaymentMethod.Bkash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Live")]
    public void UnrecognisedMode_FallsBackToTheSimulator(string mode)
    {
        Assert.Equal(PaymentGateway.Simulator, NewResolver(mode).ForMethod(PaymentMethod.Bkash));
    }

    [Fact]
    public void Get_ReturnsTheClientMatchingTheStoredGateway_NotTheCurrentMode()
    {
        // Mode is Simulator, but an order created earlier under Sandbox still
        // names Bkash/SslCommerz — those orders must resolve to their own client.
        var resolver = NewResolver(PaymentGatewayOptions.Simulator);

        Assert.IsType<SimulatedGateway>(resolver.Get(PaymentGateway.Simulator));
        Assert.IsType<BkashGatewayClient>(resolver.Get(PaymentGateway.Bkash));
        Assert.IsType<SslCommerzGatewayClient>(resolver.Get(PaymentGateway.SslCommerz));
    }
}
