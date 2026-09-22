namespace MuktoAin.Web.Services;

/// <summary>
/// Picks the directory for the Data Protection key ring. <c>DataProtection:KeysPath</c>
/// overrides the default <c>ContentRootPath/keys</c>, so hosts whose content root is
/// replaced on every deploy (e.g. Azure App Service's <c>/home/site/wwwroot</c>) can
/// keep keys somewhere that survives deploys.
/// </summary>
public static class DataProtectionKeysPath
{
    public static string Resolve(string? configuredPath, string contentRootPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(contentRootPath, "keys")
            : configuredPath;
}
