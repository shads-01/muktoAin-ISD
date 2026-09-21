using System.Net.Http;
using System.Text.RegularExpressions;
using Xunit;

namespace MuktoAin.IntegrationTests.Helpers;

public static class AntiForgeryHelper
{
    private static readonly Regex TokenRegex =
        new("__RequestVerificationToken[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// Gets the given page (any page renders an antiforgery token into its
    /// forms — the token is cookie-scoped, not form-scoped), extracts the
    /// hidden token, and POSTs <paramref name="fields"/> to <paramref name="postUrl"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string anyGetUrl, string postUrl,
        Dictionary<string, string> fields)
    {
        var page = await client.GetAsync(anyGetUrl);
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No __RequestVerificationToken found on {anyGetUrl}");

        fields["__RequestVerificationToken"] = match.Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
