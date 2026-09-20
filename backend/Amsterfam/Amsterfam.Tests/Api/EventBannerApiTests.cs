using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Endpoints;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Api;

public class EventBannerApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];

    private static async Task<EventResponse> CreateEvent(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                name,
                null,
                new DateOnly(2030, 8, 1),
                new DateOnly(2030, 8, 7),
                "Amsterdam"
            )
        );
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    private static Task<HttpResponseMessage> Upload(
        HttpClient client,
        Guid eventId,
        byte[] data,
        string? fileName = null
    )
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var query = fileName is null ? "" : $"?fileName={Uri.EscapeDataString(fileName)}";
        return client.PutAsync($"/api/v1/events/{eventId}/banner{query}", content);
    }

    private static async Task<EventResponse> Reload(HttpClient client, Guid eventId) =>
        (await client.GetFromJsonAsync<EventResponse>($"/api/v1/events/{eventId}"))!;

    [Fact]
    public async Task Organiser_UploadsBanner_AndMembersCanFetchIt()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-1");
        var member = api.CreateClientWithUser("discord|banner-member-1");
        var ev = await CreateEvent(owner, "Banner 1");
        Assert.Null(ev.BannerFileId);
        await owner.TransitionThroughAsync(ev.Id, "Open");
        (await member.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();

        var upload = await Upload(owner, ev.Id, Png, "../../my banner.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var reloaded = await Reload(member, ev.Id);
        Assert.NotNull(reloaded.BannerFileId);

        var fetched = await member.GetAsync($"/api/v1/events/{ev.Id}/banner");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync());

        await using var db = await api.CreateDbContextAsync();
        var file = await db.EventFiles.SingleAsync(f => f.Id == reloaded.BannerFileId);
        Assert.Equal("my banner.png", file.FileName);
        Assert.Equal(Png.Length, file.Size);
    }

    [Fact]
    public async Task Upload_TrustsContentNotClientHeader()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-2");
        var ev = await CreateEvent(owner, "Banner 2");

        (await Upload(owner, ev.Id, Jpeg)).EnsureSuccessStatusCode();
        var fetched = await owner.GetAsync($"/api/v1/events/{ev.Id}/banner");
        Assert.Equal("image/jpeg", fetched.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("just some text")]
    [InlineData("")]
    public async Task Upload_RejectsNonImages(string body)
    {
        var owner = api.CreateClientWithUser($"discord|banner-owner-3-{body.Length}");
        var ev = await CreateEvent(owner, "Banner 3");

        var response = await Upload(owner, ev.Id, System.Text.Encoding.UTF8.GetBytes(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await Reload(owner, ev.Id)).BannerFileId);
    }

    [Fact]
    public async Task Upload_RejectsOversizedFile()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-4");
        var ev = await CreateEvent(owner, "Banner 4");

        var big = new byte[EventBannerEndpoints.MaxBannerBytes + 1];
        Png.CopyTo(big, 0);
        var response = await Upload(owner, ev.Id, big);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ReplacesPreviousBanner()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-5");
        var ev = await CreateEvent(owner, "Banner 5");

        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();
        var first = (await Reload(owner, ev.Id)).BannerFileId;
        (await Upload(owner, ev.Id, Jpeg)).EnsureSuccessStatusCode();
        var second = (await Reload(owner, ev.Id)).BannerFileId;

        Assert.NotEqual(first, second);
        await using var db = await api.CreateDbContextAsync();
        Assert.Equal(
            [second!.Value],
            await db.EventFiles.Where(f => f.EventId == ev.Id).Select(f => f.Id).ToListAsync()
        );
    }

    [Fact]
    public async Task Delete_RemovesBanner()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-6");
        var ev = await CreateEvent(owner, "Banner 6");
        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();

        var delete = await owner.DeleteAsync($"/api/v1/events/{ev.Id}/banner");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Null((await Reload(owner, ev.Id)).BannerFileId);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/v1/events/{ev.Id}/banner")).StatusCode
        );
        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.EventFiles.AnyAsync(f => f.EventId == ev.Id));
    }

    [Fact]
    public async Task Attendee_CannotChangeBanner()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-7");
        var attendee = api.CreateClientWithUser("discord|banner-attendee-7");
        var ev = await CreateEvent(owner, "Banner 7");
        await owner.TransitionThroughAsync(ev.Id, "Open");
        (await attendee.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(attendee, ev.Id, Png)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await attendee.DeleteAsync($"/api/v1/events/{ev.Id}/banner")).StatusCode
        );
    }

    [Fact]
    public async Task NonMember_CannotSeeOrChangeBanner()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-8");
        var stranger = api.CreateClientWithUser("discord|banner-stranger-8");
        var ev = await CreateEvent(owner, "Banner 8");
        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/events/{ev.Id}/banner")).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(stranger, ev.Id, Jpeg)).StatusCode);
    }

    [Fact]
    public async Task Upload_ToArchivedEvent_Returns409()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-9");
        var ev = await CreateEvent(owner, "Banner 9");
        await owner.TransitionThroughAsync(ev.Id, "Archived");

        Assert.Equal(HttpStatusCode.Conflict, (await Upload(owner, ev.Id, Png)).StatusCode);
    }

    [Fact]
    public async Task Get_SupportsConditionalRequests()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-10");
        var ev = await CreateEvent(owner, "Banner 10");
        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();

        var first = await owner.GetAsync($"/api/v1/events/{ev.Id}/banner");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events/{ev.Id}/banner");
        request.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await owner.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task JoinLinkPreview_ExposesBannerToNonMembers()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-11");
        var joiner = api.CreateClientWithUser("discord|banner-joiner-11");
        var ev = await CreateEvent(owner, "Banner 11");
        await owner.TransitionThroughAsync(ev.Id, "Open");
        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();

        var link = await (
            await owner.PostAsJsonAsync(
                $"/api/v1/events/{ev.Id}/join-links/",
                new CreateJoinLinkRequest(null, null, null)
            )
        ).Content.ReadFromJsonAsync<JoinLinkResponse>();

        var preview = await joiner.GetFromJsonAsync<JoinLinkPreviewResponse>(
            $"/api/v1/join-links/{link!.Token}"
        );
        Assert.NotNull(preview!.BannerFileId);

        var banner = await joiner.GetAsync($"/api/v1/join-links/{link.Token}/banner");
        Assert.Equal(HttpStatusCode.OK, banner.StatusCode);
        Assert.Equal(Png, await banner.Content.ReadAsByteArrayAsync());

        await owner.DeleteAsync($"/api/v1/events/{ev.Id}/join-links/{link.Id}");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await joiner.GetAsync($"/api/v1/join-links/{link.Token}/banner")).StatusCode
        );
    }

    [Fact]
    public async Task DeletingEvent_RemovesItsFiles()
    {
        var owner = api.CreateClientWithUser("discord|banner-owner-12");
        var ev = await CreateEvent(owner, "Banner 12");
        (await Upload(owner, ev.Id, Png)).EnsureSuccessStatusCode();
        await owner.TransitionThroughAsync(ev.Id, "Archived");

        var delete = await owner.DeleteAsync($"/api/v1/events/{ev.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        await using var db = await api.CreateDbContextAsync();
        Assert.False(await db.EventFiles.AnyAsync(f => f.EventId == ev.Id));
    }
}
