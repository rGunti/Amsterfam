using System.Net;
using System.Text;
using System.Text.Json;
using Amsterfam.Api.Services;

namespace Amsterfam.Tests.Services;

public class TelegramBotApiTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseGroups_KeepsGroupsFromEveryKindOfUpdate()
    {
        var groups = TelegramBotApi.ParseGroups(
            Json(
                """
                [
                  {"update_id":1,"my_chat_member":{"chat":{"id":-1,"title":"Added to","type":"group"}}},
                  {"update_id":2,"message":{"chat":{"id":-1002,"title":"Chatty","type":"supergroup"}}},
                  {"update_id":3,"edited_message":{"chat":{"id":-1003,"title":"Edited","type":"supergroup"}}},
                  {"update_id":4,"message":{"chat":{"id":7,"first_name":"Klaus","type":"private"}}},
                  {"update_id":5,"channel_post":{"chat":{"id":-1005,"title":"A channel","type":"channel"}}},
                  {"update_id":6,"poll":{"id":"x"}}
                ]
                """
            )
        );

        Assert.Equal(
            [
                new TelegramChat(-1, "Added to", "group"),
                new TelegramChat(-1002, "Chatty", "supergroup"),
                new TelegramChat(-1003, "Edited", "supergroup"),
            ],
            groups
        );
    }

    [Fact]
    public void ParseGroups_ListsEachGroupOnce_WithItsLatestTitle()
    {
        var groups = TelegramBotApi.ParseGroups(
            Json(
                """
                [
                  {"update_id":1,"message":{"chat":{"id":-5,"title":"Old name","type":"group"}}},
                  {"update_id":2,"message":{"chat":{"id":-5,"title":"New name","type":"group"}}}
                ]
                """
            )
        );

        Assert.Equal([new TelegramChat(-5, "New name", "group")], groups);
    }

    [Fact]
    public void ParseGroups_Empty() => Assert.Empty(TelegramBotApi.ParseGroups(Json("[]")));

    private class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
    }

    private const string Token = "123456:AAH-secret_token_value_00000000000";

    [Fact]
    public async Task Errors_CarryTelegramsDescription_NotTheToken()
    {
        var api = new TelegramBotApi(
            new HttpClient(
                new Answer(
                    HttpStatusCode.BadRequest,
                    """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}"""
                )
            )
        );

        var ex = await Assert.ThrowsAsync<TelegramApiException>(() =>
            api.SendMessageAsync(Token, -1, "hi", default)
        );

        Assert.Equal(400, ex.ErrorCode);
        Assert.Equal("Telegram answered 400: Bad Request: chat not found", ex.Message);
    }

    [Fact]
    public async Task TooManyRequests_CarriesRetryAfter()
    {
        var api = new TelegramBotApi(
            new HttpClient(
                new Answer(
                    HttpStatusCode.TooManyRequests,
                    """{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 7","parameters":{"retry_after":7}}"""
                )
            )
        );

        var ex = await Assert.ThrowsAsync<NotificationRateLimitedException>(() =>
            api.SendMessageAsync(Token, -1, "hi", default)
        );

        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task UpgradedGroup_SaysToPickItAgain()
    {
        var api = new TelegramBotApi(
            new HttpClient(
                new Answer(
                    HttpStatusCode.BadRequest,
                    """{"ok":false,"error_code":400,"description":"Bad Request: group chat was upgraded to a supergroup chat","parameters":{"migrate_to_chat_id":-1009}}"""
                )
            )
        );

        var ex = await Assert.ThrowsAsync<TelegramApiException>(() =>
            api.SendMessageAsync(Token, -1, "hi", default)
        );

        Assert.Contains("pick it again", ex.Message);
    }

    [Fact]
    public async Task UnreadableAnswer_IsAnError()
    {
        var api = new TelegramBotApi(
            new HttpClient(new Answer(HttpStatusCode.BadGateway, "<html>proxy</html>"))
        );

        var ex = await Assert.ThrowsAsync<TelegramApiException>(() =>
            api.GetMeAsync(Token, default)
        );
        Assert.Equal(502, ex.ErrorCode);
    }
}
