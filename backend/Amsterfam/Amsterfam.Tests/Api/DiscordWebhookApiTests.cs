using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class DiscordWebhookApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Secret = "s3cr3t-T0ken_value";

    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest)
    {
        public string Webhook => $"/api/v1/events/{EventId}/discord-webhook";
    }

    private static string WebhookUrl(int n) =>
        $"https://discord.com/api/webhooks/7700{n}/{Secret}{n}";

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    private async Task<Trip> CreateTrip(int n)
    {
        var owner = api.CreateClientWithUser($"discord|hook-owner-{n}");
        var guest = api.CreateClientWithUser($"discord|hook-guest-{n}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Webhook Test {n}",
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
        return new Trip(ev.Id, owner, guest);
    }

    private static Task<HttpResponseMessage> Set(Trip t, string? url) =>
        t.Owner.PutAsJsonAsync(t.Webhook, new SetDiscordWebhookRequest(url));

    [Fact]
    public async Task Organiser_CanSetAndRemoveTheWebhook()
    {
        var t = await CreateTrip(1);

        var status = await t.Owner.GetFromJsonAsync<DiscordWebhookStatus>(t.Webhook);
        Assert.Equal(new DiscordWebhookStatus(false, null), status);

        var response = await Set(t, WebhookUrl(1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new DiscordWebhookStatus(true, "lue1"),
            await response.Content.ReadFromJsonAsync<DiscordWebhookStatus>()
        );
        Assert.Equal(
            new DiscordWebhookStatus(true, "lue1"),
            await t.Owner.GetFromJsonAsync<DiscordWebhookStatus>(t.Webhook)
        );

        Assert.Equal(HttpStatusCode.NoContent, (await t.Owner.DeleteAsync(t.Webhook)).StatusCode);
        Assert.Equal(
            new DiscordWebhookStatus(false, null),
            await t.Owner.GetFromJsonAsync<DiscordWebhookStatus>(t.Webhook)
        );
    }

    [Fact]
    public async Task OnlyOrganisersCanSeeOrChangeIt()
    {
        var t = await CreateTrip(2);
        var pending = api.CreateClientWithUser("discord|hook-pending-2");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|hook-stranger-2");

        // Like the news endpoints: members who aren't organisers get 403, everyone else 404.
        foreach (
            var (client, expected) in new[]
            {
                (t.Guest, HttpStatusCode.Forbidden),
                (pending, HttpStatusCode.Forbidden),
                (stranger, HttpStatusCode.NotFound),
            }
        )
        {
            Assert.Equal(expected, (await client.GetAsync(t.Webhook)).StatusCode);
            Assert.Equal(
                expected,
                (
                    await client.PutAsJsonAsync(
                        t.Webhook,
                        new SetDiscordWebhookRequest(WebhookUrl(2))
                    )
                ).StatusCode
            );
            Assert.Equal(expected, (await client.DeleteAsync(t.Webhook)).StatusCode);
            Assert.Equal(expected, (await client.PostAsync($"{t.Webhook}/test", null)).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Owner.GetAsync($"/api/v1/events/{Guid.NewGuid()}/discord-webhook")).StatusCode
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://discord.com/api/webhooks/1/abc")]
    [InlineData("https://example.com/api/webhooks/1/abc")]
    [InlineData("https://discord.com.evil.example/api/webhooks/1/abc")]
    [InlineData("https://evil.example/?u=https://discord.com/api/webhooks/1/abc")]
    [InlineData("https://discord.com:8443/api/webhooks/1/abc")]
    [InlineData("https://user:pw@discord.com/api/webhooks/1/abc")]
    [InlineData("https://discord.com/api/webhooks/abc/def")]
    [InlineData("https://discord.com/api/webhooks/1/abc/extra")]
    [InlineData("https://discord.com/api/webhooks/1/abc?thread_id=2")]
    [InlineData("https://discord.com/api/webhooks/1/abc#x")]
    [InlineData("https://discord.com/api/webhooks/1/../../users/@me")]
    [InlineData("https://127.0.0.1/api/webhooks/1/abc")]
    public async Task InvalidUrls_AreRejected(string? url)
    {
        var t = await CreateTrip(3);

        var response = await Set(t, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await t.Owner.GetFromJsonAsync<DiscordWebhookStatus>(t.Webhook))!.Configured);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/123/abc_DEF-9")]
    [InlineData("https://discordapp.com/api/webhooks/123/abc_DEF-9")]
    [InlineData("  https://Discord.com/api/webhooks/123/abc_DEF-9  ")]
    public async Task DiscordUrls_AreAccepted(string url)
    {
        var t = await CreateTrip(4);

        var response = await Set(t, url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Url_NeverAppearsInEventOrTimelineResponses()
    {
        var t = await CreateTrip(5);
        (await Set(t, WebhookUrl(5))).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Webhook)).EnsureSuccessStatusCode();

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
        var t = await CreateTrip(6);
        (await Set(t, WebhookUrl(6))).EnsureSuccessStatusCode();
        // Saving the same URL again isn't a change.
        (await Set(t, WebhookUrl(6))).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Webhook)).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync(t.Webhook)).EnsureSuccessStatusCode();

        var entries = (
            await t.Owner.GetFromJsonAsync<List<TimelineEntryResponse>>(
                $"/api/v1/events/{t.EventId}/timeline"
            )
        )!
            .Where(e => e.Type == "DiscordWebhookChanged")
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("Organisers", e.Visibility));
        Assert.False(entries[0].Data!.Value.GetProperty("configured").GetBoolean());
        Assert.True(entries[1].Data!.Value.GetProperty("configured").GetBoolean());

        var guestTimeline = await t.Guest.GetFromJsonAsync<List<TimelineEntryResponse>>(
            $"/api/v1/events/{t.EventId}/timeline"
        );
        Assert.DoesNotContain(guestTimeline!, e => e.Type == "DiscordWebhookChanged");
    }

    [Fact]
    public async Task Test_SendsAMessage()
    {
        var t = await CreateTrip(7);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await t.Owner.PostAsync($"{t.Webhook}/test", null)).StatusCode
        );
        (await Set(t, WebhookUrl(7))).EnsureSuccessStatusCode();

        var response = await t.Owner.PostAsync($"{t.Webhook}/test", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var call = Assert.Single(api.Discord.CallsTo("77007"));
        Assert.Contains("Webhook Test 7", call.Body);
        Assert.Contains("\"parse\":[]", call.Body);
    }

    [Fact]
    public async Task Test_ReportsDiscordErrors_WithoutTheUrl()
    {
        var t = await CreateTrip(8);
        (await Set(t, WebhookUrl(8))).EnsureSuccessStatusCode();
        api.Discord.Failing["77008"] = HttpStatusCode.Unauthorized;

        var response = await t.Owner.PostAsync($"{t.Webhook}/test", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("401", body);
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task ReadOnlyEvents_CantChangeIt()
    {
        var t = await CreateTrip(9);
        await t.Owner.TransitionThroughAsync(t.EventId, "Cancelled");

        Assert.Equal(HttpStatusCode.Conflict, (await Set(t, WebhookUrl(9))).StatusCode);
    }

    [Fact]
    public async Task Test_IsThrottledPerEvent()
    {
        var t = await CreateTrip(10);
        var other = await CreateTrip(11);
        (await Set(t, WebhookUrl(10))).EnsureSuccessStatusCode();
        (await Set(other, WebhookUrl(11))).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.PostAsync($"{t.Webhook}/test", null)).StatusCode
        );
        var again = await t.Owner.PostAsync($"{t.Webhook}/test", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.NotNull(again.Headers.RetryAfter?.Delta);
        Assert.Single(api.Discord.CallsTo("770010"));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await other.Owner.PostAsync($"{other.Webhook}/test", null)).StatusCode
        );
    }
}
