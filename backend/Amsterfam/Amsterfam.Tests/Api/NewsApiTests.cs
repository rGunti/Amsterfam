using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Tests.Infrastructure;

namespace Amsterfam.Tests.Api;

public class NewsApiTests(ApiFixture api) : IClassFixture<ApiFixture>
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
        var owner = api.CreateClientWithUser($"discord|news-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|news-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"News Test {suffix}",
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
        string body = "Bring **towels**.",
        string? title = "Packing"
    )
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest(title, body)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private static async Task<NewsFeedResponse> Feed(HttpClient client, Trip t, string query = "")
    {
        var response = await client.GetAsync(t.News + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsFeedResponse>())!;
    }

    private static async Task<int?> Unread(HttpClient client, Guid eventId) =>
        (
            await client.GetFromJsonAsync<EventResponse>($"/api/v1/events/{eventId}")
        )!.UnreadNewsCount;

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
    public async Task Organiser_Posts_AndAttendeeReadsIt()
    {
        var t = await CreateTrip("1");

        var post = await Post(t);

        Assert.Equal("Packing", post.Title);
        Assert.Equal("Bring **towels**.", post.Body);
        Assert.Equal(t.OwnerId, post.Author.Id);
        Assert.NotNull(post.PublishedAt);
        Assert.True(post.CanEdit);

        var feed = await Feed(t.Guest, t);
        var seen = Assert.Single(feed.Posts);
        Assert.Equal(post.Id, seen.Id);
        Assert.False(seen.CanEdit);

        var single = await t.Guest.GetFromJsonAsync<NewsPostResponse>($"{t.News}/{post.Id}");
        Assert.Equal("Bring **towels**.", single!.Body);
    }

    [Fact]
    public async Task Attendee_CannotWrite()
    {
        var t = await CreateTrip("2");
        var post = await Post(t);
        var request = new UpsertNewsPostRequest(null, "Hi");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.PostAsJsonAsync(t.News, request)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.PutAsJsonAsync($"{t.News}/{post.Id}", request)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.DeleteAsync($"{t.News}/{post.Id}")).StatusCode
        );
    }

    [Fact]
    public async Task PendingAndNonMembers_CannotRead()
    {
        var t = await CreateTrip("3");
        var post = await Post(t);
        var pending = api.CreateClientWithUser("discord|news-pending-3");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|news-stranger-3");

        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync(t.News)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.GetAsync($"{t.News}/{post.Id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.PostAsync($"{t.News}/seen", null)).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(t.News)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"{t.News}/{post.Id}")).StatusCode
        );
        Assert.Null(await Unread(pending, t.EventId));
    }

    [Fact]
    public async Task Writes_BlockedOnArchivedEvent()
    {
        var t = await CreateTrip("4");
        var post = await Post(t);
        (await t.Owner.TransitionAsync(t.EventId, "Archived")).EnsureSuccessStatusCode();

        var request = new UpsertNewsPostRequest(null, "Too late");
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.PostAsJsonAsync(t.News, request)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.PutAsJsonAsync($"{t.News}/{post.Id}", request)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).StatusCode
        );

        // Still readable, but no longer editable.
        var feed = await Feed(t.Owner, t);
        Assert.False(Assert.Single(feed.Posts).CanEdit);
    }

    [Fact]
    public async Task Create_ValidatesBodyAndTitle()
    {
        var t = await CreateTrip("5");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await t.Owner.PostAsJsonAsync(t.News, new UpsertNewsPostRequest("Title", "   "))
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await t.Owner.PostAsJsonAsync(
                    t.News,
                    new UpsertNewsPostRequest(null, new string('x', 4001))
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await t.Owner.PostAsJsonAsync(
                    t.News,
                    new UpsertNewsPostRequest(new string('x', 121), "Body")
                )
            ).StatusCode
        );

        var blankTitle = await Post(t, "Body", "  ");
        Assert.Null(blankTitle.Title);
    }

    [Fact]
    public async Task Feed_PagesNewestFirst()
    {
        var t = await CreateTrip("6");
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
            ids.Add((await Post(t, $"Post {i}")).Id);

        var first = await Feed(t.Guest, t, "?limit=3");
        Assert.Equal(new[] { ids[4], ids[3], ids[2] }, first.Posts.Select(p => p.Id));

        var last = first.Posts[^1];
        var cursor = Uri.EscapeDataString(last.PublishedAt!.Value.ToString("O"));
        var second = await Feed(t.Guest, t, $"?limit=3&before={cursor}&beforeId={last.Id}");
        Assert.Equal(new[] { ids[1], ids[0] }, second.Posts.Select(p => p.Id));
    }

    [Fact]
    public async Task Unread_CountsOthersPostsUntilSeen()
    {
        var t = await CreateTrip("7");
        Assert.Equal(0, await Unread(t.Guest, t.EventId));

        await Post(t, "One");
        await Post(t, "Two");

        Assert.Equal(2, await Unread(t.Guest, t.EventId));
        // Your own posts never count.
        Assert.Equal(0, await Unread(t.Owner, t.EventId));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.PostAsync($"{t.News}/seen", null)).StatusCode
        );
        Assert.Equal(0, await Unread(t.Guest, t.EventId));
        Assert.NotNull((await Feed(t.Guest, t)).SeenAt);

        await Post(t, "Three");
        Assert.Equal(1, await Unread(t.Guest, t.EventId));
    }

    [Fact]
    public async Task Edit_UpdatesPost_AndLogsWithoutBody()
    {
        var t = await CreateTrip("8");
        var post = await Post(t, "Secret body text", "Old title");

        var response = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("New title", "Changed body text")
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
        Assert.Equal("New title", edited.Title);
        Assert.NotNull(edited.EditedAt);

        var timeline = await Timeline(t.Guest, t.EventId);
        Assert.Equal("NewsEdited", timeline[0].Type);
        Assert.Equal("New title", timeline[0].Data!.Value.GetProperty("title").GetString());
        Assert.Equal(post.Id, timeline[0].Data!.Value.GetProperty("postId").GetInt32());

        var raw = await t.Guest.GetStringAsync($"/api/v1/events/{t.EventId}/timeline");
        Assert.DoesNotContain("Secret body text", raw);
    }

    [Fact]
    public async Task Timeline_ShowsLivePreviewOfPostedNews()
    {
        var t = await CreateTrip("9");
        var longBody =
            "**Hello** [everyone](https://example.com)! "
            + string.Join(' ', Enumerable.Repeat("word", 100));
        var post = await Post(t, longBody, "Big news");

        var posted = (await Timeline(t.Guest, t.EventId)).First(e => e.Type == "NewsPosted");
        Assert.NotNull(posted.News);
        Assert.Equal(post.Id, posted.News!.PostId);
        Assert.Equal("Big news", posted.News.Title);
        Assert.StartsWith("Hello everyone! word", posted.News.Excerpt);
        Assert.True(posted.News.Truncated);
        Assert.Null(posted.News.ImageFileId);

        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest("Smaller news", "Short now.")
            )
        ).EnsureSuccessStatusCode();
        posted = (await Timeline(t.Guest, t.EventId)).First(e => e.Type == "NewsPosted");
        Assert.Equal("Smaller news", posted.News!.Title);
        Assert.Equal("Short now.", posted.News.Excerpt);
        Assert.False(posted.News.Truncated);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).StatusCode
        );
        var timeline = await Timeline(t.Guest, t.EventId);
        Assert.Equal("NewsDeleted", timeline[0].Type);
        Assert.Equal("Smaller news", timeline[0].Data!.Value.GetProperty("title").GetString());
        Assert.Null(timeline.First(e => e.Type == "NewsPosted").News);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}")).StatusCode
        );
        Assert.Empty((await Feed(t.Guest, t)).Posts);
    }

    [Fact]
    public void Excerpt_IsPlainText()
    {
        var (text, truncated) = NewsText.Excerpt(
            "# Heading\n\n<script>alert(1)</script>\n\n- one\n- *two*\n\n![pic](https://x.test/a.png)"
        );

        Assert.False(truncated);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("*", text);
        Assert.DoesNotContain("https://x.test", text);
        Assert.Contains("Heading", text);
        Assert.Contains("two", text);
        Assert.DoesNotContain("\n", text);
    }
}
