using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

// AUD-5: throttling is wired via [EnableRateLimiting] attributes. Reflection
// pins the policy name on every throttled action so a refactor that silently
// drops the attribute fails the suite. The limiter mechanics themselves are
// the framework's own (fixed window), so no behavioral test is needed here.
public class RateLimitingPolicyTests
{
    [Fact]
    public void AccountLogin_IsRateLimitedByAuthPolicy()
    {
        var method = typeof(AccountController).GetMethod(
            nameof(AccountController.Login), new[] { typeof(LoginViewModel), typeof(string) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("auth", attr!.PolicyName);
    }

    [Fact]
    public void AccountRegister_IsRateLimitedByAuthPolicy()
    {
        var method = typeof(AccountController).GetMethod(
            nameof(AccountController.Register), new[] { typeof(RegisterViewModel) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("auth", attr!.PolicyName);
    }

    [Fact]
    public void ChatAsk_IsRateLimitedByChatPolicy()
    {
        var method = typeof(ChatController).GetMethod(
            nameof(ChatController.Ask), new[] { typeof(ChatAskRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("chat", attr!.PolicyName);
    }

    [Fact]
    public void PaymentHonorarium_IsRateLimitedByPaymentPolicy()
    {
        var method = typeof(PaymentController).GetMethod(
            nameof(PaymentController.Honorarium), new[] { typeof(HonorariumPaymentRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("payment", attr!.PolicyName);
    }

    [Fact]
    public void PaymentTopUp_IsRateLimitedByPaymentPolicy()
    {
        var method = typeof(PaymentController).GetMethod(
            nameof(PaymentController.TopUp), new[] { typeof(TopUpPaymentRequest) })!;

        var attr = method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .OfType<EnableRateLimitingAttribute>().SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("payment", attr!.PolicyName);
    }
}
