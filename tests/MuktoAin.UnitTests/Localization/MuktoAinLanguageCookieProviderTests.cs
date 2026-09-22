using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using MuktoAin.Web.Localization;

namespace MuktoAin.UnitTests.Localization;

// S-3.5: the provider bridges the EXISTING client-side toggle (main.js sets the
// "mkt-lang" cookie on every switch) to server-side RequestLocalization. The
// mapping must stay in lockstep with main.js's "bn"/"en" values.
public class MuktoAinLanguageCookieProviderTests
{
    private static ProviderCultureResult? Resolve(string? cookieValue)
    {
        var provider = new MuktoAinLanguageCookieProvider();
        var context = new DefaultHttpContext();

        if (cookieValue is not null)
        {
            context.Request.Headers.Cookie =
                $"{MuktoAinLanguageCookieProvider.CookieName}={cookieValue}";
        }

        return provider
            .DetermineProviderCultureResult(context)
            .GetAwaiter()
            .GetResult();
    }

    [Theory]
    [InlineData("bn")]
    [InlineData("en")]
    public void KnownCookieValue_YieldsProviderResult(string lang)
    {
        Assert.NotNull(Resolve(lang));
    }

    [Fact]
    public void BnCookie_MapsToBnBDCulture()
    {
        var result = Resolve("bn")!;

        Assert.Equal("bn-BD", result.Cultures[0].ToString());
        Assert.Equal("bn-BD", result.UICultures[0].ToString());
    }

    [Fact]
    public void EnCookie_MapsToEnCulture()
    {
        var result = Resolve("en")!;

        Assert.Equal("en", result.Cultures[0].ToString());
        Assert.Equal("en", result.UICultures[0].ToString());
    }

    [Fact]
    public void MissingCookie_YieldsNull_SoDefaultCultureApplies()
    {
        Assert.Null(Resolve(null));
    }

    [Fact]
    public void UnknownCookieValue_FallsBackToBnBD()
    {
        // main.js only writes "bn"/"en", but garbage must never crash the pipeline.
        var result = Resolve("fr")!;

        Assert.Equal("bn-BD", result.Cultures[0].ToString());
    }
}
