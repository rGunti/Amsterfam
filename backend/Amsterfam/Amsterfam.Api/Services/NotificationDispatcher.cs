using Amsterfam.Api.Endpoints;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Services;

/// <summary>
/// Works through the notification outbox (ADR-015): sends due rows one at a time, retries
/// failures with a backoff, and skips rows that no longer apply. Delivery is at least once; a
/// crash between sending and saving can repeat a message.
/// </summary>
public class NotificationDispatcher(
    AmsterfamDbContext db,
    IEnumerable<INotificationChannel> channels,
    ILogger<NotificationDispatcher> logger
)
{
    /// <summary>Sends every due row; returns how many went out.</summary>
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var due = await db
            .NotificationDispatches.Where(d =>
                d.Status == NotificationStatus.Pending && d.NextAttemptAt <= now
            )
            .OrderBy(d => d.NextAttemptAt)
            .ThenBy(d => d.Id)
            .Select(d => new { d.Id, d.Attempts })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in due)
        {
            try
            {
                if (await RunOneAsync(row.Id, row.Attempts, now, ct))
                    sent++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Notification {DispatchId} failed", row.Id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        if (sent > 0)
            logger.LogInformation("Sent {Count} notifications", sent);
        return sent;
    }

    /// <summary>
    /// For a post that's about to be deleted: drops its rows that haven't gone out, and turns
    /// sent messages that can be taken back into delete rows. Like <see cref="EventLog"/>, it
    /// only changes the context; the caller saves.
    /// </summary>
    public async Task RetractAsync(int postId, DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await db
            .NotificationDispatches.Where(d =>
                d.NewsPostId == postId && d.Action == NotificationAction.Send
            )
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var canDelete = channels.Any(c => c.Channel == row.Channel && c.CanDelete);
            if (row.Status == NotificationStatus.Sent && row.ExternalId is not null && canDelete)
            {
                row.Action = NotificationAction.Delete;
                row.Status = NotificationStatus.Pending;
                row.Attempts = 0;
                row.NextAttemptAt = now;
                row.LastError = null;
                row.SentAt = null;
            }
            else
            {
                db.NotificationDispatches.Remove(row);
            }
        }
    }

    private async Task<bool> RunOneAsync(
        long id,
        int seenAttempts,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        // Counting the attempt up front claims the row; a second instance matches nothing.
        var claimed = await db
            .NotificationDispatches.Where(d =>
                d.Id == id && d.Status == NotificationStatus.Pending && d.Attempts == seenAttempts
            )
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Attempts, seenAttempts + 1), ct);
        if (claimed == 0)
            return false;

        var dispatch = await db
            .NotificationDispatches.Include(d => d.Event)
            .Include(d => d.NewsPost)
            .SingleAsync(d => d.Id == id, ct);
        var channel = channels.FirstOrDefault(c => c.Channel == dispatch.Channel);

        if (
            (
                await SkipReasonAsync(dispatch, channel)
                ?? await channel!.SkipReasonAsync(dispatch, ct)
            ) is
            { } reason
        )
        {
            dispatch.Status = NotificationStatus.Skipped;
            dispatch.LastError = reason;
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }

        var ok = true;
        try
        {
            if (dispatch.Action == NotificationAction.Send)
            {
                var externalId = await channel!.SendAsync(
                    dispatch,
                    dispatch.NewsPost!,
                    dispatch.Event,
                    ct
                );
                // A cut-off id would point at the wrong message later; without one, the message
                // just can't be taken back.
                if (externalId?.Length > NotificationDispatch.MaxExternalIdLength)
                {
                    logger.LogWarning(
                        "Notification {DispatchId} to {Channel} got an id too long to keep",
                        dispatch.Id,
                        dispatch.Channel
                    );
                    externalId = null;
                }
                dispatch.ExternalId = externalId;
            }
            else
            {
                await channel!.DeleteAsync(dispatch, dispatch.Event, ct);
            }
            dispatch.Status = NotificationStatus.Sent;
            dispatch.SentAt = now;
            dispatch.LastError = null;
        }
        // An HTTP timeout is a cancellation too, but only stopping the run is a real one. When
        // the run is stopped mid-send, the attempt counted when claiming stays counted: the
        // send may have gone out, and one lost try is cheaper than a possible duplicate.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ok = false;
            dispatch.LastError = ex.Message[
                ..Math.Min(ex.Message.Length, NotificationDispatch.MaxErrorLength)
            ];
            if (dispatch.Attempts >= NotificationDispatch.MaxAttempts)
            {
                dispatch.Status = NotificationStatus.Failed;
                logger.LogWarning(
                    "Notification {DispatchId} to {Channel} gave up after {Attempts} attempts: {Error}",
                    dispatch.Id,
                    dispatch.Channel,
                    dispatch.Attempts,
                    ex.Message
                );
            }
            else
            {
                // A channel that was told how long to wait (a 429) waits exactly that long.
                dispatch.NextAttemptAt =
                    now
                    + (
                        ex is NotificationRateLimitedException limited
                            ? limited.RetryAfter
                            : NotificationDispatch.Backoff[dispatch.Attempts - 1]
                    );
                logger.LogInformation(
                    "Notification {DispatchId} to {Channel} failed, retrying at {NextAttemptAt}: {Error}",
                    dispatch.Id,
                    dispatch.Channel,
                    dispatch.NextAttemptAt,
                    ex.Message
                );
            }
        }

        // What was sent is recorded even if the run is being stopped, to avoid repeats.
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The post was deleted while this was being sent, and RetractAsync removed the row
            // without knowing about the message. Queue taking it back instead.
            await RetractLateAsync(dispatch, channel!, now);
        }
        return ok;
    }

    private async Task RetractLateAsync(
        NotificationDispatch sent,
        INotificationChannel channel,
        DateTimeOffset now
    )
    {
        db.ChangeTracker.Clear();
        if (
            sent.Action != NotificationAction.Send
            || sent.Status != NotificationStatus.Sent
            || sent.ExternalId is null
            || !channel.CanDelete
        )
            return;

        logger.LogInformation(
            "Notification {DispatchId}'s post was deleted while sending; queuing its removal",
            sent.Id
        );
        db.NotificationDispatches.Add(
            new NotificationDispatch
            {
                EventId = sent.EventId,
                Action = NotificationAction.Delete,
                Channel = sent.Channel,
                RecipientUserId = sent.RecipientUserId,
                Status = NotificationStatus.Pending,
                NextAttemptAt = now,
                CreatedAt = now,
                ExternalId = sent.ExternalId,
            }
        );
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<string?> SkipReasonAsync(
        NotificationDispatch dispatch,
        INotificationChannel? channel
    )
    {
        if (dispatch.Action == NotificationAction.Send)
        {
            if (dispatch.NewsPost is not { PublishedAt: not null })
                return "The post is no longer published.";
            if (EventStateMachine.IsHiddenFrom(dispatch.Event.Status, isOrganiser: false))
                return "The event is no longer visible to its members.";
            if (dispatch.RecipientUserId is { } userId)
            {
                var role = await EventGuards.RoleOf(db, dispatch.EventId, userId);
                if (!EventGuards.IsConfirmed(role))
                    return "The recipient is no longer a confirmed member.";
            }
        }

        if (channel is null)
            return "The channel isn't available.";
        if (!channel.IsConfigured(dispatch.Event))
            return "The channel is no longer set up for this event.";
        return null;
    }
}
