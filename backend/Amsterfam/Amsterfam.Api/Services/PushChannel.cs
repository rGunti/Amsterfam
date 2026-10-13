using System.Net;
using System.Text.Json;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Amsterfam.Api.Services;

/// <summary>
/// Sends news posts as web push notifications (ADR-017): one outbox row per member, sent to
/// each of their devices. The payload is the title, a short plain-text excerpt and a link back,
/// in the format the Angular service worker shows. A device the push service no longer knows
/// (404/410) is dropped. Errors never contain endpoints or keys.
/// </summary>
public class PushChannel(
    HttpClient http,
    AmsterfamDbContext db,
    TimeProvider time,
    IOptions<PushOptions> options,
    IOptions<AppOptions> app,
    ILogger<PushChannel> logger
) : INotificationChannel
{
    /// <summary>Push UIs cut the body off anyway; this keeps it to a glance.</summary>
    public const int MaxBodyLength = 140;

    /// <summary>A post that couldn't be delivered within a day is old news.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

    public NotificationChannel Channel => NotificationChannel.Push;

    public bool CanDelete => false;

    public bool IsConfigured(Event ev) => options.Value.IsEnabled;

    /// <summary>
    /// Sends to every device of the recipient. Throws (to be retried) only if no device got it
    /// and at least one failed for a reason worth retrying; a retry goes to all devices again,
    /// so it isn't worth a duplicate on the ones that already have it.
    /// </summary>
    public async Task<string?> SendAsync(
        NotificationDispatch dispatch,
        NewsPost post,
        Event ev,
        CancellationToken ct
    )
    {
        var subscriptions = await db
            .PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == dispatch.RecipientUserId)
            .ToListAsync(ct);

        var client = Client();
        var payload = Payload(post, ev);
        var delivered = 0;
        string? lastFailure = null;

        foreach (var subscription in subscriptions)
        {
            var message = new PushMessage(payload)
            {
                TimeToLive = (int)TimeToLive.TotalSeconds,
                Urgency = PushMessageUrgency.Normal,
            };
            try
            {
                await client.RequestPushMessageDeliveryAsync(ToWebPush(subscription), message, ct);
                delivered++;
                var now = time.GetUtcNow();
                await db
                    .PushSubscriptions.Where(s => s.Id == subscription.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSuccessAt, now), ct);
            }
            catch (PushServiceClientException ex)
                when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                logger.LogInformation(
                    "Push subscription {SubscriptionId} is gone ({Status}); removing it",
                    subscription.Id,
                    (int)ex.StatusCode
                );
                await db
                    .PushSubscriptions.Where(s => s.Id == subscription.Id)
                    .ExecuteDeleteAsync(ct);
            }
            // Anything else (an error status, a timeout, no connection) is worth retrying. The
            // message is our own: the library's could include the endpoint.
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastFailure = ex is PushServiceClientException push
                    ? $"The push service answered {(int)push.StatusCode} {push.StatusCode}."
                    : $"The push service couldn't be reached ({ex.GetType().Name}).";
                logger.LogInformation(
                    "Push to subscription {SubscriptionId} failed: {Error}",
                    subscription.Id,
                    lastFailure
                );
            }
        }

        if (delivered == 0 && lastFailure is not null)
            throw new HttpRequestException(lastFailure);
        return null;
    }

    public Task DeleteAsync(NotificationDispatch dispatch, Event ev, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>The ngsw notification payload: shown as is, a click opens the post.</summary>
    public string Payload(NewsPost post, Event ev) =>
        JsonSerializer.Serialize(
            new
            {
                notification = new
                {
                    title = post.Title ?? ev.Name,
                    body = NewsText.Excerpt(post.Body, MaxBodyLength).Text,
                    icon = "/icons/icon-192.png",
                    data = new
                    {
                        onActionClick = new
                        {
                            @default = new
                            {
                                operation = "navigateLastFocusedOrOpen",
                                url = app.Value.NewsPostUrl(ev.Id, post.Id),
                            },
                        },
                    },
                },
            }
        );

    private PushServiceClient Client() =>
        new(http)
        {
            DefaultAuthentication = new VapidAuthentication(
                options.Value.VapidPublicKey!,
                options.Value.VapidPrivateKey!
            )
            {
                Subject = options.Value.Subject,
            },
        };

    private static WebPushSubscription ToWebPush(Core.Entities.PushSubscription subscription)
    {
        var result = new WebPushSubscription { Endpoint = subscription.Endpoint };
        result.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        result.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        return result;
    }
}
