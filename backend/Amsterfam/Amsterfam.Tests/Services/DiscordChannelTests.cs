using System.Net;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Amsterfam.Tests.Services;

public class DiscordChannelTests
{
    private const string Webhook = "https://discord.com/api/webhooks/42/tok";

    private readonly StubDiscordHandler _discord = new();

    private DiscordChannel Channel() =>
        new(
            new HttpClient(_discord),
            Options.Create(new AppOptions { PublicUrl = "https://amsterfam.test/" })
        );

    private static readonly Event Ev = new()
    {
        Name = "Trip",
        Location = "Amsterdam",
        DiscordWebhookUrl = Webhook,
    };

    private static NewsPost Post(string body, string? title = "Hello") =>
        new()
        {
            Id = 7,
            EventId = Ev.Id,
            Title = title,
            Body = body,
        };

    [Fact]
    public async Task Send_ReturnsTheMessageId()
    {
        var id = await Channel().SendAsync(new NotificationDispatch(), Post("Hi"), Ev, default);

        Assert.NotNull(id);
        var call = Assert.Single(_discord.Calls);
        Assert.Equal($"{Webhook}?wait=true", call.Uri.ToString());
    }

    [Fact]
    public async Task Send_ThrowsWithoutTheUrl()
    {
        _discord.Failing["42"] = HttpStatusCode.TooManyRequests;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Channel().SendAsync(new NotificationDispatch(), Post("Hi"), Ev, default)
        );

        Assert.Contains("429", ex.Message);
        Assert.DoesNotContain("tok", ex.Message);
    }

    [Fact]
    public async Task Delete_TargetsTheMessage_AndTreatsNotFoundAsDone()
    {
        var dispatch = new NotificationDispatch { ExternalId = "123" };
        await Channel().DeleteAsync(dispatch, Ev, default);
        Assert.Equal($"{Webhook}/messages/123", Assert.Single(_discord.Calls).Uri.ToString());

        _discord.Failing["42"] = HttpStatusCode.NotFound;
        await Channel().DeleteAsync(dispatch, Ev, default);

        _discord.Failing["42"] = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Channel().DeleteAsync(dispatch, Ev, default)
        );
    }

    [Fact]
    public void Content_HasTitleBodyAndLink()
    {
        var content = Channel().Content(Post("Meet at *8*."));

        Assert.Equal(
            $"**Hello**\n\nMeet at *8*.\n\nhttps://amsterfam.test/events/{Ev.Id}/news/7",
            content
        );
    }

    [Fact]
    public void Content_EscapesMarkdownInTheTitle()
    {
        var content = Channel().Content(Post("Hi", title: "snacks_and_drinks *now*"));

        Assert.StartsWith(@"**snacks\_and\_drinks \*now\***", content);
    }

    [Fact]
    public void Content_WithoutTitle_StartsWithTheBody()
    {
        Assert.StartsWith("Meet", Channel().Content(Post("Meet at 8.", title: null)));
    }

    [Fact]
    public void Content_StaysWithinDiscordsLimit()
    {
        var title = new string('t', NewsPost.MaxTitleLength);
        var body = string.Join("\n\n", Enumerable.Repeat(new string('x', 300), 13));

        Assert.True(Channel().Content(Post(body, title)).Length <= 2000);
    }
}
