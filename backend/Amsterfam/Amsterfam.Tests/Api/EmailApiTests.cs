using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Amsterfam.Api.Auth;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Amsterfam.Tests.Api;

public partial class EmailApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private const string Settings = "/api/v1/me/notification-settings";
    private const string Address = "/api/v1/me/notification-email";

    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest, HttpClient Pending)
    {
        public string News => $"/api/v1/events/{EventId}/news";
    }

    private static DateTimeOffset Tomorrow => DateTimeOffset.UtcNow.AddDays(1);

    private static string LoginEmail(string externalId) => $"{externalId}@test.example";

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    private static async Task<NotificationSettingsResponse> GetSettings(HttpClient client) =>
        (await client.GetFromJsonAsync<NotificationSettingsResponse>(Settings))!;

    private static Task<HttpResponseMessage> SetOptIn(HttpClient client, bool on) =>
        client.PutAsJsonAsync(Settings, new UpdateNotificationSettingsRequest(on));

    private static Task<HttpResponseMessage> SetAddress(HttpClient client, string email) =>
        client.PutAsJsonAsync(Address, new SetNotificationEmailRequest(email));

    private static Task<HttpResponseMessage> Confirm(HttpClient client, string? token) =>
        client.PostAsJsonAsync($"{Address}/confirm", new ConfirmNotificationEmailRequest(token));

    [GeneratedRegex(@"confirm-email\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();

    private string LatestToken(string address)
    {
        var message = api.Email.SentTo(address).Last();
        return TokenPattern().Match(message.TextBody ?? "").Groups[1].Value;
    }

    /// <summary>An open event with its owner, a confirmed attendee and a pending member.</summary>
    private async Task<Trip> CreateTrip(string n, bool guestHasNoEmail = false)
    {
        var owner = api.CreateClientWithUser($"discord|mail-owner-{n}");
        var guest = api.CreateClientWithUser($"discord|mail-guest-{n}", noEmail: guestHasNoEmail);
        var pending = api.CreateClientWithUser($"discord|mail-pending-{n}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Mail Test {n}",
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
            new UpsertNewsPostRequest(
                "Dinner",
                "Table at **8**, see [the map](/map).",
                Notify: notify
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private async Task<List<NotificationDispatch>> EmailRows(Guid eventId)
    {
        await using var db = await api.CreateDbContextAsync();
        return await db
            .NotificationDispatches.Where(d =>
                d.EventId == eventId && d.Channel == NotificationChannel.Email
            )
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    private async Task Dispatch()
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunAsync(Tomorrow);
    }

    private async Task UpdateUser(HttpClient client, Action<User> change)
    {
        var id = await MyId(client);
        await using var db = await api.CreateDbContextAsync();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        change(user);
        await db.SaveChangesAsync();
    }

    private HttpClient ClientWithSmtpOff(string externalId)
    {
        var client = api.WithWebHostBuilder(b => b.UseSetting("Smtp:Host", "")).CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, externalId);
        return client;
    }

    // --- Settings ---

    [Fact]
    public async Task Settings_DefaultToOffWithTheLoginAddress()
    {
        var client = api.CreateClientWithUser("discord|mail-defaults");

        var settings = await GetSettings(client);

        Assert.True(settings.EmailAvailable);
        Assert.False(settings.EmailNotifications);
        Assert.Equal(LoginEmail("discord|mail-defaults"), settings.LoginEmail);
        Assert.Equal(LoginEmail("discord|mail-defaults"), settings.EffectiveEmail);
        Assert.Null(settings.NotificationEmail);
    }

    [Fact]
    public async Task Settings_OptInRoundTrips()
    {
        var client = api.CreateClientWithUser("discord|mail-roundtrip");

        Assert.Equal(HttpStatusCode.OK, (await SetOptIn(client, true)).StatusCode);
        Assert.True((await GetSettings(client)).EmailNotifications);

        Assert.Equal(HttpStatusCode.OK, (await SetOptIn(client, false)).StatusCode);
        Assert.False((await GetSettings(client)).EmailNotifications);
    }

    [Fact]
    public async Task Settings_FallbackAddressHidesLoginEmailAndCantOptIn()
    {
        var client = api.CreateClientWithUser("discord|mail-fallback", noEmail: true);

        var settings = await GetSettings(client);
        Assert.Null(settings.LoginEmail);
        Assert.Null(settings.EffectiveEmail);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetOptIn(client, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetOptIn(client, false)).StatusCode);
    }

    [Fact]
    public async Task Settings_ReportEmailUnavailableWhenSmtpIsOff()
    {
        var client = ClientWithSmtpOff("discord|mail-smtp-off");

        Assert.False((await GetSettings(client)).EmailAvailable);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SetAddress(client, "someone@example.com")).StatusCode
        );
    }

    [Fact]
    public async Task Settings_RequireSignIn()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await api.CreateClient().GetAsync(Settings)).StatusCode
        );
    }

    // --- Publishing and sending ---

    [Fact]
    public async Task Publish_QueuesOneRowPerOptedInConfirmedMember()
    {
        var t = await CreateTrip("queue");
        await SetOptIn(t.Owner, true);
        await SetOptIn(t.Guest, true);
        await SetOptIn(t.Pending, true);

        await Post(t);

        var row = Assert.Single(await EmailRows(t.EventId));
        Assert.Equal(await MyId(t.Guest), row.RecipientUserId);
        Assert.Equal(NotificationStatus.Pending, row.Status);
    }

    [Fact]
    public async Task Publish_SkipsMembersWhoDidntOptIn()
    {
        var t = await CreateTrip("no-opt-in");

        await Post(t);

        Assert.Empty(await EmailRows(t.EventId));
    }

    [Fact]
    public async Task Publish_WithoutNotifyQueuesNothing()
    {
        var t = await CreateTrip("no-notify");
        await SetOptIn(t.Guest, true);

        await Post(t, notify: false);

        Assert.Empty(await EmailRows(t.EventId));
    }

    [Fact]
    public async Task Publish_SkipsFallbackAddressesEvenWhenOptedIn()
    {
        var t = await CreateTrip("fallback", guestHasNoEmail: true);
        await UpdateUser(t.Guest, u => u.EmailNotifications = true);

        await Post(t);

        Assert.Empty(await EmailRows(t.EventId));
    }

    [Fact]
    public async Task Dispatch_SendsTheMessageAndMarksTheRowSent()
    {
        var t = await CreateTrip("send");
        await SetOptIn(t.Guest, true);
        var post = await Post(t);

        await Dispatch();

        var row = Assert.Single(await EmailRows(t.EventId));
        Assert.Equal(NotificationStatus.Sent, row.Status);
        var message = Assert.Single(api.Email.SentTo(LoginEmail("discord|mail-guest-send")));
        Assert.Equal(row.ExternalId, message.MessageId);
        Assert.Equal("[Mail Test send] Dinner", message.Subject);
        Assert.Contains(
            $"{ApiFixture.PublicUrl}/events/{t.EventId}/news/{post.Id}",
            message.TextBody
        );
        Assert.Contains($"{ApiFixture.PublicUrl}/profile", message.TextBody);
    }

    [Fact]
    public async Task Dispatch_SkipsMembersWhoOptedOutAfterQueueing()
    {
        var t = await CreateTrip("opt-out");
        await SetOptIn(t.Guest, true);
        await Post(t);
        await SetOptIn(t.Guest, false);

        await Dispatch();

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await EmailRows(t.EventId)).Status);
        Assert.Empty(api.Email.SentTo(LoginEmail("discord|mail-guest-opt-out")));
    }

    [Fact]
    public async Task Dispatch_SkipsMembersWhoLeft()
    {
        var t = await CreateTrip("left");
        await SetOptIn(t.Guest, true);
        await Post(t);
        var guestId = await MyId(t.Guest);
        await using (var db = await api.CreateDbContextAsync())
        {
            await db
                .EventAttendances.Where(a => a.EventId == t.EventId && a.UserId == guestId)
                .ExecuteDeleteAsync();
        }

        await Dispatch();

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await EmailRows(t.EventId)).Status);
    }

    [Fact]
    public async Task Dispatch_RetriesWhenTheMailServerFails()
    {
        var t = await CreateTrip("retry");
        await SetOptIn(t.Guest, true);
        await Post(t);
        var address = LoginEmail("discord|mail-guest-retry");
        api.Email.Failing[address] = true;

        await Dispatch();

        var row = Assert.Single(await EmailRows(t.EventId));
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("couldn't be reached", row.LastError);

        api.Email.Failing.TryRemove(address, out _);
        await using (var db = await api.CreateDbContextAsync())
        {
            await db
                .NotificationDispatches.Where(d => d.Id == row.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(d => d.NextAttemptAt, DateTimeOffset.UtcNow)
                );
        }
        await Dispatch();

        Assert.Equal(NotificationStatus.Sent, Assert.Single(await EmailRows(t.EventId)).Status);
        Assert.Single(api.Email.SentTo(address));
    }

    [Fact]
    public async Task Dispatch_FailsRightAwayWhenTheMailServerRejectsForGood()
    {
        var t = await CreateTrip("rejected");
        await SetOptIn(t.Guest, true);
        await Post(t);
        api.Email.Rejecting[LoginEmail("discord|mail-guest-rejected")] = true;

        await Dispatch();

        var row = Assert.Single(await EmailRows(t.EventId));
        Assert.Equal(NotificationStatus.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal("The mail server refused the message (550).", row.LastError);
    }

    // --- Custom address ---

    [Fact]
    public async Task CustomAddress_SendsAConfirmationLinkToTheNewAddress()
    {
        var client = api.CreateClientWithUser("discord|mail-custom-link");

        var response = await SetAddress(client, "custom-link@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = (await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>())!;
        Assert.Equal("custom-link@example.com", settings.NotificationEmail);
        Assert.False(settings.NotificationEmailConfirmed);
        Assert.Equal(LoginEmail("discord|mail-custom-link"), settings.EffectiveEmail);
        var message = Assert.Single(api.Email.SentTo("custom-link@example.com"));
        Assert.Contains($"{ApiFixture.PublicUrl}/profile/confirm-email?token=", message.TextBody);
        Assert.DoesNotContain(
            LatestToken("custom-link@example.com"),
            await client.GetStringAsync(Settings)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("Name <a@example.com>")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("someone@unknown")]
    public async Task CustomAddress_RejectsInvalidAddresses(string email)
    {
        var client = api.CreateClientWithUser("discord|mail-custom-invalid");

        Assert.Equal(HttpStatusCode.BadRequest, (await SetAddress(client, email)).StatusCode);
    }

    [Fact]
    public async Task CustomAddress_PendingKeepsNewsOnTheLoginAddress()
    {
        var t = await CreateTrip("pending-custom");
        await SetOptIn(t.Guest, true);
        await SetAddress(t.Guest, "pending-custom@example.com");

        await Post(t);
        await Dispatch();

        Assert.Single(api.Email.SentTo(LoginEmail("discord|mail-guest-pending-custom")));
        // Only the confirmation mail went to the unconfirmed address.
        Assert.Single(api.Email.SentTo("pending-custom@example.com"));
    }

    [Fact]
    public async Task CustomAddress_ConfirmedGetsTheNews()
    {
        var t = await CreateTrip("confirmed-custom");
        await SetOptIn(t.Guest, true);
        await SetAddress(t.Guest, "confirmed-custom@example.com");

        var response = await Confirm(t.Guest, LatestToken("confirmed-custom@example.com"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = (await response.Content.ReadFromJsonAsync<NotificationSettingsResponse>())!;
        Assert.True(settings.NotificationEmailConfirmed);
        Assert.Equal("confirmed-custom@example.com", settings.EffectiveEmail);

        await Post(t);
        await Dispatch();

        Assert.Equal(2, api.Email.SentTo("confirmed-custom@example.com").Count);
        Assert.Empty(api.Email.SentTo(LoginEmail("discord|mail-guest-confirmed-custom")));
    }

    [Fact]
    public async Task CustomAddress_LetsAFallbackLoginReceiveNews()
    {
        var t = await CreateTrip("fallback-custom", guestHasNoEmail: true);
        await SetAddress(t.Guest, "fallback-custom@example.com");
        // Pending counts, so they can opt in right away.
        Assert.Equal(HttpStatusCode.OK, (await SetOptIn(t.Guest, true)).StatusCode);
        await Confirm(t.Guest, LatestToken("fallback-custom@example.com"));

        await Post(t);
        await Dispatch();

        Assert.Equal(2, api.Email.SentTo("fallback-custom@example.com").Count);
    }

    [Fact]
    public async Task CustomAddress_SentAfterQueueingStillGetsTheNews()
    {
        var t = await CreateTrip("late-custom");
        await SetOptIn(t.Guest, true);
        await Post(t);
        await SetAddress(t.Guest, "late-custom@example.com");
        await Confirm(t.Guest, LatestToken("late-custom@example.com"));

        await Dispatch();

        Assert.Equal(2, api.Email.SentTo("late-custom@example.com").Count);
        Assert.Empty(api.Email.SentTo(LoginEmail("discord|mail-guest-late-custom")));
    }

    [Fact]
    public async Task CustomAddress_SomeoneElsesTokenIsRejected()
    {
        var alice = api.CreateClientWithUser("discord|mail-token-alice");
        var bob = api.CreateClientWithUser("discord|mail-token-bob");
        await SetAddress(alice, "token-alice@example.com");
        await SetAddress(bob, "token-bob@example.com");

        var response = await Confirm(bob, LatestToken("token-alice@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await GetSettings(alice)).NotificationEmailConfirmed);
        Assert.False((await GetSettings(bob)).NotificationEmailConfirmed);
    }

    [Fact]
    public async Task CustomAddress_ExpiredTokenIsRejected()
    {
        var client = api.CreateClientWithUser("discord|mail-token-expired");
        await SetAddress(client, "token-expired@example.com");
        var token = LatestToken("token-expired@example.com");
        await UpdateUser(
            client,
            u => u.NotificationEmailTokenSentAt = DateTimeOffset.UtcNow.AddHours(-2)
        );

        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, token)).StatusCode);
    }

    [Fact]
    public async Task CustomAddress_ChangingTheAddressVoidsTheOldLink()
    {
        var client = api.CreateClientWithUser("discord|mail-change");
        await SetAddress(client, "change-old@example.com");
        var oldToken = LatestToken("change-old@example.com");
        await PastCooldown(client);

        Assert.Equal(
            HttpStatusCode.OK,
            (await SetAddress(client, "change-new@example.com")).StatusCode
        );

        var settings = await GetSettings(client);
        Assert.False(settings.NotificationEmailConfirmed);
        Assert.Equal(LoginEmail("discord|mail-change"), settings.EffectiveEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await Confirm(client, oldToken)).StatusCode);
    }

    private Task PastCooldown(HttpClient client) =>
        UpdateUser(
            client,
            u => u.NotificationEmailTokenSentAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        );

    [Fact]
    public async Task CustomAddress_SettingAnAddressSharesTheCooldown()
    {
        var client = api.CreateClientWithUser("discord|mail-set-spam");
        await SetAddress(client, "set-spam-1@example.com");

        var same = await SetAddress(client, "set-spam-1@example.com");
        var other = await SetAddress(client, "set-spam-2@example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, same.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, other.StatusCode);
        Assert.Single(api.Email.SentTo("set-spam-1@example.com"));
        Assert.Empty(api.Email.SentTo("set-spam-2@example.com"));
        Assert.Equal("set-spam-1@example.com", (await GetSettings(client)).NotificationEmail);
    }

    [Fact]
    public async Task CustomAddress_ParallelRequestsSendOneMail()
    {
        var client = api.CreateClientWithUser("discord|mail-parallel");
        await GetSettings(client);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(i => SetAddress(client, $"parallel-{i}@example.com"))
        );

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Single(
            Enumerable.Range(0, 5).SelectMany(i => api.Email.SentTo($"parallel-{i}@example.com"))
        );
    }

    [Fact]
    public async Task CustomAddress_RefusedByTheMailServerIs400()
    {
        var client = api.CreateClientWithUser("discord|mail-refused");
        api.Email.Rejecting["refused@example.com"] = true;

        var response = await SetAddress(client, "refused@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CustomAddress_DeletingDoesNotResetTheCooldown()
    {
        var client = api.CreateClientWithUser("discord|mail-delete-spam");
        await SetAddress(client, "delete-spam-1@example.com");
        await client.DeleteAsync(Address);

        var response = await SetAddress(client, "delete-spam-2@example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(api.Email.SentTo("delete-spam-2@example.com"));
    }

    [Fact]
    public async Task CustomAddress_FailedSendsCountTowardsTheCooldown()
    {
        var client = api.CreateClientWithUser("discord|mail-fail-spam");
        api.Email.Failing["fail-spam@example.com"] = true;
        await SetAddress(client, "fail-spam@example.com");

        var response = await client.PostAsync($"{Address}/resend", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task CustomAddress_ResendIsRateLimited()
    {
        var client = api.CreateClientWithUser("discord|mail-resend");
        await SetAddress(client, "resend@example.com");

        var tooSoon = await client.PostAsync($"{Address}/resend", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);

        await PastCooldown(client);
        var later = await client.PostAsync($"{Address}/resend", null);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Equal(2, api.Email.SentTo("resend@example.com").Count);
    }

    [Fact]
    public async Task CustomAddress_ResendWithNothingPendingConflicts()
    {
        var client = api.CreateClientWithUser("discord|mail-resend-none");

        var response = await client.PostAsync($"{Address}/resend", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CustomAddress_FailedConfirmationMailIs503AndStaysPending()
    {
        var client = api.CreateClientWithUser("discord|mail-confirm-fails");
        api.Email.Failing["confirm-fails@example.com"] = true;

        var response = await SetAddress(client, "confirm-fails@example.com");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var settings = await GetSettings(client);
        Assert.Equal("confirm-fails@example.com", settings.NotificationEmail);
        Assert.False(settings.NotificationEmailConfirmed);
    }

    [Fact]
    public async Task CustomAddress_DeleteGoesBackToTheLoginAddress()
    {
        var client = api.CreateClientWithUser("discord|mail-delete");
        await SetAddress(client, "delete@example.com");
        await Confirm(client, LatestToken("delete@example.com"));

        var response = await client.DeleteAsync(Address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await GetSettings(client);
        Assert.Null(settings.NotificationEmail);
        Assert.Equal(LoginEmail("discord|mail-delete"), settings.EffectiveEmail);
    }

    [Fact]
    public async Task CustomAddress_ConfirmationMailNamesTheAccountButNoEvent()
    {
        var t = await CreateTrip("confirm-content");
        await SetAddress(t.Guest, "confirm-content@example.com");

        var message = Assert.Single(api.Email.SentTo("confirm-content@example.com"));

        Assert.Contains("mail-guest-confirm-content", message.TextBody);
        Assert.DoesNotContain("Mail Test", message.TextBody);
        Assert.IsType<MultipartAlternative>(message.Body);
    }
}
