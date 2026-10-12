using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

/// <summary>Comments on news posts (#147).</summary>
public class NewsCommentApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Trip(
        Guid EventId,
        HttpClient Owner,
        int OwnerId,
        HttpClient Guest,
        int GuestId,
        HttpClient Other
    )
    {
        public string News => $"/api/v1/events/{EventId}/news";

        public string Comments(int postId) => $"{News}/{postId}/comments";
    }

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>An open event with its owner (an organiser) and two confirmed attendees.</summary>
    private async Task<Trip> CreateTrip(string suffix)
    {
        var owner = api.CreateClientWithUser($"discord|newsc-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|newsc-guest-{suffix}");
        var other = api.CreateClientWithUser($"discord|newsc-other-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"News Comments {suffix}",
                null,
                new DateOnly(2030, 7, 1),
                new DateOnly(2030, 7, 8),
                "Amsterdam"
            )
        );
        response.EnsureSuccessStatusCode();
        var ev = (await response.Content.ReadFromJsonAsync<EventResponse>())!;
        await owner.TransitionThroughAsync(ev.Id, "Open");

        foreach (var member in new[] { guest, other })
        {
            (await member.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();
            var id = await MyId(member);
            (
                await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{id}/confirm", null)
            ).EnsureSuccessStatusCode();
        }

        return new Trip(ev.Id, owner, await MyId(owner), guest, await MyId(guest), other);
    }

    private static async Task<NewsPostResponse> Post(Trip t, bool commentsEnabled = true)
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest(
                "Deposit due",
                "Pay by Friday.",
                CommentsEnabled: commentsEnabled
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private static Task<HttpResponseMessage> Comment(
        HttpClient client,
        Trip t,
        int postId,
        string body = "Which day exactly?"
    ) => client.PostAsJsonAsync(t.Comments(postId), new UpsertNewsCommentRequest(body));

    private static async Task<NewsCommentResponse> AddComment(
        HttpClient client,
        Trip t,
        int postId,
        string body = "Which day exactly?"
    )
    {
        var response = await Comment(client, t, postId, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsCommentResponse>())!;
    }

    private static async Task<List<NewsCommentResponse>> Comments(
        HttpClient client,
        Trip t,
        int postId,
        string query = ""
    ) => (await client.GetFromJsonAsync<List<NewsCommentResponse>>(t.Comments(postId) + query))!;

    private static async Task<NewsPostResponse> Get(HttpClient client, Trip t, int postId) =>
        (await client.GetFromJsonAsync<NewsPostResponse>($"{t.News}/{postId}"))!;

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
    public async Task ConfirmedMembers_Comment_AndReadOldestFirst()
    {
        var t = await CreateTrip("1");
        var post = await Post(t);

        var first = await AddComment(t.Guest, t, post.Id, "  Which day exactly?  ");
        Assert.Equal("Which day exactly?", first.Body);
        Assert.True(first.CanEdit);
        Assert.True(first.CanDelete);
        await AddComment(t.Owner, t, post.Id, "Friday the 3rd.");

        var list = await Comments(t.Other, t, post.Id);
        Assert.Equal(["Which day exactly?", "Friday the 3rd."], list.Select(c => c.Body));
        Assert.Equal(t.GuestId, list[0].Author.Id);
        Assert.All(list, c => Assert.False(c.CanEdit));
        Assert.All(list, c => Assert.False(c.CanDelete));

        // Organisers can delete anyone's comment but only edit their own.
        var asOwner = await Comments(t.Owner, t, post.Id);
        Assert.Equal([false, true], asOwner.Select(c => c.CanEdit));
        Assert.All(asOwner, c => Assert.True(c.CanDelete));
    }

    [Fact]
    public async Task Comments_PageForwardFromCursor()
    {
        var t = await CreateTrip("2");
        var post = await Post(t);
        for (var i = 1; i <= 3; i++)
            await AddComment(t.Guest, t, post.Id, $"Comment {i}");

        var page = await Comments(t.Guest, t, post.Id, "?limit=2");
        Assert.Equal(["Comment 1", "Comment 2"], page.Select(c => c.Body));

        var last = page[^1];
        var after = Uri.EscapeDataString(last.CreatedAt.ToString("O"));
        var rest = await Comments(t.Guest, t, post.Id, $"?after={after}&afterId={last.Id}");
        Assert.Equal(["Comment 3"], rest.Select(c => c.Body));
    }

    [Fact]
    public async Task PendingNonMembersAndReadOnlyEvents_AreRefused()
    {
        var t = await CreateTrip("3");
        var post = await Post(t);
        var pending = api.CreateClientWithUser("discord|newsc-pending-3");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|newsc-stranger-3");

        Assert.Equal(HttpStatusCode.Forbidden, (await Comment(pending, t, post.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.GetAsync(t.Comments(post.Id))).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await Comment(stranger, t, post.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync(t.Comments(post.Id))).StatusCode
        );

        var comment = await AddComment(t.Guest, t, post.Id);
        (await t.Owner.TransitionAsync(t.EventId, "Archived")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await Comment(t.Guest, t, post.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await t.Guest.PutAsJsonAsync(
                    $"{t.Comments(post.Id)}/{comment.Id}",
                    new UpsertNewsCommentRequest("Edited")
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.DeleteAsync($"{t.Comments(post.Id)}/{comment.Id}")).StatusCode
        );

        // Still readable, but no longer editable.
        var list = await Comments(t.Guest, t, post.Id);
        Assert.False(Assert.Single(list).CanEdit);
        Assert.False(list[0].CanDelete);
    }

    [Fact]
    public async Task DisabledOrUnpublishedPosts_RefuseComments()
    {
        var t = await CreateTrip("4");
        var closed = await Post(t, commentsEnabled: false);
        Assert.False(closed.CommentsEnabled);
        Assert.Equal(HttpStatusCode.BadRequest, (await Comment(t.Guest, t, closed.Id)).StatusCode);

        // Turning comments off later keeps existing ones readable but blocks edits.
        var post = await Post(t);
        var comment = await AddComment(t.Guest, t, post.Id);
        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest("Deposit due", "Pay by Friday.", CommentsEnabled: false)
            )
        ).EnsureSuccessStatusCode();
        Assert.False((await Get(t.Guest, t, post.Id)).CommentsEnabled);
        Assert.Single(await Comments(t.Guest, t, post.Id));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (
                await t.Guest.PutAsJsonAsync(
                    $"{t.Comments(post.Id)}/{comment.Id}",
                    new UpsertNewsCommentRequest("Edited")
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.DeleteAsync($"{t.Comments(post.Id)}/{comment.Id}")).StatusCode
        );

        // Scheduled posts (#141) aren't published yet.
        var draft = await Post(t);
        await using (var db = await api.CreateDbContextAsync())
            await db
                .NewsPosts.Where(p => p.Id == draft.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, (DateTimeOffset?)null));
        Assert.Equal(HttpStatusCode.NotFound, (await Comment(t.Guest, t, draft.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Guest.GetAsync(t.Comments(draft.Id))).StatusCode
        );
    }

    [Fact]
    public async Task Comment_ValidatesBody()
    {
        var t = await CreateTrip("5");
        var post = await Post(t);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Comment(t.Guest, t, post.Id, "  ")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Comment(t.Guest, t, post.Id, new string('x', 2001))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Created,
            (await Comment(t.Guest, t, post.Id, new string('x', 2000))).StatusCode
        );
    }

    [Fact]
    public async Task OnlyTheAuthor_CanEdit()
    {
        var t = await CreateTrip("6");
        var post = await Post(t);
        var comment = await AddComment(t.Guest, t, post.Id);
        var url = $"{t.Comments(post.Id)}/{comment.Id}";

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Owner.PutAsJsonAsync(url, new UpsertNewsCommentRequest("Hijacked"))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Other.PutAsJsonAsync(url, new UpsertNewsCommentRequest("Hijacked"))).StatusCode
        );

        var response = await t.Guest.PutAsJsonAsync(
            url,
            new UpsertNewsCommentRequest("Which Friday?")
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<NewsCommentResponse>())!;
        Assert.Equal("Which Friday?", edited.Body);
        Assert.NotNull(edited.EditedAt);
        Assert.Null(comment.EditedAt);
    }

    [Fact]
    public async Task AuthorAndOrganisers_CanDelete_OthersCannot()
    {
        var t = await CreateTrip("7");
        var post = await Post(t);
        var own = await AddComment(t.Guest, t, post.Id, "Mine");
        var moderated = await AddComment(t.Guest, t, post.Id, "Something rude");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Other.DeleteAsync($"{t.Comments(post.Id)}/{own.Id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.DeleteAsync($"{t.Comments(post.Id)}/{own.Id}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.DeleteAsync($"{t.Comments(post.Id)}/{moderated.Id}")).StatusCode
        );
        Assert.Empty(await Comments(t.Guest, t, post.Id));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Owner.DeleteAsync($"{t.Comments(post.Id)}/{moderated.Id}")).StatusCode
        );
    }

    [Fact]
    public async Task Moderation_IsLoggedForOrganisersOnly_WithoutTheText()
    {
        var t = await CreateTrip("8");
        var post = await Post(t);
        var ownByOrganiser = await AddComment(t.Owner, t, post.Id, "Organiser note");
        var ownByGuest = await AddComment(t.Guest, t, post.Id, "Guest note");
        var moderated = await AddComment(t.Guest, t, post.Id, "Something rude");

        (
            await t.Owner.DeleteAsync($"{t.Comments(post.Id)}/{ownByOrganiser.Id}")
        ).EnsureSuccessStatusCode();
        (
            await t.Guest.DeleteAsync($"{t.Comments(post.Id)}/{ownByGuest.Id}")
        ).EnsureSuccessStatusCode();
        (
            await t.Owner.DeleteAsync($"{t.Comments(post.Id)}/{moderated.Id}")
        ).EnsureSuccessStatusCode();

        var entry = Assert.Single(
            await Timeline(t.Owner, t.EventId),
            e => e.Type == "NewsCommentRemoved"
        );
        Assert.Equal("Organisers", entry.Visibility);
        Assert.Equal(t.OwnerId, entry.Actor!.Id);
        Assert.Equal(t.GuestId, entry.Subject!.Id);
        Assert.Equal(post.Id, entry.Data!.Value.GetProperty("postId").GetInt32());
        Assert.Equal("Deposit due", entry.Data.Value.GetProperty("title").GetString());
        Assert.DoesNotContain("rude", entry.Data.Value.GetRawText());

        Assert.DoesNotContain(
            await Timeline(t.Guest, t.EventId),
            e => e.Type == "NewsCommentRemoved"
        );
    }

    [Fact]
    public async Task DeletingThePost_DeletesItsComments()
    {
        var t = await CreateTrip("9");
        var post = await Post(t);
        await AddComment(t.Guest, t, post.Id);
        await AddComment(t.Owner, t, post.Id);

        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();

        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.NewsComments.AnyAsync(c => c.PostId == post.Id));
    }

    [Fact]
    public async Task CommentCounts_LeaveOutOwnAndSeenComments()
    {
        var t = await CreateTrip("10");
        var post = await Post(t);
        Assert.Equal(0, post.CommentCount);
        Assert.True(post.CommentsEnabled);

        (await t.Guest.PostAsync($"{t.News}/seen", null)).EnsureSuccessStatusCode();
        await AddComment(t.Guest, t, post.Id, "Mine");
        await AddComment(t.Owner, t, post.Id, "Answer 1");
        await AddComment(t.Other, t, post.Id, "Answer 2");

        var forGuest = await Get(t.Guest, t, post.Id);
        Assert.Equal(3, forGuest.CommentCount);
        Assert.Equal(2, forGuest.NewCommentCount);

        (await t.Guest.PostAsync($"{t.News}/seen", null)).EnsureSuccessStatusCode();
        var afterSeen = await Get(t.Guest, t, post.Id);
        Assert.Equal(3, afterSeen.CommentCount);
        Assert.Equal(0, afterSeen.NewCommentCount);

        // The feed carries the same counts.
        var feed = (await t.Owner.GetFromJsonAsync<NewsFeedResponse>(t.News))!;
        var inFeed = Assert.Single(feed.Posts);
        Assert.Equal(3, inFeed.CommentCount);
        Assert.Equal(2, inFeed.NewCommentCount);
    }

    private static string Reaction(Trip t, int postId, int commentId, string emoji) =>
        $"{t.Comments(postId)}/{commentId}/reactions/{Uri.EscapeDataString(emoji)}";

    [Fact]
    public async Task CommentReactions_AreCountedPerEmoji_AndIdempotent()
    {
        var t = await CreateTrip("11");
        var post = await Post(t);
        var comment = await AddComment(t.Guest, t, post.Id);
        Assert.Empty(comment.Reactions);

        foreach (var client in new[] { t.Owner, t.Other, t.Other })
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PutAsync(Reaction(t, post.Id, comment.Id, "👍"), null)).StatusCode
            );
        (
            await t.Other.PutAsync(Reaction(t, post.Id, comment.Id, "❤️"), null)
        ).EnsureSuccessStatusCode();

        var asOther = Assert.Single(await Comments(t.Other, t, post.Id));
        Assert.Equal(
            [new NewsReactionCount("👍", 2), new NewsReactionCount("❤️", 1)],
            asOther.Reactions
        );
        Assert.Equal(["👍", "❤️"], asOther.MyReactions);
        Assert.Empty(Assert.Single(await Comments(t.Guest, t, post.Id)).MyReactions);

        (
            await t.Other.DeleteAsync(Reaction(t, post.Id, comment.Id, "👍"))
        ).EnsureSuccessStatusCode();
        // Removing a reaction you don't have is a no-op.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Guest.DeleteAsync(Reaction(t, post.Id, comment.Id, "👍"))).StatusCode
        );
        var after = Assert.Single(await Comments(t.Owner, t, post.Id));
        Assert.Equal(
            [new NewsReactionCount("👍", 1), new NewsReactionCount("❤️", 1)],
            after.Reactions
        );
        Assert.Equal(["👍"], after.MyReactions);

        // Reactions aren't timeline material.
        Assert.DoesNotContain(
            await Timeline(t.Owner, t.EventId),
            e => e.Type.Contains("Reaction", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task CommentReactions_AreRefusedOutsideTheRules()
    {
        var t = await CreateTrip("12");
        var post = await Post(t);
        var comment = await AddComment(t.Guest, t, post.Id);
        var other = await Post(t);
        var pending = api.CreateClientWithUser("discord|newsc-pending-12");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|newsc-stranger-12");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await t.Other.PutAsync(Reaction(t, post.Id, comment.Id, "💩"), null)).StatusCode
        );
        // The comment must belong to the post in the URL.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Other.PutAsync(Reaction(t, other.Id, comment.Id, "👍"), null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.PutAsync(Reaction(t, post.Id, comment.Id, "👍"), null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.PutAsync(Reaction(t, post.Id, comment.Id, "👍"), null)).StatusCode
        );

        // Still allowed with comments turned off: a reaction adds no text.
        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest("Deposit due", "Pay by Friday.", CommentsEnabled: false)
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Other.PutAsync(Reaction(t, post.Id, comment.Id, "👍"), null)).StatusCode
        );

        (await t.Owner.TransitionAsync(t.EventId, "Archived")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Other.PutAsync(Reaction(t, post.Id, comment.Id, "❤️"), null)).StatusCode
        );
    }

    [Fact]
    public async Task DeletingAComment_DeletesItsReactions()
    {
        var t = await CreateTrip("13");
        var post = await Post(t);
        var comment = await AddComment(t.Guest, t, post.Id);
        (
            await t.Other.PutAsync(Reaction(t, post.Id, comment.Id, "👍"), null)
        ).EnsureSuccessStatusCode();

        (
            await t.Guest.DeleteAsync($"{t.Comments(post.Id)}/{comment.Id}")
        ).EnsureSuccessStatusCode();

        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.NewsCommentReactions.AnyAsync(r => r.CommentId == comment.Id));
    }
}
