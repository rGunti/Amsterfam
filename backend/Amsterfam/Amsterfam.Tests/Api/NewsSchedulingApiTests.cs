using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Amsterfam.Tests.Api;

public class NewsSchedulingApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Trip(Guid EventId, HttpClient Owner, int OwnerId, HttpClient Guest, int GuestId)
    {
        public string News => $"/api/v1/events/{EventId}/news";
    }

    /// <summary>Posts are scheduled an hour ahead; "due" runs pretend it's two hours later.</summary>
    private static DateTimeOffset InAnHour => Seconds(DateTimeOffset.UtcNow.AddHours(1));

    private static DateTimeOffset Later => Seconds(DateTimeOffset.UtcNow.AddHours(2));

    // Postgres keeps microseconds, so compare whole seconds.
    private static DateTimeOffset Seconds(DateTimeOffset t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>An open event with its owner (an organiser) and one confirmed attendee.</summary>
    private async Task<Trip> CreateTrip(string suffix)
    {
        var owner = api.CreateClientWithUser($"discord|sched-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|sched-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Scheduling Test {suffix}",
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

    private static async Task<NewsPostResponse> Schedule(
        Trip t,
        DateTimeOffset? publishAt,
        bool pin = false,
        string title = "Dinner"
    )
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest(title, "Table at **8**.", Pin: pin, PublishAt: publishAt)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private static async Task<List<NewsPostResponse>> Scheduled(Trip t) =>
        (await t.Owner.GetFromJsonAsync<List<NewsPostResponse>>($"{t.News}/scheduled"))!;

    private static async Task<NewsFeedResponse> Feed(HttpClient client, Trip t) =>
        (await client.GetFromJsonAsync<NewsFeedResponse>(t.News))!;

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

    private async Task<int> PublishDue(DateTimeOffset now)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NewsPublisher>().PublishDueAsync(now);
    }

    [Fact]
    public async Task FuturePost_IsOnlyVisibleToOrganisers()
    {
        var t = await CreateTrip("1");
        var at = InAnHour;

        var post = await Schedule(t, at);

        Assert.Null(post.PublishedAt);
        Assert.Equal(at, post.PublishAt);
        Assert.Equal(post.Id, Assert.Single(await Scheduled(t)).Id);
        Assert.Equal(HttpStatusCode.OK, (await t.Owner.GetAsync($"{t.News}/{post.Id}")).StatusCode);
        Assert.Empty((await Feed(t.Owner, t)).Posts);

        Assert.Empty((await Feed(t.Guest, t)).Posts);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.GetAsync($"{t.News}/scheduled")).StatusCode
        );
        var summary = await t.Guest.GetFromJsonAsync<NewsSummaryResponse>($"{t.News}/summary");
        Assert.Null(summary!.Latest);
        Assert.Equal(0, await Unread(t.Guest, t.EventId));
    }

    [Fact]
    public async Task ScheduledList_RejectsPendingAndStrangers()
    {
        var t = await CreateTrip("2");
        var pending = api.CreateClientWithUser("discord|sched-pending-2");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|sched-stranger-2");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.GetAsync($"{t.News}/scheduled")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"{t.News}/scheduled")).StatusCode
        );
    }

    [Fact]
    public async Task Scheduling_IsLoggedForOrganisersOnly()
    {
        var t = await CreateTrip("3");
        var post = await Schedule(t, InAnHour);

        var entry = Assert.Single(
            await Timeline(t.Owner, t.EventId),
            e => e.Type == "NewsScheduled"
        );
        Assert.Equal("Organisers", entry.Visibility);
        Assert.Equal(post.Id, entry.Data!.Value.GetProperty("postId").GetInt32());
        Assert.False(entry.Data!.Value.TryGetProperty("body", out _));
        Assert.DoesNotContain(await Timeline(t.Guest, t.EventId), e => e.Type.StartsWith("News"));
    }

    [Fact]
    public async Task PublishAtWithinAMinute_PublishesStraightAway()
    {
        var t = await CreateTrip("4");

        var post = await Schedule(t, DateTimeOffset.UtcNow.AddSeconds(30));

        Assert.NotNull(post.PublishedAt);
        Assert.Single((await Feed(t.Guest, t)).Posts);
        Assert.Empty(await Scheduled(t));
    }

    [Fact]
    public async Task ScheduledPost_CanBeRescheduledEditedAndDeleted_QuietlyForMembers()
    {
        var t = await CreateTrip("5");
        var post = await Schedule(t, InAnHour);
        var newAt = InAnHour.AddHours(3);

        var response = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("Lunch", "Table at **12**.", PublishAt: newAt)
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
        Assert.Equal(newAt, edited.PublishAt);
        Assert.Null(edited.EditedAt);
        Assert.Null(edited.PublishedAt);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).StatusCode
        );

        var log = await Timeline(t.Owner, t.EventId);
        Assert.Equal("Organisers", Assert.Single(log, e => e.Type == "NewsEdited").Visibility);
        Assert.Equal("Organisers", Assert.Single(log, e => e.Type == "NewsDeleted").Visibility);
        Assert.DoesNotContain(await Timeline(t.Guest, t.EventId), e => e.Type.StartsWith("News"));
    }

    [Fact]
    public async Task MovingScheduledPostToNow_PublishesIt()
    {
        var t = await CreateTrip("6");
        var post = await Schedule(t, InAnHour);

        var response = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("Dinner", "Table at **8**.", PublishAt: DateTimeOffset.UtcNow)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<NewsPostResponse>())!.PublishedAt);
        Assert.Single((await Feed(t.Guest, t)).Posts);
        Assert.Single(await Timeline(t.Guest, t.EventId), e => e.Type == "NewsPosted");
    }

    [Fact]
    public async Task PublishedPost_CannotBeRescheduled()
    {
        var t = await CreateTrip("7");
        var post = await Schedule(t, null);

        var moved = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("Dinner", "Table at **8**.", PublishAt: InAnHour)
        );
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);

        // Sending the time it already has is fine.
        var same = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("Dinner", "Table at **9**.", PublishAt: post.PublishAt)
        );
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task PublishDue_PublishesOnceAsTheAuthor()
    {
        var t = await CreateTrip("8");
        var post = await Schedule(t, InAnHour);
        var now = Later;

        Assert.True(await PublishDue(now) >= 1);
        Assert.Equal(0, await PublishDue(now));

        var feed = await Feed(t.Guest, t);
        var published = Assert.Single(feed.Posts);
        Assert.Equal(post.Id, published.Id);
        Assert.Equal(now, published.PublishedAt);
        Assert.Empty(await Scheduled(t));

        var entry = Assert.Single(await Timeline(t.Guest, t.EventId), e => e.Type == "NewsPosted");
        Assert.Equal(t.OwnerId, entry.Actor!.Id);
        Assert.Equal(now, entry.OccurredAt);
    }

    [Fact]
    public async Task PublishDue_RunningConcurrently_PublishesOnce()
    {
        var t = await CreateTrip("9");
        await Schedule(t, InAnHour);
        var now = Later;

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PublishDue(now)));

        Assert.Single(await Timeline(t.Guest, t.EventId), e => e.Type == "NewsPosted");
        Assert.Single((await Feed(t.Guest, t)).Posts);
    }

    [Fact]
    public async Task PublishDue_LeavesFuturePostsAlone()
    {
        var t = await CreateTrip("10");
        var post = await Schedule(t, DateTimeOffset.UtcNow.AddDays(30));

        await PublishDue(Later);

        Assert.Equal(post.Id, Assert.Single(await Scheduled(t)).Id);
        Assert.Empty((await Feed(t.Guest, t)).Posts);
    }

    [Fact]
    public async Task PublishDue_SkipsReadOnlyEvents()
    {
        var t = await CreateTrip("11");
        var post = await Schedule(t, InAnHour);
        (await t.Owner.TransitionAsync(t.EventId, "Archived")).EnsureSuccessStatusCode();

        await PublishDue(Later);

        // Still there for organisers, as an overdue post.
        var overdue = Assert.Single(await Scheduled(t));
        Assert.Equal(post.Id, overdue.Id);
        Assert.Null(overdue.PublishedAt);
        Assert.False(overdue.CanEdit);
        Assert.Empty((await Feed(t.Guest, t)).Posts);
    }

    [Fact]
    public async Task PublishDue_PinsPostsThatAskedForIt()
    {
        var t = await CreateTrip("12");
        var post = await Schedule(t, InAnHour, pin: true);
        Assert.Empty((await Feed(t.Owner, t)).Posts);
        var now = Later;

        await PublishDue(now);

        var published = Assert.Single((await Feed(t.Guest, t)).Posts);
        Assert.Equal(post.Id, published.Id);
        Assert.Equal(now, published.PinnedAt);
        Assert.Single(await Timeline(t.Guest, t.EventId), e => e.Type == "NewsPinned");
    }

    [Fact]
    public async Task Unread_IgnoresScheduledUntilPublished()
    {
        var t = await CreateTrip("13");
        await Schedule(t, InAnHour);
        Assert.Equal(0, await Unread(t.Guest, t.EventId));

        await PublishDue(Later);

        Assert.Equal(1, await Unread(t.Guest, t.EventId));
    }
}
