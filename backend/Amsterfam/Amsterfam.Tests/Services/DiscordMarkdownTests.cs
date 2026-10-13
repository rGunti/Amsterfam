using Amsterfam.Api.Services;

namespace Amsterfam.Tests.Services;

public class DiscordMarkdownTests
{
    [Fact]
    public void ShortBodies_AreSentAsIs()
    {
        Assert.Equal(
            "Hi **all**.\n\n- one\n- two",
            NewsText.DiscordMarkdown("Hi **all**.\r\n\r\n- one\r\n- two\r\n", 100)
        );
    }

    [Fact]
    public void LongBodies_AreCutAfterTheLastWholeBlock()
    {
        var body = "First paragraph.\n\nSecond [link](https://example.com/a-long-path).\n\nThird.";

        var result = NewsText.DiscordMarkdown(body, 40);

        Assert.Equal("First paragraph.\n\n…", result);
    }

    [Fact]
    public void CodeBlocks_AreNeverSplit()
    {
        var body = "Intro.\n\n```\nline 1\nline 2\nline 3\n```\n\nOutro.";

        var result = NewsText.DiscordMarkdown(body, 25);

        Assert.Equal("Intro.\n\n…", result);
    }

    [Fact]
    public void WhenNoBlockFits_FallsBackToAnExcerpt()
    {
        var body = "A very long first paragraph that keeps going and going.";

        var result = NewsText.DiscordMarkdown(body, 20);

        Assert.EndsWith("…", result);
        Assert.True(result.Length <= 21);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void Images_BecomeLinks()
    {
        var result = NewsText.DiscordMarkdown(
            "See ![the map](https://example.com/map.png) and ![](https://example.com/b.png).",
            500
        );

        Assert.Equal(
            "See [the map](https://example.com/map.png) and <https://example.com/b.png>.",
            result
        );
    }
}
