using System.Text.RegularExpressions;
using Markdig;

namespace Amsterfam.Api.Services;

/// <summary>
/// Turns a news post's Markdown body into plain text, for previews and for channels that
/// can't render Markdown. Raw HTML in the source is treated as text, never as markup.
/// </summary>
public static partial class NewsText
{
    public const int DefaultExcerptLength = 240;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>The body as plain text, paragraphs separated by blank lines.</summary>
    public static string PlainText(string markdown)
    {
        var text = Markdown.ToPlainText(markdown, Pipeline);
        return BlankLines().Replace(text.ReplaceLineEndings("\n"), "\n\n").Trim();
    }

    /// <summary>
    /// The start of the body as one line of plain text, cut at a word boundary with an
    /// ellipsis when longer than <paramref name="maxLength"/>.
    /// </summary>
    public static (string Text, bool Truncated) Excerpt(
        string markdown,
        int maxLength = DefaultExcerptLength
    )
    {
        var text = Whitespace().Replace(PlainText(markdown), " ");
        if (text.Length <= maxLength)
            return (text, false);

        var cut = text.LastIndexOf(' ', maxLength);
        if (cut < maxLength / 2)
            cut = maxLength;
        return (text[..cut].TrimEnd() + "…", true);
    }

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
