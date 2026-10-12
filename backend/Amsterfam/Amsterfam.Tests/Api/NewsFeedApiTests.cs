using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Xml.Linq;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class NewsFeedApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private record Trip(Guid EventId, HttpClient Owner, int OwnerId, HttpClient Guest, int GuestId)
    {
        public string News => $"/api/v1/events/{EventId}/news";
        public string FeedToken => $"{News}/feed-token";
    }

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>An open event with its owner (an organiser) and one confirmed attendee.</summary>
    private async Task<Trip> CreateTrip(string suffix)
    {
        var owner = api.CreateClientWithUser($"discord|feed-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|feed-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Feed Test {suffix}",
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

    private static async Task<string> CreateFeedUrl(HttpClient client, Trip t)
    {
        var response = await client.PostAsync(t.FeedToken, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<NewsFeedTokenCreated>())!;
        Assert.StartsWith($"{ApiFixture.PublicUrl}/api/v1/feeds/", created.Url);
        return created.Url;
    }

    /// <summary>Fetches the feed without any credentials, the way a feed reader would.</summary>
    private Task<HttpResponseMessage> Fetch(string url) =>
        api.CreateClient().GetAsync(new Uri(url).PathAndQuery);

    private static string TokenOf(string url) => url.Split('/')[^2];

    [Fact]
    public async Task ValidToken_ServesAtomAnonymously()
    {
        var t = await CreateTrip("1");
        var first = await Post(t, "First [docs](/events) and ![pic](https://x.test/a.png)", "One");
        var second = await Post(t, "Second <script>alert(1)</script>", null);

        // An unpublished post (e.g. scheduled) stays out of the feed.
        await using (var db = await api.CreateDbContextAsync())
        {
            db.NewsPosts.Add(
                new NewsPost
                {
                    EventId = t.EventId,
                    AuthorId = t.OwnerId,
                    Title = "Secret",
                    Body = "Not yet",
                    CreatedAt = DateTimeOffset.UtcNow,
                    PublishAt = DateTimeOffset.UtcNow.AddDays(1),
                }
            );
            await db.SaveChangesAsync();
        }

        var url = await CreateFeedUrl(t.Guest, t);
        var response = await Fetch(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/atom+xml", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl.MaxAge);
        Assert.NotNull(response.Headers.ETag);

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var feed = xml.Root!;
        Assert.Equal(Atom + "feed", feed.Name);
        Assert.Equal("Feed Test 1 — News", feed.Element(Atom + "title")!.Value);

        var entries = feed.Elements(Atom + "entry").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(
            $"{ApiFixture.PublicUrl}/events/{t.EventId}/news/{second.Id}",
            entries[0].Element(Atom + "id")!.Value
        );
        Assert.Equal(
            $"{ApiFixture.PublicUrl}/events/{t.EventId}/news/{first.Id}",
            entries[1].Element(Atom + "link")!.Attribute("href")!.Value
        );
        Assert.Equal("One", entries[1].Element(Atom + "title")!.Value);
        Assert.Equal("Second <script>alert(1)</script>", entries[0].Element(Atom + "title")!.Value);
        Assert.NotEmpty(entries[0].Element(Atom + "author")!.Element(Atom + "name")!.Value);

        var firstHtml = entries[1].Element(Atom + "content")!;
        Assert.Equal("html", firstHtml.Attribute("type")!.Value);
        Assert.Contains($"href=\"{ApiFixture.PublicUrl}/events\"", firstHtml.Value);
        Assert.DoesNotContain("<img", firstHtml.Value);
        Assert.Contains("href=\"https://x.test/a.png\"", firstHtml.Value);

        var secondHtml = entries[0].Element(Atom + "content")!.Value;
        Assert.DoesNotContain("<script>", secondHtml);
        Assert.Contains("&lt;script&gt;", secondHtml);
        Assert.Equal(
            "Second <script>alert(1)</script>",
            entries[0].Element(Atom + "summary")!.Value
        );
        Assert.DoesNotContain("Secret", xml.ToString());
    }

    [Fact]
    public async Task WrongToken_Returns404()
    {
        var response = await Fetch(
            $"{ApiFixture.PublicUrl}/api/v1/feeds/{SecureToken.New()}/news.atom"
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokedToken_Returns404()
    {
        var t = await CreateTrip("2");
        var url = await CreateFeedUrl(t.Guest, t);

        Assert.Equal(HttpStatusCode.NoContent, (await t.Guest.DeleteAsync(t.FeedToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(url)).StatusCode);
        var status = await t.Guest.GetFromJsonAsync<NewsFeedTokenStatus>(t.FeedToken);
        Assert.False(status!.Active);

        // Revoking again is harmless.
        Assert.Equal(HttpStatusCode.NoContent, (await t.Guest.DeleteAsync(t.FeedToken)).StatusCode);
    }

    [Fact]
    public async Task Regenerate_RevokesOldToken_KeepsOneActive()
    {
        var t = await CreateTrip("3");
        var old = await CreateFeedUrl(t.Guest, t);
        var fresh = await CreateFeedUrl(t.Guest, t);

        Assert.NotEqual(old, fresh);
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Fetch(fresh)).StatusCode);

        await using var db = await api.CreateDbContextAsync();
        var tokens = await db
            .NewsFeedTokens.Where(x => x.EventId == t.EventId && x.UserId == t.GuestId)
            .ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, x => x.RevokedAt == null);

        var status = await t.Guest.GetFromJsonAsync<NewsFeedTokenStatus>(t.FeedToken);
        Assert.True(status!.Active);
        Assert.NotNull(status.LastUsedAt);
    }

    [Fact]
    public async Task Token_IsStoredHashed()
    {
        var t = await CreateTrip("4");
        var token = TokenOf(await CreateFeedUrl(t.Guest, t));

        await using var db = await api.CreateDbContextAsync();
        var stored = await db.NewsFeedTokens.SingleAsync(x =>
            x.EventId == t.EventId && x.UserId == t.GuestId
        );
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash);
        Assert.Equal(SecureToken.Hash(token), stored.TokenHash);
    }

    [Fact]
    public async Task PendingMembersToken_Returns404()
    {
        var t = await CreateTrip("5");
        var pending = api.CreateClientWithUser("discord|feed-pending-5");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await pending.PostAsync(t.FeedToken, null)).StatusCode
        );

        // A token can't be created while pending; seed one to prove the feed re-checks the role.
        var token = SecureToken.New();
        await using (var db = await api.CreateDbContextAsync())
        {
            db.NewsFeedTokens.Add(
                new NewsFeedToken
                {
                    EventId = t.EventId,
                    UserId = await MyId(pending),
                    TokenHash = SecureToken.Hash(token),
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        var response = await Fetch($"{ApiFixture.PublicUrl}/api/v1/feeds/{token}/news.atom");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemovedMembersToken_Returns404_AndStaysRevoked()
    {
        var t = await CreateTrip("6");
        var url = await CreateFeedUrl(t.Guest, t);

        (
            await t.Owner.DeleteAsync($"/api/v1/events/{t.EventId}/attendees/{t.GuestId}")
        ).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(url)).StatusCode);

        // Confirmed again later, the old link stays dead.
        (await t.Guest.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        (
            await t.Owner.PostAsync(
                $"/api/v1/events/{t.EventId}/attendees/{t.GuestId}/confirm",
                null
            )
        ).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(url)).StatusCode);
    }

    [Fact]
    public async Task CancelledEvent_HidesFeedFromAttendees()
    {
        var t = await CreateTrip("7");
        var guestUrl = await CreateFeedUrl(t.Guest, t);
        var ownerUrl = await CreateFeedUrl(t.Owner, t);

        await t.Owner.TransitionThroughAsync(t.EventId, "Cancelled");

        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(guestUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Fetch(ownerUrl)).StatusCode);
    }

    [Fact]
    public async Task NonMember_CannotManageToken()
    {
        var t = await CreateTrip("8");
        var stranger = api.CreateClientWithUser("discord|feed-stranger-8");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.PostAsync(t.FeedToken, null)).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(t.FeedToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(t.FeedToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await api.CreateClient().PostAsync(t.FeedToken, null)).StatusCode
        );
    }

    [Fact]
    public async Task ETag_Returns304_UntilPostsChange()
    {
        var t = await CreateTrip("9");
        var post = await Post(t);
        var url = await CreateFeedUrl(t.Guest, t);
        var reader = api.CreateClient();
        var path = new Uri(url).PathAndQuery;

        async Task<HttpResponseMessage> Conditional(EntityTagHeaderValue etag)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.IfNoneMatch.Add(etag);
            return await reader.SendAsync(request);
        }

        var etag = (await reader.GetAsync(path)).Headers.ETag!;
        Assert.Equal(HttpStatusCode.NotModified, (await Conditional(etag)).StatusCode);

        (
            await t.Owner.PutAsJsonAsync(
                $"{t.News}/{post.Id}",
                new UpsertNewsPostRequest("Packing", "Bring towels and soap.")
            )
        ).EnsureSuccessStatusCode();
        var edited = await Conditional(etag);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedTag = edited.Headers.ETag!;
        Assert.NotEqual(etag.Tag, editedTag.Tag);

        var extra = await Post(t, "Another");
        var withExtra = (await reader.GetAsync(path)).Headers.ETag!;
        (await t.Owner.DeleteAsync($"{t.News}/{extra.Id}")).EnsureSuccessStatusCode();
        var afterDelete = (await reader.GetAsync(path)).Headers.ETag!;
        Assert.NotEqual(withExtra.Tag, afterDelete.Tag);
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static async Task UploadImage(Trip t, int postId, byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await t.Owner.PutAsync($"{t.News}/{postId}/image", content)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PostImage_IsInFeed_AndServedThroughTheToken()
    {
        var t = await CreateTrip("10");
        var withImage = await Post(t, "Look at this", "Photo");
        await UploadImage(t, withImage.Id, Png);
        var plain = await Post(t, "No picture", "Text");

        var url = await CreateFeedUrl(t.Guest, t);
        var xml = XDocument.Parse(await (await Fetch(url)).Content.ReadAsStringAsync());
        var entries = xml.Root!.Elements(Atom + "entry").ToList();
        var photo = entries.Single(e => e.Element(Atom + "title")!.Value == "Photo");
        var text = entries.Single(e => e.Element(Atom + "title")!.Value == "Text");

        var enclosure = photo
            .Elements(Atom + "link")
            .Single(l => l.Attribute("rel")!.Value == "enclosure");
        Assert.Equal("image/png", enclosure.Attribute("type")!.Value);
        var imageUrl = enclosure.Attribute("href")!.Value;
        var feedBase = url[..url.LastIndexOf('/')];
        Assert.StartsWith($"{feedBase}/images/{withImage.Id}.png?v=", imageUrl);
        Assert.Equal(Png.Length.ToString(), enclosure.Attribute("length")!.Value);
        Assert.Contains(
            "<img src=\"" + imageUrl.Replace("&", "&amp;") + "\"",
            photo.Element(Atom + "content")!.Value
        );
        Assert.DoesNotContain(
            text.Elements(Atom + "link"),
            l => l.Attribute("rel")!.Value == "enclosure"
        );
        Assert.DoesNotContain("<img", text.Element(Atom + "content")!.Value);

        // A reader fetches it without credentials.
        var image = await Fetch(imageUrl);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        var head = await api.CreateClient()
            .SendAsync(new HttpRequestMessage(HttpMethod.Head, new Uri(imageUrl).PathAndQuery));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(Png.Length, head.Content.Headers.ContentLength);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Fetch($"{feedBase}/images/{plain.Id}.png")).StatusCode
        );

        // Revoking the feed link cuts off the image too.
        (await t.Guest.DeleteAsync(t.FeedToken)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(imageUrl)).StatusCode);
    }

    [Fact]
    public async Task FeedImage_OnlyServesPostsOfTheTokensEvent()
    {
        var mine = await CreateTrip("11");
        var other = await CreateTrip("12");
        var foreign = await Post(other, "Elsewhere", "Other");
        await UploadImage(other, foreign.Id, Png);

        var url = await CreateFeedUrl(mine.Guest, mine);
        var feedBase = url[..url.LastIndexOf('/')];
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Fetch($"{feedBase}/images/{foreign.Id}.png")).StatusCode
        );
    }

    [Fact]
    public void Html_EscapesRawHtml_AbsolutisesLinks_AndDropsImagesAndUnsafeLinks()
    {
        var html = NewsText.Html(
            "Hi <b>there</b> [rel](/events/1) [page](news) ![](https://x.test/a.png) "
                + "[bad](javascript:alert(1)) [ok](https://example.com)",
            "https://app.test/"
        );

        Assert.Contains("&lt;b&gt;there&lt;/b&gt;", html);
        Assert.Contains("href=\"https://app.test/events/1\"", html);
        Assert.Contains("href=\"https://app.test/news\"", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains(">https://x.test/a.png</a>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("bad", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }
}
