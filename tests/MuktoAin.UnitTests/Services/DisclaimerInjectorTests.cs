using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;

namespace MuktoAin.UnitTests.Services;

public class DisclaimerInjectorTests
{
    private readonly DisclaimerInjector _injector = new();

    [Fact]
    public void InjectDisclaimer_EnglishLanguage_AppendsEnglishDisclaimer()
    {
        var response = "This is an AI generated response.";
        var result = _injector.InjectDisclaimer(response, "en");

        Assert.StartsWith(response, result);
        Assert.EndsWith(Disclaimers.Legal, result);
        Assert.Contains(Environment.NewLine + Environment.NewLine, result);
    }

    [Fact]
    public void InjectDisclaimer_BanglaLanguage_AppendsBanglaDisclaimer()
    {
        var response = "এটি এআই নির্দেশিকা।";
        var result = _injector.InjectDisclaimer(response, "bn");

        Assert.StartsWith(response, result);
        Assert.EndsWith(Disclaimers.LegalBangla, result);
        Assert.Contains(Environment.NewLine + Environment.NewLine, result);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("")]
    [InlineData(null)]
    public void InjectDisclaimer_UnknownOrEmptyLanguage_DefaultsToBangla(string? language)
    {
        var response = "Some response text";
        var result = _injector.InjectDisclaimer(response, language!);

        Assert.EndsWith(Disclaimers.LegalBangla, result);
    }

    [Fact]
    public void InjectDisclaimer_PreservesOriginalTextVerbatim()
    {
        var original = "Original unmodified text with symbols: 123 !@#";
        var result = _injector.InjectDisclaimer(original, "en");

        Assert.Contains(original, result);
    }

    [Fact]
    public void InjectDisclaimer_NullOrWhitespaceInput_DoesNotThrow()
    {
        var resultNull = _injector.InjectDisclaimer(null!, "en");
        Assert.EndsWith(Disclaimers.Legal, resultNull);

        var resultWhitespace = _injector.InjectDisclaimer("   ", "bn");
        Assert.EndsWith(Disclaimers.LegalBangla, resultWhitespace);
    }
}
