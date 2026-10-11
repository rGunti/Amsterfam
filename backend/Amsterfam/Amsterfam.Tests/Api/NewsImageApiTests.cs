using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class NewsImageApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];

    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest)
    {
        public string News => $"/api/v1/events/{EventId}/news";
    }

    /// <summary>An open event with its owner (an organiser) and one confirmed attendee.</summary>
    private async Task<Trip> CreateTrip(string suffix)
    {
        var owner = api.CreateClientWithUser($"discord|news-img-owner-{suffix}");
        var guest = api.CreateClientWithUser($"discord|news-img-guest-{suffix}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"News Image {suffix}",
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
        var guestId = (await guest.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;
        (
            await owner.PostAsync($"/api/v1/events/{ev.Id}/attendees/{guestId}/confirm", null)
        ).EnsureSuccessStatusCode();

        return new Trip(ev.Id, owner, guest);
    }

    private static async Task<NewsPostResponse> Post(Trip t)
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest("Packing", "Bring towels.")
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private static Task<HttpResponseMessage> Upload(
        HttpClient client,
        Trip t,
        int postId,
        byte[] data,
        string query = ""
    )
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return client.PutAsync($"{t.News}/{postId}/image{query}", content);
    }

    private static async Task<NewsPostResponse> Reload(HttpClient client, Trip t, int postId) =>
        (await client.GetFromJsonAsync<NewsPostResponse>($"{t.News}/{postId}"))!;

    private static async Task<List<TimelineEntryResponse>> Timeline(HttpClient client, Trip t) =>
        (
            await client.GetFromJsonAsync<List<TimelineEntryResponse>>(
                $"/api/v1/events/{t.EventId}/timeline"
            )
        )!;

    [Fact]
    public async Task Organiser_UploadsImage_AndMembersCanFetchIt()
    {
        var t = await CreateTrip("1");
        var post = await Post(t);
        Assert.Null(post.ImageFileId);

        var upload = await Upload(t.Owner, t, post.Id, Png, "?fileName=..%2F..%2Fbeach.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var reloaded = await Reload(t.Guest, t, post.Id);
        Assert.NotNull(reloaded.ImageFileId);

        var fetched = await t.Guest.GetAsync($"{t.News}/{post.Id}/image");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync());

        // The timeline preview points at the same file.
        var posted = (await Timeline(t.Guest, t)).First(e => e.Type == "NewsPosted");
        Assert.Equal(reloaded.ImageFileId, posted.News?.ImageFileId);

        await using var db = await api.CreateDbContextAsync();
        var file = await db.EventFiles.SingleAsync(f => f.Id == reloaded.ImageFileId);
        Assert.Equal("beach.png", file.FileName);
        Assert.Equal(t.EventId, file.EventId);
    }

    [Fact]
    public async Task Upload_TrustsContentNotClientHeader()
    {
        var t = await CreateTrip("2");
        var post = await Post(t);

        (await Upload(t.Owner, t, post.Id, Jpeg)).EnsureSuccessStatusCode();
        var fetched = await t.Guest.GetAsync($"{t.News}/{post.Id}/image");
        Assert.Equal("image/jpeg", fetched.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("GIF89a not allowed")]
    [InlineData("")]
    public async Task Upload_RejectsNonImages(string body)
    {
        var t = await CreateTrip($"3-{body.Length}");
        var post = await Post(t);

        var response = await Upload(t.Owner, t, post.Id, System.Text.Encoding.UTF8.GetBytes(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await Reload(t.Owner, t, post.Id)).ImageFileId);
    }

    [Fact]
    public async Task Upload_RejectsOversizedFile()
    {
        var t = await CreateTrip("4");
        var post = await Post(t);
        var big = new byte[ImageUpload.MaxBytes + 1];
        Png.CopyTo(big, 0);

        var response = await Upload(t.Owner, t, post.Id, big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Replacing_DeletesOldFile_AndLogsEdit()
    {
        var t = await CreateTrip("5");
        var post = await Post(t);

        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();
        var first = (await Reload(t.Owner, t, post.Id)).ImageFileId;
        (await Upload(t.Owner, t, post.Id, Jpeg)).EnsureSuccessStatusCode();
        var second = await Reload(t.Owner, t, post.Id);

        Assert.NotEqual(first, second.ImageFileId);
        Assert.NotNull(second.EditedAt);
        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.EventFiles.AnyAsync(f => f.Id == first));
        Assert.True(await db.EventFiles.AnyAsync(f => f.Id == second.ImageFileId));

        var edits = (await Timeline(t.Owner, t)).Where(e => e.Type == "NewsEdited").ToList();
        Assert.Equal(2, edits.Count);
        var data = edits[0].Data!.Value;
        Assert.Equal("image", data.GetProperty("changed")[0].GetString());
        Assert.Equal("Packing", data.GetProperty("title").GetString());
        Assert.False(data.TryGetProperty("body", out _));
    }

    [Fact]
    public async Task InitialUpload_IsNotAnEdit()
    {
        var t = await CreateTrip("6");
        var post = await Post(t);

        (await Upload(t.Owner, t, post.Id, Png, "?initial=true")).EnsureSuccessStatusCode();
        var reloaded = await Reload(t.Owner, t, post.Id);
        Assert.NotNull(reloaded.ImageFileId);
        Assert.Null(reloaded.EditedAt);
        Assert.DoesNotContain(await Timeline(t.Owner, t), e => e.Type == "NewsEdited");

        // Once there's an image, "initial" no longer hides the change.
        (await Upload(t.Owner, t, post.Id, Jpeg, "?initial=true")).EnsureSuccessStatusCode();
        Assert.NotNull((await Reload(t.Owner, t, post.Id)).EditedAt);
        Assert.Contains(await Timeline(t.Owner, t), e => e.Type == "NewsEdited");
    }

    [Fact]
    public async Task Delete_RemovesImageAndFile()
    {
        var t = await CreateTrip("7");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png, "?initial=true")).EnsureSuccessStatusCode();
        var imageId = (await Reload(t.Owner, t, post.Id)).ImageFileId;

        var response = await t.Owner.DeleteAsync($"{t.News}/{post.Id}/image");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var reloaded = await Reload(t.Owner, t, post.Id);
        Assert.Null(reloaded.ImageFileId);
        Assert.NotNull(reloaded.EditedAt);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.EventFiles.AnyAsync(f => f.Id == imageId));

        // Nothing left to remove: no-op.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
        Assert.Single(await Timeline(t.Owner, t), e => e.Type == "NewsEdited");
    }

    [Fact]
    public async Task DeletingPost_RemovesItsImage()
    {
        var t = await CreateTrip("8");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();
        var imageId = (await Reload(t.Owner, t, post.Id)).ImageFileId;

        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();

        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.EventFiles.AnyAsync(f => f.Id == imageId));
    }

    [Fact]
    public async Task Attendee_CanReadButNotChange()
    {
        var t = await CreateTrip("9");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Upload(t.Guest, t, post.Id, Jpeg)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await t.Guest.DeleteAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
    }

    [Fact]
    public async Task PendingAndNonMembers_AreRefused()
    {
        var t = await CreateTrip("10");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();
        var pending = api.CreateClientWithUser("discord|news-img-pending-10");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var stranger = api.CreateClientWithUser("discord|news-img-stranger-10");

        var image = $"{t.News}/{post.Id}/image";
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync(image)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await Upload(pending, t, post.Id, Jpeg)).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(image)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await Upload(stranger, t, post.Id, Jpeg)).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(image)).StatusCode);
    }

    [Fact]
    public async Task Writes_BlockedOnArchivedEvent()
    {
        var t = await CreateTrip("11");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();
        (await t.Owner.TransitionAsync(t.EventId, "Archived")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await Upload(t.Owner, t, post.Id, Jpeg)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await t.Guest.GetAsync($"{t.News}/{post.Id}/image")).StatusCode
        );
    }

    [Fact]
    public async Task Get_SupportsConditionalRequests()
    {
        var t = await CreateTrip("12");
        var post = await Post(t);
        (await Upload(t.Owner, t, post.Id, Png)).EnsureSuccessStatusCode();
        var image = $"{t.News}/{post.Id}/image";

        var first = await t.Guest.GetAsync(image);
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var request = new HttpRequestMessage(HttpMethod.Get, image);
        request.Headers.IfNoneMatch.Add(etag);
        var second = await t.Guest.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task UnknownPost_Returns404()
    {
        var t = await CreateTrip("13");
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(t.Owner, t, 999_999, Png)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await t.Guest.GetAsync($"{t.News}/999999/image")).StatusCode
        );
    }
}
