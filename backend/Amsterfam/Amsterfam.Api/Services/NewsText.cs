using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Amsterfam.Api.Services;

/// <summary>
/// Turns a news post's Markdown body into plain text, for previews and for channels that
/// can't render Markdown, or into HTML for the Atom feed. Raw HTML in the source is treated as
/// text, never as markup.
/// </summary>
public static partial class NewsText
{
    public const int DefaultExcerptLength = 240;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>Tracks where each inline starts and ends, for editing the source text.</summary>
    private static readonly MarkdownPipeline PrecisePipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .DisableHtml()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>The body as plain text, paragraphs separated by blank lines.</summary>
    public static string PlainText(string markdown)
    {
        var text = Markdown.ToPlainText(markdown, Pipeline);
        return BlankLines().Replace(text.ReplaceLineEndings("\n"), "\n\n").Trim();
    }

    /// <summary>
    /// Cuts plain text to <paramref name="maxLength"/>, at a word boundary with an ellipsis,
    /// keeping its line breaks (unlike <see cref="Excerpt"/>).
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        if (text.Length <= maxLength)
            return text;
        // Room for the ellipsis.
        var limit = maxLength - 1;
        var cut = text.LastIndexOfAny([' ', '\n'], limit);
        if (cut < limit / 2)
        {
            cut = limit;
            // A hard cut must never leave half of a surrogate pair (an emoji) behind.
            if (char.IsHighSurrogate(text[cut - 1]))
                cut--;
        }
        return text[..cut].TrimEnd() + "…";
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

    /// <summary>
    /// The body as an HTML fragment for channels outside the app (the Atom feed, ADR-016).
    /// Relative links are made absolute against <paramref name="baseUrl"/>, Markdown images
    /// become plain links (readers never load images from other hosts), and links, including
    /// <c>&lt;scheme:…&gt;</c> autolinks, that aren't http(s) or mailto are dropped, keeping
    /// their text.
    /// </summary>
    public static string Html(string markdown, string baseUrl)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            var url = Absolute(link.Url, baseUrl);
            if (url is null)
            {
                Unwrap(link);
                continue;
            }

            link.Url = url;
            if (link.IsImage)
            {
                link.IsImage = false;
                if (link.FirstChild is null)
                    link.AppendChild(new LiteralInline(url));
            }
        }

        // Angle-bracket autolinks are a separate node type and accept any scheme.
        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (!autolink.IsEmail && !IsSafeScheme(autolink.Url))
                autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString().Trim();
    }

    /// <summary>
    /// The body as Markdown for chat apps that render it themselves (Discord). Longer bodies
    /// are cut after the last whole block that fits, so a link or code block is never split;
    /// if not even the first block fits, it falls back to <see cref="Excerpt"/>. Images become
    /// plain links, so nothing is embedded.
    /// </summary>
    public static string DiscordMarkdown(string markdown, int maxLength)
    {
        var source = markdown.ReplaceLineEndings("\n").Trim();
        if (source.Length > maxLength)
        {
            var document = Markdown.Parse(source, Pipeline);
            var end = document.TakeWhile(b => b.Span.End < maxLength).LastOrDefault()?.Span.End;
            // Plain text, but Discord still renders Markdown, so it's escaped.
            if (end is null)
                return EscapeDiscordMarkdown(Excerpt(source, maxLength).Text);
            source = source[..(end.Value + 1)].TrimEnd() + "\n\n…";
        }
        return ImagesAsLinks(source);
    }

    /// <summary>
    /// Backslash-escapes the characters Discord treats as inline Markdown, so plain text (a
    /// title, an excerpt) shows as written.
    /// </summary>
    public static string EscapeDiscordMarkdown(string text) =>
        DiscordMarkdownChars().Replace(text, @"\$0");

    private static string ImagesAsLinks(string source)
    {
        var document = Markdown.Parse(source, PrecisePipeline);
        var images = document
            .Descendants<LinkInline>()
            .Where(l => l.IsImage && !l.Span.IsEmpty)
            .OrderByDescending(l => l.Span.Start);
        foreach (var image in images)
        {
            var hasText = image.FirstChild is not null;
            source = hasText
                // "![alt](url)" → "[alt](url)"
                ? source.Remove(image.Span.Start, 1)
                : source[..image.Span.Start] + $"<{image.Url}>" + source[(image.Span.End + 1)..];
        }
        return source;
    }

    private static string? Absolute(string? url, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        // On Unix "/path" parses as an absolute file URI, so rule that out first.
        if (!url.StartsWith('/') && Uri.TryCreate(url, UriKind.Absolute, out _))
            return IsSafeScheme(url) ? url : null;

        var root = baseUrl.TrimEnd('/');
        return url.StartsWith('/') ? root + url : $"{root}/{url}";
    }

    private static bool IsSafeScheme(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" or "mailto";

    /// <summary>Replaces a link with its own content.</summary>
    private static void Unwrap(LinkInline link)
    {
        while (link.FirstChild is { } child)
        {
            child.Remove();
            link.InsertBefore(child);
        }
        link.Remove();
    }

    [GeneratedRegex(@"[\\*_~`|\[\]]")]
    private static partial Regex DiscordMarkdownChars();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
