using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class TelegramApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    /// <summary>The part of every stub token that would be a secret.</summary>
    private const string Secret = "secret_token_value";

    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest)
    {
        public string Telegram => $"/api/v1/events/{EventId}/telegram";
    }

    private static string Token(int n) => StubTelegramHandler.Token(n);

    private static long ChatId(int n) => -100_000_000_000 - n;

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    private async Task<Trip> CreateTrip(int n)
    {
        var owner = api.CreateClientWithUser($"discord|tg-owner-{n}");
        var guest = api.CreateClientWithUser($"discord|tg-guest-{n}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Telegram Test {n}",
                null,
                new DateOnly(2030, 7, 1),
                new DateOnly(2030, 7, 8),
                "Amsterdam"
            )
        );
        response.EnsureSuccessStatusCode();
        var ev = (await response.Content.ReadFromJsonAsync<EventResponse>())!;
        await owner.TransitionThroughAsync(ev.Id, "Open");
        (await guest.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();
        (
            await owner.PostAsync(
                $"/api/v1/events/{ev.Id}/attendees/{await MyId(guest)}/confirm",
                null
            )
        ).EnsureSuccessStatusCode();
        api.Telegram.Chats[ChatId(n)] = $"Group {n}";
        return new Trip(ev.Id, owner, guest);
    }

    private static Task<HttpResponseMessage> Set(Trip t, string? token, long? chatId = null) =>
        t.Owner.PutAsJsonAsync(t.Telegram, new SetTelegramRequest(token, chatId));

    /// <summary>Saves the token and picks the chat, the way the settings page does.</summary>
    private static async Task Connect(Trip t, int n)
    {
        (await Set(t, Token(n))).EnsureSuccessStatusCode();
        (await Set(t, null, ChatId(n))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Organiser_SavesTokenThenPicksChat_ThenRemovesIt()
    {
        var t = await CreateTrip(1);
        Assert.Equal(
            new TelegramStatus(false, false, null, null, null, null),
            await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram)
        );

        var afterToken = await Set(t, Token(1));
        Assert.Equal(HttpStatusCode.OK, afterToken.StatusCode);
        Assert.Equal(
            new TelegramStatus(false, true, "1xyz", "amsterfam_7000001_bot", null, null),
            await afterToken.Content.ReadFromJsonAsync<TelegramStatus>()
        );
        Assert.Single(api.Telegram.CallsBy(Token(1), "getMe"));

        var afterChat = await Set(t, null, ChatId(1));
        Assert.Equal(HttpStatusCode.OK, afterChat.StatusCode);
        var connected = new TelegramStatus(
            true,
            true,
            "1xyz",
            "amsterfam_7000001_bot",
            ChatId(1),
            "Group 1"
        );
        Assert.Equal(connected, await afterChat.Content.ReadFromJsonAsync<TelegramStatus>());
        Assert.Equal(connected, await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram));

        Assert.Equal(HttpStatusCode.NoContent, (await t.Owner.DeleteAsync(t.Telegram)).StatusCode);
        Assert.Equal(
            new TelegramStatus(false, false, null, null, null, null),
            await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram)
        );
    }

    [Fact]
    public async Task ANewToken_ClearsTheChat()
    {
        var t = await CreateTrip(2);
        await Connect(t, 2);

        var status = await (await Set(t, Token(102))).Content.ReadFromJsonAsync<TelegramStatus>();

        Assert.False(status!.Configured);
        Assert.Null(status.ChatId);
        Assert.Equal("amsterfam_7000102_bot", status.BotUsername);
    }

    [Fact]
    public async Task OnlyOrganisersCanSeeOrChangeIt()
    {
        var t = await CreateTrip(3);
        var pending = api.CreateClientWithUser("discord|tg-pending-3");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|tg-stranger-3");

        foreach (
            var (client, expected) in new[]
            {
                (t.Guest, HttpStatusCode.Forbidden),
                (pending, HttpStatusCode.Forbidden),
                (stranger, HttpStatusCode.NotFound),
            }
        )
        {
            Assert.Equal(expected, (await client.GetAsync(t.Telegram)).StatusCode);
            Assert.Equal(
                expected,
                (
                    await client.PutAsJsonAsync(t.Telegram, new SetTelegramRequest(Token(3), null))
                ).StatusCode
            );
            Assert.Equal(expected, (await client.DeleteAsync(t.Telegram)).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"{t.Telegram}/chats")).StatusCode);
            Assert.Equal(expected, (await client.PostAsync($"{t.Telegram}/test", null)).StatusCode);
        }
        Assert.Empty(api.Telegram.CallsBy(Token(3)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("123:short")]
    [InlineData("1234567:AAH-secret_token_value_000000/../getMe")]
    [InlineData("1234567:AAH-secret token value 0000000000")]
    public async Task MalformedTokens_AreRejected_WithoutCallingTelegram(string? token)
    {
        var t = await CreateTrip(4);

        var response = await t.Owner.PutAsJsonAsync(
            t.Telegram,
            new SetTelegramRequest(token, token is null ? null : (long?)null)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram))!.TokenSaved);
    }

    [Fact]
    public async Task TokenTelegramRefuses_IsRejected()
    {
        var t = await CreateTrip(5);
        api.Telegram.Failing[Token(5)] = new(401, "Unauthorized");

        var response = await Set(t, Token(5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unauthorized", body);
        Assert.DoesNotContain(Secret, body);
        Assert.False((await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram))!.TokenSaved);
    }

    [Fact]
    public async Task ChatTheBotCantSee_IsRejected()
    {
        var t = await CreateTrip(6);
        (await Set(t, Token(6))).EnsureSuccessStatusCode();

        var response = await Set(t, null, -42);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("chat not found", await response.Content.ReadAsStringAsync());
        Assert.False((await t.Owner.GetFromJsonAsync<TelegramStatus>(t.Telegram))!.Configured);
    }

    [Fact]
    public async Task ChatWithoutToken_IsRejected()
    {
        var t = await CreateTrip(7);

        Assert.Equal(HttpStatusCode.BadRequest, (await Set(t, null, ChatId(7))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await t.Owner.GetAsync($"{t.Telegram}/chats")).StatusCode
        );
    }

    [Fact]
    public async Task Token_NeverAppearsInEventOrTimelineResponses()
    {
        var t = await CreateTrip(8);
        await Connect(t, 8);
        (await t.Owner.PostAsync($"{t.Telegram}/test", null)).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Telegram)).EnsureSuccessStatusCode();
        await Connect(t, 8);

        Assert.DoesNotContain(Secret, await t.Owner.GetStringAsync(t.Telegram));
        foreach (var client in new[] { t.Owner, t.Guest })
        {
            Assert.DoesNotContain(
                Secret,
                await client.GetStringAsync($"/api/v1/events/{t.EventId}")
            );
            Assert.DoesNotContain(Secret, await client.GetStringAsync("/api/v1/events/"));
            Assert.DoesNotContain(
                Secret,
                await client.GetStringAsync($"/api/v1/events/{t.EventId}/timeline")
            );
        }
    }

    [Fact]
    public async Task Changes_AreLoggedForOrganisersOnly()
    {
        var t = await CreateTrip(9);
        api.Telegram.Chats[-9_999] = "Other group";
        // Only the token: not set up yet, so nothing to log.
        (await Set(t, Token(9))).EnsureSuccessStatusCode();
        (await Set(t, null, ChatId(9))).EnsureSuccessStatusCode();
        // The same chat again isn't a change; another one is.
        (await Set(t, null, ChatId(9))).EnsureSuccessStatusCode();
        (await Set(t, null, -9_999)).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Telegram)).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Telegram)).EnsureSuccessStatusCode();

        var entries = (
            await t.Owner.GetFromJsonAsync<List<TimelineEntryResponse>>(
                $"/api/v1/events/{t.EventId}/timeline"
            )
        )!
            .Where(e => e.Type == "TelegramChanged")
            .ToList();
        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal("Organisers", e.Visibility));
        Assert.Equal(
            [false, true, true],
            entries.Select(e => e.Data!.Value.GetProperty("configured").GetBoolean())
        );

        var guestTimeline = await t.Guest.GetFromJsonAsync<List<TimelineEntryResponse>>(
            $"/api/v1/events/{t.EventId}/timeline"
        );
        Assert.DoesNotContain(guestTimeline!, e => e.Type == "TelegramChanged");
    }

    [Fact]
    public async Task Chats_ListsGroupsTheBotHasSeen()
    {
        var t = await CreateTrip(10);
        (await Set(t, Token(10))).EnsureSuccessStatusCode();
        api.Telegram.Updates[Token(10)] = """
            [
              {"update_id":1,"my_chat_member":{"chat":{"id":-1001,"title":"Trip crew","type":"supergroup"}}},
              {"update_id":2,"message":{"chat":{"id":55,"first_name":"Klaus","type":"private"}}}
            ]
            """;

        var chats = await t.Owner.GetFromJsonAsync<List<TelegramChatOption>>($"{t.Telegram}/chats");

        Assert.Equal([new TelegramChatOption(-1001, "Trip crew", "supergroup")], chats);
    }

    [Fact]
    public async Task Chats_ReportsTelegramErrors()
    {
        var t = await CreateTrip(11);
        (await Set(t, Token(11))).EnsureSuccessStatusCode();
        api.Telegram.Failing[$"{Token(11)}/getUpdates"] = new(
            409,
            "Conflict: can't use getUpdates method while webhook is active"
        );

        var response = await t.Owner.GetAsync($"{t.Telegram}/chats");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("webhook is active", body);
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task Test_SendsAPlainMessage_AndIsThrottled()
    {
        var t = await CreateTrip(12);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await t.Owner.PostAsync($"{t.Telegram}/test", null)).StatusCode
        );
        await Connect(t, 12);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.PostAsync($"{t.Telegram}/test", null)).StatusCode
        );
        var again = await t.Owner.PostAsync($"{t.Telegram}/test", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.NotNull(again.Headers.RetryAfter?.Delta);
        var call = Assert.Single(api.Telegram.CallsBy(Token(12), "sendMessage"));
        Assert.Equal(ChatId(12), call.Json.GetProperty("chat_id").GetInt64());
        Assert.Contains("Telegram Test 12", call.Json.GetProperty("text").GetString());
        Assert.False(call.Json.TryGetProperty("parse_mode", out _));
    }

    [Fact]
    public async Task Test_ReportsTelegramErrors_WithoutTheToken()
    {
        var t = await CreateTrip(13);
        await Connect(t, 13);
        api.Telegram.Failing[$"{Token(13)}/sendMessage"] = new(
            403,
            "Forbidden: bot was kicked from the supergroup chat"
        );

        var response = await t.Owner.PostAsync($"{t.Telegram}/test", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("bot was kicked", body);
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task ReadOnlyEvents_CantChangeIt()
    {
        var t = await CreateTrip(14);
        await t.Owner.TransitionThroughAsync(t.EventId, "Cancelled");

        Assert.Equal(HttpStatusCode.Conflict, (await Set(t, Token(14))).StatusCode);
    }
}
