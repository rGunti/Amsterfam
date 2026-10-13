using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Amsterfam.Tests.Api;

public class NotificationOutboxApiTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private record Trip(Guid EventId, HttpClient Owner, HttpClient Guest, string WebhookId)
    {
        public string News => $"/api/v1/events/{EventId}/news";
        public string Webhook => $"/api/v1/events/{EventId}/discord-webhook";
    }

    // Postgres keeps microseconds, so compare whole seconds.
    private static DateTimeOffset Seconds(DateTimeOffset t) =>
        new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Offset);

    /// <summary>Dispatch runs pretend it's a day later, so every queued row is due.</summary>
    private static DateTimeOffset Tomorrow => Seconds(DateTimeOffset.UtcNow.AddDays(1));

    private static async Task<int> MyId(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse>("/api/v1/me/"))!.Id;

    /// <summary>
    /// An open event with its owner and one confirmed attendee, mirrored to Discord unless
    /// <paramref name="discord"/> is false.
    /// </summary>
    private async Task<Trip> CreateTrip(int n, bool discord = true)
    {
        var owner = api.CreateClientWithUser($"discord|outbox-owner-{n}");
        var guest = api.CreateClientWithUser($"discord|outbox-guest-{n}");
        var response = await owner.PostAsJsonAsync(
            "/api/v1/events/",
            new CreateEventRequest(
                $"Outbox Test {n}",
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

        var webhookId = $"9000{n}";
        var trip = new Trip(ev.Id, owner, guest, webhookId);
        if (discord)
        {
            (
                await owner.PutAsJsonAsync(
                    trip.Webhook,
                    new SetDiscordWebhookRequest(
                        $"https://discord.com/api/webhooks/{webhookId}/secret-token-{n}"
                    )
                )
            ).EnsureSuccessStatusCode();
        }
        return trip;
    }

    private static async Task<NewsPostResponse> Post(
        Trip t,
        bool notify = true,
        DateTimeOffset? publishAt = null,
        string title = "Dinner"
    )
    {
        var response = await t.Owner.PostAsJsonAsync(
            t.News,
            new UpsertNewsPostRequest(
                title,
                "Table at **8**.",
                PublishAt: publishAt,
                Notify: notify
            )
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewsPostResponse>())!;
    }

    private async Task<List<NotificationDispatch>> Rows(Guid eventId)
    {
        await using var db = await api.CreateDbContextAsync();
        return await db
            .NotificationDispatches.Where(d => d.EventId == eventId)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }

    private async Task<int> Dispatch(DateTimeOffset now)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<NotificationDispatcher>()
            .RunAsync(now);
    }

    private async Task<int> PublishDue(DateTimeOffset now)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NewsPublisher>().PublishDueAsync(now);
    }

    [Fact]
    public async Task Publishing_QueuesOneDiscordRow_WhichIsSentWithoutMentions()
    {
        var t = await CreateTrip(1);

        var post = await Post(t);

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(post.Id, row.NewsPostId);
        Assert.Equal(NotificationChannel.Discord, row.Channel);
        Assert.Equal(NotificationAction.Send, row.Action);
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Null(row.RecipientUserId);
        Assert.Empty(api.Discord.CallsTo(t.WebhookId));

        await Dispatch(Tomorrow);

        var call = Assert.Single(api.Discord.CallsTo(t.WebhookId));
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("?wait=true", call.Uri.Query);
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal(
            0,
            body.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength()
        );
        var content = body.RootElement.GetProperty("content").GetString()!;
        Assert.StartsWith("**Dinner**", content);
        Assert.Contains("Table at **8**.", content);
        Assert.EndsWith($"{ApiFixture.PublicUrl}/events/{t.EventId}/news/{post.Id}", content);

        row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.SentAt);
        Assert.NotNull(row.ExternalId);
    }

    [Fact]
    public async Task SentRows_AreNeverSentAgain()
    {
        var t = await CreateTrip(2);
        await Post(t);

        await Dispatch(Tomorrow);
        await Dispatch(Tomorrow.AddHours(1));

        Assert.Single(api.Discord.CallsTo(t.WebhookId));
    }

    [Fact]
    public async Task ScheduledPost_QueuesOnlyWhenPublished_AndOnlyOnce()
    {
        var t = await CreateTrip(3);
        var later = Seconds(DateTimeOffset.UtcNow.AddHours(2));
        await Post(t, publishAt: Seconds(DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Empty(await Rows(t.EventId));

        await PublishDue(later);
        await PublishDue(later);

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(later, row.NextAttemptAt);
    }

    [Fact]
    public async Task NotifyFalse_QueuesNothing()
    {
        var t = await CreateTrip(4);

        var post = await Post(t, notify: false);

        Assert.False(post.Notify);
        Assert.Empty(await Rows(t.EventId));
    }

    [Fact]
    public async Task WithoutWebhook_NothingIsQueued()
    {
        var t = await CreateTrip(5, discord: false);

        await Post(t);

        Assert.Empty(await Rows(t.EventId));
    }

    [Fact]
    public async Task EditingAPublishedPost_NotifiesNobody()
    {
        var t = await CreateTrip(6);
        var post = await Post(t);

        var response = await t.Owner.PutAsJsonAsync(
            $"{t.News}/{post.Id}",
            new UpsertNewsPostRequest("Dinner", "Table at **9** now.", Notify: true)
        );
        response.EnsureSuccessStatusCode();

        Assert.Single(await Rows(t.EventId));
    }

    [Fact]
    public async Task FailedSends_RetryWithBackoff_ThenFail()
    {
        var t = await CreateTrip(7);
        await Post(t);
        api.Discord.Failing[t.WebhookId] = HttpStatusCode.InternalServerError;
        var now = Tomorrow;

        await Dispatch(now);
        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(now.AddMinutes(1), row.NextAttemptAt);
        Assert.Contains("500", row.LastError);
        Assert.DoesNotContain("secret-token", row.LastError);

        // Not due yet: nothing happens.
        await Dispatch(now.AddSeconds(30));
        Assert.Single(api.Discord.CallsTo(t.WebhookId));

        now = now.AddMinutes(1);
        await Dispatch(now);
        Assert.Equal(now.AddMinutes(5), Assert.Single(await Rows(t.EventId)).NextAttemptAt);

        now = now.AddMinutes(5);
        await Dispatch(now);
        Assert.Equal(now.AddMinutes(30), Assert.Single(await Rows(t.EventId)).NextAttemptAt);

        now = now.AddMinutes(30);
        await Dispatch(now);
        row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Failed, row.Status);
        Assert.Equal(NotificationDispatch.MaxAttempts, row.Attempts);
        Assert.Equal(4, api.Discord.CallsTo(t.WebhookId).Count);

        await Dispatch(now.AddDays(1));
        Assert.Equal(4, api.Discord.CallsTo(t.WebhookId).Count);
    }

    [Fact]
    public async Task FailedSend_SucceedsOnRetry()
    {
        var t = await CreateTrip(8);
        await Post(t);
        api.Discord.Failing[t.WebhookId] = HttpStatusCode.BadGateway;
        await Dispatch(Tomorrow);

        api.Discord.Failing.TryRemove(t.WebhookId, out _);
        await Dispatch(Tomorrow.AddMinutes(1));

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Equal(2, row.Attempts);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task CancelledEvent_IsSkipped()
    {
        var t = await CreateTrip(9);
        await Post(t);
        await t.Owner.TransitionThroughAsync(t.EventId, "Cancelled");

        await Dispatch(Tomorrow);

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await Rows(t.EventId)).Status);
        Assert.Empty(api.Discord.CallsTo(t.WebhookId));
    }

    [Fact]
    public async Task RemovedWebhook_IsSkipped()
    {
        var t = await CreateTrip(10);
        await Post(t);
        (await t.Owner.DeleteAsync(t.Webhook)).EnsureSuccessStatusCode();

        await Dispatch(Tomorrow);

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await Rows(t.EventId)).Status);
        Assert.Empty(api.Discord.CallsTo(t.WebhookId));
    }

    [Fact]
    public async Task RecipientNoLongerConfirmed_IsSkipped()
    {
        var t = await CreateTrip(11);
        var post = await Post(t);
        var pending = api.CreateClientWithUser("discord|outbox-pending-11");
        (await pending.JoinAsync(api, t.EventId)).EnsureSuccessStatusCode();
        var pendingId = await MyId(pending);

        // Personal channels come in later issues; a row for one stands in for them here.
        await using (var db = await api.CreateDbContextAsync())
        {
            db.NotificationDispatches.Add(
                new NotificationDispatch
                {
                    EventId = t.EventId,
                    NewsPostId = post.Id,
                    Action = NotificationAction.Send,
                    Channel = NotificationChannel.Telegram,
                    RecipientUserId = pendingId,
                    Status = NotificationStatus.Pending,
                    NextAttemptAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            );
            await db.SaveChangesAsync();
        }

        await Dispatch(Tomorrow);

        var row = (await Rows(t.EventId)).Single(r => r.RecipientUserId == pendingId);
        Assert.Equal(NotificationStatus.Skipped, row.Status);
        Assert.Contains("confirmed member", row.LastError);
    }

    [Fact]
    public async Task DeletingAPost_DropsRowsThatHaventGoneOut()
    {
        var t = await CreateTrip(12);
        var post = await Post(t);

        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();
        await Dispatch(Tomorrow);

        Assert.Empty(await Rows(t.EventId));
        Assert.Empty(api.Discord.CallsTo(t.WebhookId));
    }

    [Fact]
    public async Task DeletingASentPost_DeletesTheDiscordMessage()
    {
        var t = await CreateTrip(13);
        var post = await Post(t);
        await Dispatch(Tomorrow);
        var messageId = Assert.Single(await Rows(t.EventId)).ExternalId;

        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationAction.Delete, row.Action);
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Null(row.NewsPostId);
        Assert.Equal(0, row.Attempts);

        await Dispatch(Tomorrow.AddMinutes(1));

        var call = api.Discord.CallsTo(t.WebhookId)[^1];
        Assert.Equal(HttpMethod.Delete, call.Method);
        Assert.EndsWith($"/messages/{messageId}", call.Uri.AbsolutePath);
        row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Sent, row.Status);
    }

    [Fact]
    public async Task DeletingAMessageThatIsAlreadyGone_CountsAsDone()
    {
        var t = await CreateTrip(14);
        var post = await Post(t);
        await Dispatch(Tomorrow);
        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();
        api.Discord.Failing[t.WebhookId] = HttpStatusCode.NotFound;

        await Dispatch(Tomorrow.AddMinutes(1));

        Assert.Equal(NotificationStatus.Sent, Assert.Single(await Rows(t.EventId)).Status);
    }

    [Fact]
    public async Task FailedDelete_RetriesWithBackoff()
    {
        var t = await CreateTrip(15);
        var post = await Post(t);
        await Dispatch(Tomorrow);
        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();
        api.Discord.Failing[t.WebhookId] = HttpStatusCode.ServiceUnavailable;
        var now = Tomorrow.AddMinutes(1);

        await Dispatch(now);

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationAction.Delete, row.Action);
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(now.AddMinutes(1), row.NextAttemptAt);
    }

    [Fact]
    public async Task DeleteAfterWebhookRemoved_IsSkipped()
    {
        var t = await CreateTrip(16);
        var post = await Post(t);
        await Dispatch(Tomorrow);
        (await t.Owner.DeleteAsync(t.Webhook)).EnsureSuccessStatusCode();
        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();

        await Dispatch(Tomorrow.AddMinutes(1));

        Assert.Equal(NotificationStatus.Skipped, Assert.Single(await Rows(t.EventId)).Status);
        Assert.Single(api.Discord.CallsTo(t.WebhookId));
    }

    [Fact]
    public async Task PostDeletedWhileSending_StillDeletesTheMessage()
    {
        var t = await CreateTrip(17);
        var post = await Post(t);
        // Discord answers only after the organiser has deleted the post.
        api.Discord.BeforeAnswer[t.WebhookId] = async () =>
            (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();

        await Dispatch(Tomorrow);
        api.Discord.BeforeAnswer.TryRemove(t.WebhookId, out _);

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationAction.Delete, row.Action);
        Assert.Equal(NotificationStatus.Pending, row.Status);
        Assert.NotNull(row.ExternalId);

        await Dispatch(Tomorrow.AddMinutes(1));

        var call = api.Discord.CallsTo(t.WebhookId)[^1];
        Assert.Equal(HttpMethod.Delete, call.Method);
        Assert.EndsWith($"/messages/{row.ExternalId}", call.Uri.AbsolutePath);
    }

    [Fact]
    public async Task OverlongMessageId_IsNotKept_SoNoWrongMessageIsDeleted()
    {
        var t = await CreateTrip(18);
        var post = await Post(t);
        api.Discord.MessageIds[t.WebhookId] = new string(
            '9',
            NotificationDispatch.MaxExternalIdLength + 1
        );

        await Dispatch(Tomorrow);

        var row = Assert.Single(await Rows(t.EventId));
        Assert.Equal(NotificationStatus.Sent, row.Status);
        Assert.Null(row.ExternalId);

        (await t.Owner.DeleteAsync($"{t.News}/{post.Id}")).EnsureSuccessStatusCode();
        await Dispatch(Tomorrow.AddMinutes(1));

        Assert.Empty(await Rows(t.EventId));
        Assert.DoesNotContain(api.Discord.CallsTo(t.WebhookId), c => c.Method == HttpMethod.Delete);
    }
}
