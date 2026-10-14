using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using MimeKit;

namespace Amsterfam.Tests.Services;

public class EmailChannelTests
{
    private static readonly Event Ev = new() { Name = "Trip <2030>", Location = "Amsterdam" };

    private static readonly AppOptions App = new() { PublicUrl = "https://amsterfam.test/" };

    private static MimeMessage Build(string? title, string body) =>
        EmailChannel.BuildMessage(
            new NewsPost
            {
                Id = 7,
                EventId = Ev.Id,
                Title = title,
                Body = body,
            },
            Ev,
            "guest@example.com",
            App
        );

    [Fact]
    public void Message_HasSubjectRecipientAndAnId()
    {
        var message = Build("Dinner", "Table at 8.");

        Assert.Equal("[Trip <2030>] Dinner", message.Subject);
        Assert.Equal("guest@example.com", Assert.Single(message.To.Mailboxes).Address);
        Assert.False(string.IsNullOrEmpty(message.MessageId));
        Assert.EndsWith("@amsterfam.test", message.MessageId);
    }

    [Fact]
    public void Message_WithoutATitleSaysNews()
    {
        Assert.Equal("[Trip <2030>] News", Build(null, "Hi").Subject);
    }

    [Fact]
    public void TextPart_IsPlainTextWithLinks()
    {
        var text = Build("Dinner", "Table at **8**, see [the map](/map).").TextBody;

        Assert.Contains("Table at 8, see the map.", text);
        Assert.DoesNotContain("**", text);
        Assert.Contains($"https://amsterfam.test/events/{Ev.Id}/news/7", text);
        Assert.Contains("https://amsterfam.test/profile", text);
    }

    [Fact]
    public void HtmlPart_DropsRawHtmlAndMakesLinksAbsolute()
    {
        var html = Build(
            "<b>Dinner</b>",
            "Hi <script>alert(1)</script> see [the map](/map)."
        ).HtmlBody;

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>Dinner</b>", html);
        Assert.Contains("&lt;b&gt;Dinner&lt;/b&gt;", html);
        Assert.Contains("Trip &lt;2030&gt;", html);
        Assert.Contains("href=\"https://amsterfam.test/map\"", html);
        Assert.Contains($"href=\"https://amsterfam.test/events/{Ev.Id}/news/7\"", html);
        Assert.Contains("href=\"https://amsterfam.test/profile\"", html);
    }
}
