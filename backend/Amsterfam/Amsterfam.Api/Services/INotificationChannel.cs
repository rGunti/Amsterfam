using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// Sends outbox rows to one outside channel (ADR-015). Called by
/// <see cref="NotificationDispatcher"/>, one row at a time; throwing means "try again later".
/// </summary>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    /// <summary>Whether a sent message can be taken back when its post is deleted.</summary>
    bool CanDelete { get; }

    /// <summary>Whether the event still has this channel set up.</summary>
    bool IsConfigured(Event ev);

    /// <summary>Sends the post; returns the channel's id for the message, if it has one.</summary>
    Task<string?> SendAsync(
        NotificationDispatch dispatch,
        NewsPost post,
        Event ev,
        CancellationToken ct
    );

    /// <summary>Removes the message <see cref="NotificationDispatch.ExternalId"/> points at.</summary>
    Task DeleteAsync(NotificationDispatch dispatch, Event ev, CancellationToken ct);
}
