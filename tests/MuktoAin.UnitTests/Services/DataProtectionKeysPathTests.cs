using MuktoAin.Web.Services;

namespace MuktoAin.UnitTests.Services;

public class DataProtectionKeysPathTests
{
    [Fact]
    public void Resolve_NoConfiguredPath_DefaultsToKeysUnderContentRoot()
    {
        var contentRoot = Path.Combine("app", "root");

        var result = DataProtectionKeysPath.Resolve(null, contentRoot);

        Assert.Equal(Path.Combine(contentRoot, "keys"), result);
    }

    [Fact]
    public void Resolve_BlankConfiguredPath_DefaultsToKeysUnderContentRoot()
    {
        var contentRoot = Path.Combine("app", "root");

        var result = DataProtectionKeysPath.Resolve("   ", contentRoot);

        Assert.Equal(Path.Combine(contentRoot, "keys"), result);
    }

    [Fact]
    public void Resolve_ConfiguredPath_UsesConfiguredPath()
    {
        var result = DataProtectionKeysPath.Resolve("/home/data/keys", "/home/site/wwwroot");

        Assert.Equal("/home/data/keys", result);
    }
}
