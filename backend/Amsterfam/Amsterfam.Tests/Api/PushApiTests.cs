using System.Net;
using System.Net.Http.Json;
using Amsterfam.Api.Auth;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Amsterfam.Tests.Api;

public class PushApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Subscriptions = "/api/v1/me/push-subscriptions";

    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest, HttpClient Pending)
    {
        public string News => $"/api/v1/events/{EventId}/news";
    }

    private static DateTimeOffset Tomorrow => DateTimeOffset.UtcNow.AddDays(1);

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    private static SavePushSubscriptionRequest Subscription(string endpoint, string? name = null) =>
        new(endpoint, new PushSubscriptionKeys(PushTestKeys.P256dh, PushTestKeys.Auth), name);

    private static Task<HttpResponseMessage> Subscribe(
        HttpClient client,
        string endpoint,
        string? name = null
    ) => client.PutAsJsonAsync(Subscriptions, Subscription(endpoint, name));

    private static async Task<List<PushDeviceResponse>> Devices(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PushDeviceResponse>>(Subscriptions))!;

    private static Task<HttpResponseMessage> Unsubscribe(HttpClient client, string endpoint) =>
        client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, Subscriptions)
            {
                Content = JsonContent.Create(new DeletePushSubscriptionRequest(endpoint)),
            }
        );

    private async Task<List<PushSubscription>> Stored(string endpoint)
    {
        await using var db = await api.CreateDbContextAsync();
        return await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ToListAsync();
    }

    /// <summary>An open event with its owner, a confirmed attendee and a pending member.</summary>
    private async Task<Trip> CreateTrip(string n)
    {
        var owner = api.CreateClientWithUser($"discord|push-owner-{n}");
        var guest = api.CreateClientWithUser($"discord|push-guest-{n}");
        var pending = api.CreateClientWithUser($"discord|push-pending-{n}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Push Test {n}",
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
        (await pending.JoinAsync(api, ev.Id)).EnsureSuccessStatusCode();
        return new Trip(ev.Id, owner, guest, pending);
    }

    private static async Task<NewsPostResponse> Post(Trip t, bool notify = true)
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest("Dinner", "Table at **8**.", Notify: notify)
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private async Task<List<NotificationDispatch>> PushRows(Guid eventId)
    {
        await using var db = await api.CreateDbContextAsync();
        return await db
            .NotificationDispatches.Where(d =>
                d.EventId == eventId && d.Channel == NotificationChannel.Push
            )
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    private async Task Dispatch()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunAsync(Tomorrow);
    }

    /// <summary>The same app with the VAPID keys left out, so push is off.</summary>
    private HttpClient ClientWithPushOff(string externalId)
    {
        var client = api.WithWebHostBuilder(b => b.UseSetting("Push:VapidPrivateKey", ""))
            .CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, externalId);
        return client;
    }

    [Fact]
    public async Task Config_ReturnsThePublicKey()
    {
        var client = api.CreateClientWithUser("discord|push-config");

        var config = await client.GetFromJsonAsync<PushConfigResponse>("/api/v1/push/config");

        Assert.Equal(PushTestKeys.VapidPublicKey, config!.PublicKey);
    }

    [Fact]
    public async Task Config_RequiresSignIn()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/push/config");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Save_UpsertsByEndpoint()
    {
        var client = api.CreateClientWithUser("discord|push-upsert");
        var endpoint = PushTestKeys.Endpoint("upsert");

        Assert.Equal(HttpStatusCode.NoContent, (await Subscribe(client, endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Subscribe(client, endpoint)).StatusCode);

        var row = Assert.Single(await Stored(endpoint));
        Assert.Equal(await MyId(client), row.UserId);
        Assert.Equal(PushTestKeys.P256dh, row.P256dh);
    }

    [Fact]
    public async Task Save_MovesADeviceToItsNewUser()
    {
        var before = api.CreateClientWithUser("discord|push-device-a");
        var after = api.CreateClientWithUser("discord|push-device-b");
        var endpoint = PushTestKeys.Endpoint("shared-device");

        await Subscribe(before, endpoint);
        await Subscribe(after, endpoint);

        Assert.Equal(await MyId(after), Assert.Single(await Stored(endpoint)).UserId);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/x")]
    [InlineData("https://user@fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://evil.example/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("")]
    public async Task Save_RejectsEndpointsOutsideKnownPushServices(string endpoint)
    {
        var client = api.CreateClientWithUser("discord|push-bad-endpoint");

        var response = await Subscribe(client, endpoint);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/x")]
    [InlineData("https://web.push.apple.com/x")]
    [InlineData("https://wns2-db5p.notify.windows.com/w/?token=x")]
    public async Task Save_AcceptsTheBrowsersPushServices(string endpoint)
    {
        var client = api.CreateClientWithUser("discord|push-good-endpoint");

        var response = await Subscribe(client, endpoint);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("not base64!", PushTestKeys.Auth)]
    // Valid base64url, but not a 65-byte uncompressed point.
    [InlineData(PushTestKeys.Auth, PushTestKeys.Auth)]
    // Right length, wrong prefix (0x05 instead of 0x04).
    [InlineData(
        "BV9VCLs8NmxJrFP9ykhAXK6OVm32ahafY6jgkTxGkYOll9hR3dTv0iKHNJLvFKCOMDl4AM0frE_0LH8PJydkjIU",
        PushTestKeys.Auth
    )]
    // Auth secret of the wrong length.
    [InlineData(PushTestKeys.P256dh, "c2hvcnQ")]
    [InlineData(PushTestKeys.P256dh, null)]
    public async Task Save_RejectsMalformedKeys(string p256dh, string? auth)
    {
        var client = api.CreateClientWithUser("discord|push-bad-keys");

        var response = await client.PutAsJsonAsync(
            Subscriptions,
            new SavePushSubscriptionRequest(
                PushTestKeys.Endpoint("bad-keys"),
                new PushSubscriptionKeys(p256dh, auth)
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Devices_ListsOnlyMyOwnWithNames()
    {
        var me = api.CreateClientWithUser("discord|push-list-me");
        var other = api.CreateClientWithUser("discord|push-list-other");
        await Subscribe(me, PushTestKeys.Endpoint("list-phone"), "Chrome on Android");
        await Subscribe(me, PushTestKeys.Endpoint("list-laptop"));
        await Subscribe(other, PushTestKeys.Endpoint("list-other"));

        var devices = await Devices(me);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, d => d.Name == "Chrome on Android");
        Assert.Contains(devices, d => d.Name == "Unknown device");
        Assert.DoesNotContain(devices, d => d.Endpoint == PushTestKeys.Endpoint("list-other"));
    }

    [Fact]
    public async Task Save_KeepsTheNameWhenResaved()
    {
        var client = api.CreateClientWithUser("discord|push-keep-name");
        var endpoint = PushTestKeys.Endpoint("keep-name");
        await Subscribe(client, endpoint, "Firefox on Linux");

        await Subscribe(client, endpoint, "Something else");

        Assert.Equal("Firefox on Linux", Assert.Single(await Stored(endpoint)).Name);
    }

    [Fact]
    public async Task Rename_ChangesOnlyMyOwnDevice()
    {
        var me = api.CreateClientWithUser("discord|push-rename-me");
        var other = api.CreateClientWithUser("discord|push-rename-other");
        await Subscribe(me, PushTestKeys.Endpoint("rename"));
        var id = Assert.Single(await Devices(me)).Id;

        var forbidden = await other.PatchAsJsonAsync(
            $"{Subscriptions}/{id}",
            new RenamePushDeviceRequest("Mine now")
        );
        var renamed = await me.PatchAsJsonAsync(
            $"{Subscriptions}/{id}",
            new RenamePushDeviceRequest("  Work laptop  ")
        );
        var blank = await me.PatchAsJsonAsync(
            $"{Subscriptions}/{id}",
            new RenamePushDeviceRequest(" ")
        );

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Work laptop", Assert.Single(await Devices(me)).Name);
    }

    [Fact]
    public async Task Revoke_RemovesOnlyMyOwnDevice()
    {
        var me = api.CreateClientWithUser("discord|push-revoke-me");
        var other = api.CreateClientWithUser("discord|push-revoke-other");
        await Subscribe(me, PushTestKeys.Endpoint("revoke"));
        var id = Assert.Single(await Devices(me)).Id;

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.DeleteAsync($"{Subscriptions}/{id}")).StatusCode
        );
        Assert.Single(await Devices(me));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await me.DeleteAsync($"{Subscriptions}/{id}")).StatusCode
        );
        Assert.Empty(await Devices(me));
    }

    [Fact]
    public async Task Save_DropsTheLeastRecentlyUsedPastTheCap()
    {
        var client = api.CreateClientWithUser("discord|push-cap");
        for (var i = 0; i < PushSubscription.MaxPerUser; i++)
            await Subscribe(client, PushTestKeys.Endpoint($"cap-{i}"));
        // Device 0 is the oldest but was used recently, so device 1 is the one to go.
        await using (var db = await api.CreateDbContextAsync())
        {
            await db
                .PushSubscriptions.Where(s => s.Endpoint == PushTestKeys.Endpoint("cap-0"))
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.LastSuccessAt, DateTimeOffset.UtcNow)
                );
        }

        await Subscribe(client, PushTestKeys.Endpoint("cap-new"));

        var endpoints = (await Devices(client)).Select(d => d.Endpoint).ToList();
        Assert.Equal(PushSubscription.MaxPerUser, endpoints.Count);
        Assert.Contains(PushTestKeys.Endpoint("cap-0"), endpoints);
        Assert.Contains(PushTestKeys.Endpoint("cap-new"), endpoints);
        Assert.DoesNotContain(PushTestKeys.Endpoint("cap-1"), endpoints);
    }

    [Fact]
    public async Task Delete_RemovesOwnSubscription()
    {
        var client = api.CreateClientWithUser("discord|push-delete");
        var endpoint = PushTestKeys.Endpoint("delete");
        await Subscribe(client, endpoint);

        Assert.Equal(HttpStatusCode.NoContent, (await Unsubscribe(client, endpoint)).StatusCode);

        Assert.Empty(await Stored(endpoint));
    }

    [Fact]
    public async Task Delete_LeavesSomeoneElsesSubscription()
    {
        var owner = api.CreateClientWithUser("discord|push-delete-owner");
        var other = api.CreateClientWithUser("discord|push-delete-other");
        var endpoint = PushTestKeys.Endpoint("delete-other");
        await Subscribe(owner, endpoint);

        Assert.Equal(HttpStatusCode.NoContent, (await Unsubscribe(other, endpoint)).StatusCode);

        Assert.Single(await Stored(endpoint));
    }

    [Fact]
    public async Task Off_WhenNotConfigured()
    {
        var client = ClientWithPushOff("discord|push-off");
        var endpoint = PushTestKeys.Endpoint("off");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/push/config")).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await Subscribe(client, endpoint)).StatusCode);
        Assert.Empty(await Stored(endpoint));
    }

    [Fact]
    public async Task Off_PublishingQueuesNoPushRows()
    {
        var t = await CreateTrip("off");
        await Subscribe(t.Guest, PushTestKeys.Endpoint("off-guest"));
        var owner = ClientWithPushOff("discord|push-owner-off");

        var response = await owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest("Dinner", "Table at 8.", Notify: true)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(await PushRows(t.EventId));
    }

    [Fact]
    public async Task Publish_QueuesOneRowPerSubscribedConfirmedMember()
    {
        var t = await CreateTrip("queue");
        await Subscribe(t.Owner, PushTestKeys.Endpoint("queue-owner"));
        await Subscribe(t.Guest, PushTestKeys.Endpoint("queue-guest-phone"));
        await Subscribe(t.Guest, PushTestKeys.Endpoint("queue-guest-laptop"));
        await Subscribe(t.Pending, PushTestKeys.Endpoint("queue-pending"));

        await Post(t);

        // Not the author, not the pending member; one row for both of the guest's devices.
        var row = Assert.Single(await PushRows(t.EventId));
        Assert.Equal(await MyId(t.Guest), row.RecipientUserId);
    }

    [Fact]
    public async Task Publish_WithoutNotifyQueuesNothing()
    {
        var t = await CreateTrip("quiet");
        await Subscribe(t.Guest, PushTestKeys.Endpoint("quiet-guest"));

        await Post(t, notify: false);

        Assert.Empty(await PushRows(t.EventId));
    }

    [Fact]
    public async Task Dispatch_SendsToEveryDeviceWithVapid()
    {
        var t = await CreateTrip("send");
        var phone = PushTestKeys.Endpoint("send-phone");
        var laptop = PushTestKeys.Endpoint("send-laptop");
        await Subscribe(t.Guest, phone);
        await Subscribe(t.Guest, laptop);
        await Post(t);

        await Dispatch();

        var call = Assert.Single(api.Push.CallsTo(phone));
        Assert.StartsWith("vapid t=", call.Authorization);
        Assert.Contains($"k={PushTestKeys.VapidPublicKey}", call.Authorization);
        Assert.NotEmpty(call.Body);
        Assert.Single(api.Push.CallsTo(laptop));
        Assert.Equal(NotificationStatus.Sent, Assert.Single(await PushRows(t.EventId)).Status);
        Assert.NotNull(Assert.Single(await Stored(phone)).LastSuccessAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Dispatch_RemovesSubscriptionsThePushServiceForgot(HttpStatusCode status)
    {
        var t = await CreateTrip($"gone-{(int)status}");
        var endpoint = PushTestKeys.Endpoint($"gone-{(int)status}");
        await Subscribe(t.Guest, endpoint);
        api.Push.Failing[endpoint] = status;
        await Post(t);

        await Dispatch();

        Assert.Empty(await Stored(endpoint));
        Assert.Equal(NotificationStatus.Sent, Assert.Single(await PushRows(t.EventId)).Status);
    }

    [Fact]
    public async Task Dispatch_RetriesWhenNoDeviceGotIt()
    {
        var t = await CreateTrip("retry");
        var endpoint = PushTestKeys.Endpoint("retry");
        await Subscribe(t.Guest, endpoint);
        api.Push.Failing[endpoint] = HttpStatusCode.InternalServerError;
        await Post(t);

        await Dispatch();

        var row = Assert.Single(await PushRows(t.EventId));
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("500", row.LastError);
        Assert.DoesNotContain("fcm", row.LastError);
        Assert.Single(await Stored(endpoint));
    }

    [Fact]
    public async Task Dispatch_DoesNotRetryWhenOneDeviceGotIt()
    {
        var t = await CreateTrip("partial");
        var broken = PushTestKeys.Endpoint("partial-broken");
        var working = PushTestKeys.Endpoint("partial-working");
        await Subscribe(t.Guest, broken);
        await Subscribe(t.Guest, working);
        api.Push.Failing[broken] = HttpStatusCode.ServiceUnavailable;
        await Post(t);

        await Dispatch();

        Assert.Equal(NotificationStatus.Sent, Assert.Single(await PushRows(t.EventId)).Status);
    }

    [Fact]
    public async Task Dispatch_SkipsMembersWithoutDevicesLeft()
    {
        var t = await CreateTrip("no-devices");
        var endpoint = PushTestKeys.Endpoint("no-devices");
        await Subscribe(t.Guest, endpoint);
        await Post(t);
        await Unsubscribe(t.Guest, endpoint);

        await Dispatch();

        var row = Assert.Single(await PushRows(t.EventId));
        Assert.Equal(NotificationStatus.Skipped, row.Status);
        Assert.Contains("no devices", row.LastError);
    }

    [Fact]
    public async Task Dispatch_DropsSubscriptionsThatCantBeEncryptedFor()
    {
        var t = await CreateTrip("unusable");
        var broken = PushTestKeys.Endpoint("unusable-broken");
        var working = PushTestKeys.Endpoint("unusable-working");
        await Subscribe(t.Guest, broken);
        await Subscribe(t.Guest, working);
        // Keys that slipped past validation (e.g. stored before it was stricter).
        await using (var db = await api.CreateDbContextAsync())
        {
            await db
                .PushSubscriptions.Where(s => s.Endpoint == broken)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.P256dh, "AAAA"));
        }
        await Post(t);

        await Dispatch();

        Assert.Empty(await Stored(broken));
        Assert.Empty(api.Push.CallsTo(broken));
        Assert.Single(api.Push.CallsTo(working));
        Assert.Equal(NotificationStatus.Sent, Assert.Single(await PushRows(t.EventId)).Status);
    }

    [Fact]
    public async Task Dispatch_SkipsMembersWhoLeft()
    {
        var t = await CreateTrip("left");
        var endpoint = PushTestKeys.Endpoint("left");
        await Subscribe(t.Guest, endpoint);
        await Post(t);
        await using (var db = await api.CreateDbContextAsync())
        {
            var guestId = await MyId(t.Guest);
            await db
                .EventAttendances.Where(a => a.EventId == t.EventId && a.UserId == guestId)
                .ExecuteDeleteAsync();
        }

        await Dispatch();

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await PushRows(t.EventId)).Status);
        Assert.Empty(api.Push.CallsTo(endpoint));
    }
}
