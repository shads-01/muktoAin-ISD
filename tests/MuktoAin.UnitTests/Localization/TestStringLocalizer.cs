using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using MuktoAin.Web.Resources;

namespace MuktoAin.UnitTests.Localization;

// Real IStringLocalizer<SharedResource> over the Web project's embedded .resx
// (the E-2.8 files), so tests assert actual resource lookups instead of stubs.
// Set CultureInfo.CurrentUICulture in the test to pick the expected language.
public static class TestStringLocalizer
{
    public static IStringLocalizer<SharedResource> Create()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        return new StringLocalizer<SharedResource>(factory);
    }
}
