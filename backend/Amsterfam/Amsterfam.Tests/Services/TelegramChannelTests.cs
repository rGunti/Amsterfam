using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Amsterfam.Tests.Services;

public class TelegramChannelTests
{
    private readonly StubTelegramHandler _telegram = new();

    private TelegramChannel Channel() =>
        new(
            new TelegramBotApi(new HttpClient(_telegram)),
            Options.Create(new AppOptions { PublicUrl = "https://amsterfam.test/" })
        );

    private static readonly string Token = StubTelegramHandler.Token(1);

    private static readonly Event Ev = new()
    {
        Name = "Trip",
        Location = "Amsterdam",
        TelegramBotToken = Token,
        TelegramChatId = -100123,
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
    public async Task Send_PostsPlainText_AndReturnsChatAndMessageId()
    {
        var id = await Channel()
            .SendAsync(
                new NotificationDispatch(),
                Post("Table at **8**, see [menu](https://x.test)."),
                Ev,
                default
            );

        var call = Assert.Single(_telegram.CallsBy(Token, "sendMessage"));
        Assert.Equal($"-100123:{501}", id);
        Assert.Equal(-100123, call.Json.GetProperty("chat_id").GetInt64());
        Assert.False(call.Json.TryGetProperty("parse_mode", out _));
        Assert.True(
            call.Json.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean()
        );
        Assert.Equal(
            $"Hello\n\nTable at 8, see menu.\n\nhttps://amsterfam.test/events/{Ev.Id}/news/7",
            call.Json.GetProperty("text").GetString()
        );
    }

    [Fact]
    public void Text_WithoutTitle_StartsWithTheBody()
    {
        Assert.StartsWith("Just text", Channel().Text(Post("Just text", title: null)));
    }

    [Fact]
    public void Text_CutsLongBodies_KeepingTheLink()
    {
        var body = string.Join("\n\n", Enumerable.Repeat("Lorem ipsum dolor sit amet.", 300));

        var text = Channel().Text(Post(body));

        Assert.True(text.Length < 4096);
        Assert.Contains("…\n\nhttps://amsterfam.test/", text);
        Assert.Contains("\n\nLorem", text);
    }

    [Fact]
    public async Task Delete_UsesTheChatTheMessageWasSentTo()
    {
        var ev = new Event
        {
            Name = "Trip",
            Location = "Amsterdam",
            TelegramBotToken = Token,
            TelegramChatId = -200,
        };

        await Channel()
            .DeleteAsync(new NotificationDispatch { ExternalId = "-100123:42" }, ev, default);

        var call = Assert.Single(_telegram.CallsBy(Token, "deleteMessage"));
        Assert.Equal(-100123, call.Json.GetProperty("chat_id").GetInt64());
        Assert.Equal(42, call.Json.GetProperty("message_id").GetInt64());
    }

    [Theory]
    [InlineData("Bad Request: message to delete not found")]
    [InlineData("Bad Request: message can't be deleted for everyone")]
    public async Task Delete_OfAMessageThatCantBeDeletedAnymore_CountsAsDone(string description)
    {
        _telegram.Failing[$"{Token}/deleteMessage"] = new(400, description);

        await Channel()
            .DeleteAsync(new NotificationDispatch { ExternalId = "-100123:42" }, Ev, default);
    }

    [Fact]
    public async Task Delete_OtherErrors_AreRetried()
    {
        _telegram.Failing[$"{Token}/deleteMessage"] = new(403, "Forbidden: bot was kicked");

        await Assert.ThrowsAsync<TelegramApiException>(() =>
            Channel()
                .DeleteAsync(new NotificationDispatch { ExternalId = "-100123:42" }, Ev, default)
        );
    }

    [Fact]
    public void Truncate_KeepsLineBreaks_AndCutsAtAWord()
    {
        Assert.Equal("one\ntwo…", NewsText.Truncate("one\ntwo three four", 10));
        Assert.Equal("short", NewsText.Truncate("short", 10));
    }
}
