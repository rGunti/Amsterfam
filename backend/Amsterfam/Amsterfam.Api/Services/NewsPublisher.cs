using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Services;

/// <summary>
/// The single place a news post goes live (ADR-014). Like <see cref="EventLog"/>,
/// <see cref="PublishAsync"/> only changes the context; the caller's SaveChanges stores the post and
/// its timeline entry together. The post must already have its id.
/// </summary>
public class NewsPublisher(AmsterfamDbContext db, EventLog log, ILogger<NewsPublisher> logger)
{
    /// <summary>A publish time closer than this counts as "now" rather than a schedule.</summary>
    public static readonly TimeSpan ScheduleThreshold = TimeSpan.FromMinutes(1);

    private static readonly EventStatus[] ReadOnlyStatuses = Enum.GetValues<EventStatus>()
        .Where(EventStateMachine.IsReadOnly)
        .ToArray();

    public static bool IsScheduled(DateTimeOffset publishAt, DateTimeOffset now) =>
        publishAt - now > ScheduleThreshold;

    /// <summary>
    /// Puts the post live. A scheduled post that was asked to be pinned carries that as
    /// <see cref="NewsPost.PinnedAt"/>; it's pinned for real (and logged) here. With
    /// <see cref="NewsPost.Notify"/>, it also queues the outside notifications (ADR-015).
    /// </summary>
    public async Task PublishAsync(
        NewsPost post,
        int actorId,
        DateTimeOffset now,
        CancellationToken ct = default
    )
    {
        post.PublishedAt = now;
        log.Record(
            post.EventId,
            EventLogType.NewsPosted,
            actorId,
            data: LogData(post),
            occurredAt: now
        );
        if (post.PinnedAt is not null)
        {
            post.PinnedAt = now;
            log.Record(
                post.EventId,
                EventLogType.NewsPinned,
                actorId,
                data: LogData(post),
                occurredAt: now
            );
        }
        if (post.Notify)
            await QueueNotificationsAsync(post, now, ct);
    }

    /// <summary>
    /// One row per group channel the event has set up. Personal channels (Telegram, push,
    /// email; later issues) add one row per confirmed member who opted in, except the author.
    /// </summary>
    private async Task QueueNotificationsAsync(
        NewsPost post,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var hasDiscord = await db
            .Events.Where(e => e.Id == post.EventId)
            .Select(e => e.DiscordWebhookUrl != null)
            .SingleAsync(ct);
        if (hasDiscord)
        {
            db.NotificationDispatches.Add(
                new NotificationDispatch
                {
                    EventId = post.EventId,
                    NewsPostId = post.Id,
                    Action = NotificationAction.Send,
                    Channel = NotificationChannel.Discord,
                    Status = NotificationStatus.Pending,
                    NextAttemptAt = now,
                    CreatedAt = now,
                }
            );
        }
    }

    /// <summary>
    /// Publishes every scheduled post that is due, as its author. Each post is claimed with a
    /// conditional UPDATE in its own transaction, so it goes out exactly once even when several
    /// instances run this at the same time. Posts on read-only events are left for organisers
    /// to see as overdue.
    /// </summary>
    public async Task<int> PublishDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var due = await db
            .NewsPosts.Where(p =>
                p.PublishedAt == null
                && p.PublishAt <= now
                && !ReadOnlyStatuses.Contains(p.Event.Status)
            )
            .OrderBy(p => p.PublishAt)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var published = 0;
        foreach (var id in due)
        {
            try
            {
                if (await PublishOneAsync(id, now, ct))
                    published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Publishing scheduled news post {PostId} failed", id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        if (published > 0)
            logger.LogInformation("Published {Count} scheduled news posts", published);
        return published;
    }

    private async Task<bool> PublishOneAsync(int id, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The row lock taken here makes a concurrent claim wait, then match nothing.
        var claimed = await db
            .NewsPosts.Where(p => p.Id == id && p.PublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, now), ct);
        if (claimed == 0)
            return false;

        var post = await db.NewsPosts.SingleAsync(p => p.Id == id, ct);
        await PublishAsync(post, post.AuthorId, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// What the timeline keeps about a post: never the body. <paramref name="changed"/> names
    /// what an edit touched, when that isn't the text (e.g. "image").
    /// </summary>
    public static object LogData(NewsPost post, string[]? changed = null) =>
        changed is null
            ? new { postId = post.Id, title = post.Title }
            : new
            {
                postId = post.Id,
                title = post.Title,
                changed,
            };

    /// <summary>Timeline data for a post that hasn't gone out yet: includes when it will.</summary>
    public static object ScheduledLogData(NewsPost post) =>
        new
        {
            postId = post.Id,
            title = post.Title,
            publishAt = post.PublishAt,
        };
}
