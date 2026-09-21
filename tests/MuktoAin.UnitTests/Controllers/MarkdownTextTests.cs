using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class MarkdownTextTests
{
    private static string ToHtmlString(string? markdown)
    {
        var html = MarkdownText.ToHtml(markdown);
        using var writer = new System.IO.StringWriter();
        html.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    public void ToHtml_StripsRawHtml_FromAiOutput(string payload)
    {
        var html = ToHtmlString(payload);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a\t", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToHtml_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ToHtmlString(null));
        Assert.Equal(string.Empty, ToHtmlString("   "));
    }

    [Fact]
    public void ToHtml_PlainMarkdown_StillRenders()
    {
        var html = ToHtmlString("**বোল্ড দাবি**");
        Assert.Contains("<strong>", html);
    }
}
