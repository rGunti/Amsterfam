using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

/// <summary>Pinning, acknowledgements and reactions on news posts (#139).</summary>
public class NewsInteractionsApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Trip(Guid EventId, HttpClient Owner, int OwnerId, HttpClient Guest, int GuestId)
    {
        public string News => $"/api/v1/events/{EventId}/news";
    }

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>An open event with its owner (an organiser) and one confirmed attendee.</summary>
    private async Task<Trip> CreateTrip(string suffix)
    {
        var owner = api.CreateClientWithUser($"discord|newsx-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|newsx-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"News Interactions {suffix}",
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
        var guestId = await MyId(guest);
        (
            await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{guestId}/confirm", null)
        ).EnsureSuccessStatusCode();

        return new Trip(ev.Id, owner, await MyId(owner), guest, guestId);
    }

    private static async Task<NewsPostResponse> Post(
        Trip t,
        bool requiresAck = false,
        bool pin = false,
        string title = "Deposit due"
    )
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest(title, "Pay by Friday.", requiresAck, pin)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private static async Task<NewsPostResponse> Get(HttpClient client, Trip t, int postId) =>
        (await client.GetFromJsonAsync<NewsPostResponse>($"{t.News}/{postId}"))!;

    private static string Reaction(Trip t, int postId, string emoji) =>
        $"{t.News}/{postId}/reactions/{Uri.EscapeDataString(emoji)}";

    private static async Task<List<TimelineEntryResponse>> Timeline(
        HttpClient client,
        Guid eventId
    ) =>
        (
            await client.GetFromJsonAsync<List<TimelineEntryResponse>>(
                $"/api/v1/events/{eventId}/timeline"
            )
        )!;

    [Fact]
    public async Task Acknowledge_IsIdempotent_AndKeepsFirstTime()
    {
        var t = await CreateTrip("1");
        var post = await Post(t, requiresAck: true);
        Assert.False(post.AcknowledgedByMe);

        var first = await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var acks = (await t.Owner.GetFromJsonAsync<NewsAcksResponse>($"{t.News}/{post.Id}/acks"))!;
        var at = Assert.Single(acks.Acknowledged).At;

        var second = await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        acks = (await t.Owner.GetFromJsonAsync<NewsAcksResponse>($"{t.News}/{post.Id}/acks"))!;
        Assert.Equal(at, Assert.Single(acks.Acknowledged).At);

        Assert.True((await Get(t.Guest, t, post.Id)).AcknowledgedByMe);
    }

    [Fact]
    public async Task Acknowledge_PostWithoutAck_Returns400()
    {
        var t = await CreateTrip("2");
        var post = await Post(t);

        var response = await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reactions_AreIdempotent()
    {
        var t = await CreateTrip("3");
        var post = await Post(t);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.PutAsync(Reaction(t, post.Id, "👍"), null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.PutAsync(Reaction(t, post.Id, "👍"), null)).StatusCode
        );
        (await t.Owner.PutAsync(Reaction(t, post.Id, "❤️"), null)).EnsureSuccessStatusCode();

        var seen = await Get(t.Guest, t, post.Id);
        Assert.Equal(
            [new NewsReactionCount("👍", 1), new NewsReactionCount("❤️", 1)],
            seen.Reactions
        );
        Assert.Equal(["👍"], seen.MyReactions);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.DeleteAsync(Reaction(t, post.Id, "👍"))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.DeleteAsync(Reaction(t, post.Id, "👍"))).StatusCode
        );
        seen = await Get(t.Guest, t, post.Id);
        Assert.Equal([new NewsReactionCount("❤️", 1)], seen.Reactions);
        Assert.Empty(seen.MyReactions);
    }

    [Theory]
    [InlineData("💩")]
    [InlineData("❤")] // ❤ without the variation selector
    [InlineData("thumbsup")]
    public async Task Reaction_OutsideTheList_Returns400(string emoji)
    {
        var t = await CreateTrip($"4-{emoji.GetHashCode():x}");
        var post = await Post(t);

        var response = await t.Guest.PutAsync(Reaction(t, post.Id, emoji), null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Acks_MissingListExcludesPendingMembers_AndIsOrganiserOnly()
    {
        var t = await CreateTrip("5");
        var pending = api.CreateClientWithUser("discord|newsx-pending-5");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var pendingId = await MyId(pending);
        var post = await Post(t, requiresAck: true);

        var acks = (await t.Owner.GetFromJsonAsync<NewsAcksResponse>($"{t.News}/{post.Id}/acks"))!;
        Assert.Empty(acks.Acknowledged);
        Assert.Equivalent(new[] { t.OwnerId, t.GuestId }, acks.Missing.Select(u => u.Id));
        Assert.DoesNotContain(acks.Missing, u => u.Id == pendingId);

        (await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null)).EnsureSuccessStatusCode();
        acks = (await t.Owner.GetFromJsonAsync<NewsAcksResponse>($"{t.News}/{post.Id}/acks"))!;
        Assert.Equal(t.GuestId, Assert.Single(acks.Acknowledged).User.Id);
        Assert.Equal(t.OwnerId, Assert.Single(acks.Missing).Id);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}/acks")).StatusCode
        );
    }

    [Fact]
    public async Task AckCounts_AreOrganiserOnly()
    {
        var t = await CreateTrip("6");
        var post = await Post(t, requiresAck: true);
        (await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null)).EnsureSuccessStatusCode();

        var asOrganiser = await Get(t.Owner, t, post.Id);
        Assert.Equal(1, asOrganiser.AckCount);
        Assert.Equal(2, asOrganiser.AckTotal);

        var asGuest = await Get(t.Guest, t, post.Id);
        Assert.Null(asGuest.AckCount);
        Assert.Null(asGuest.AckTotal);

        var raw = await t.Guest.GetStringAsync($"{t.News}/{post.Id}");
        Assert.Contains("\"ackCount\":null", raw);
    }

    [Fact]
    public async Task PinAndUnpin_AreLogged_AndPinnedPostsComeFirst()
    {
        var t = await CreateTrip("7");
        var older = await Post(t, title: "Older");
        await Post(t, title: "Newer");

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.PutAsync($"{t.News}/{older.Id}/pin", null)).StatusCode
        );
        // Pinning again changes nothing and logs nothing.
        (await t.Owner.PutAsync($"{t.News}/{older.Id}/pin", null)).EnsureSuccessStatusCode();

        var feed = (await t.Guest.GetFromJsonAsync<NewsFeedResponse>(t.News))!;
        Assert.Equal(["Older", "Newer"], feed.Posts.Select(p => p.Title));
        Assert.NotNull(feed.Posts[0].PinnedAt);

        var timeline = await Timeline(t.Guest, t.EventId);
        var pinned = Assert.Single(timeline, e => e.Type == "NewsPinned");
        Assert.Equal(older.Id, pinned.Data!.Value.GetProperty("postId").GetInt32());

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.DeleteAsync($"{t.News}/{older.Id}/pin")).StatusCode
        );
        timeline = await Timeline(t.Guest, t.EventId);
        Assert.Equal("NewsUnpinned", timeline[0].Type);

        feed = (await t.Guest.GetFromJsonAsync<NewsFeedResponse>(t.News))!;
        Assert.Equal(["Newer", "Older"], feed.Posts.Select(p => p.Title));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.PutAsync($"{t.News}/{older.Id}/pin", null)).StatusCode
        );
    }

    [Fact]
    public async Task PinnedPosts_AreNotRepeatedOnLaterPages()
    {
        var t = await CreateTrip("8");
        var pinned = await Post(t, pin: true, title: "Pinned");
        var a = await Post(t, title: "A");
        await Post(t, title: "B");

        var page1 = (await t.Guest.GetFromJsonAsync<NewsFeedResponse>($"{t.News}?limit=1"))!;
        Assert.Equal(["Pinned", "B"], page1.Posts.Select(p => p.Title));

        var last = page1.Posts[^1];
        var cursor = Uri.EscapeDataString(last.PublishedAt!.Value.ToString("O"));
        var page2 = (
            await t.Guest.GetFromJsonAsync<NewsFeedResponse>(
                $"{t.News}?limit=5&before={cursor}&beforeId={last.Id}"
            )
        )!;
        Assert.Equal([a.Id], page2.Posts.Select(p => p.Id));
        Assert.DoesNotContain(page2.Posts, p => p.Id == pinned.Id);
    }

    [Fact]
    public async Task Edit_KeepsAcks_UnlessAskedToReset()
    {
        var t = await CreateTrip("9");
        var post = await Post(t, requiresAck: true);
        (await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null)).EnsureSuccessStatusCode();

        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest("Deposit due", "Pay by Saturday.", RequiresAck: true)
            )
        ).EnsureSuccessStatusCode();
        Assert.True((await Get(t.Guest, t, post.Id)).AcknowledgedByMe);

        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest(
                    "Deposit due",
                    "Pay by Sunday.",
                    RequiresAck: true,
                    ResetAcks: true
                )
            )
        ).EnsureSuccessStatusCode();
        Assert.False((await Get(t.Guest, t, post.Id)).AcknowledgedByMe);
    }

    [Fact]
    public async Task Summary_ListsPinnedLatestAndUnacknowledged()
    {
        var t = await CreateTrip("10");
        var pinned = await Post(t, pin: true, title: "Pinned");
        var acked = await Post(t, requiresAck: true, title: "Acked");
        var open = await Post(t, requiresAck: true, title: "Open");
        var latest = await Post(t, title: "Latest");
        (await t.Guest.PostAsync($"{t.News}/{acked.Id}/ack", null)).EnsureSuccessStatusCode();

        var summary = (await t.Guest.GetFromJsonAsync<NewsSummaryResponse>($"{t.News}/summary"))!;
        Assert.Equal([pinned.Id], summary.Pinned.Select(p => p.Id));
        Assert.Equal(latest.Id, summary.Latest!.Id);
        Assert.Equal([open.Id], summary.NeedsAck.Select(p => p.Id));
    }

    [Fact]
    public async Task PendingAndNonMembers_CannotInteract()
    {
        var t = await CreateTrip("11");
        var post = await Post(t, requiresAck: true);
        var pending = api.CreateClientWithUser("discord|newsx-pending-11");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|newsx-stranger-11");

        foreach (
            var (client, expected) in new[]
            {
                (pending, HttpStatusCode.Forbidden),
                (stranger, HttpStatusCode.NotFound),
            }
        )
        {
            Assert.Equal(
                expected,
                (await client.PostAsync($"{t.News}/{post.Id}/ack", null)).StatusCode
            );
            Assert.Equal(
                expected,
                (await client.PutAsync(Reaction(t, post.Id, "👍"), null)).StatusCode
            );
            Assert.Equal(expected, (await client.GetAsync($"{t.News}/summary")).StatusCode);
        }
    }

    [Fact]
    public async Task Interactions_AreNotLoggedToTimeline()
    {
        var t = await CreateTrip("12");
        var post = await Post(t, requiresAck: true);
        var before = (await Timeline(t.Owner, t.EventId)).Count;

        (await t.Guest.PostAsync($"{t.News}/{post.Id}/ack", null)).EnsureSuccessStatusCode();
        (await t.Guest.PutAsync(Reaction(t, post.Id, "🎉"), null)).EnsureSuccessStatusCode();

        Assert.Equal(before, (await Timeline(t.Owner, t.EventId)).Count);
    }
}
