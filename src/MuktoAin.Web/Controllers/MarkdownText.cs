using Markdig;
using Microsoft.AspNetCore.Html;

namespace MuktoAin.Web.Controllers;

/// <summary>
/// Renders AI-generated markdown (rights explanations, chat answers — anything
/// coming back from Gemini) into safe HTML for display. Raw inline/block HTML
/// from the source text is disabled rather than escaped-and-shown, so a
/// prompt-injected &lt;script&gt; tag renders as nothing instead of as text or
/// as executable markup. Presentation-only — not for the generated legal
/// documents themselves, which are plain text on purpose (paper-sheet).
/// </summary>
public static class MarkdownText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static IHtmlContent ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? HtmlString.Empty
            : new HtmlString(Markdown.ToHtml(markdown, Pipeline));
}
