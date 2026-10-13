namespace Amsterfam.Core.Entities;

/// <summary>
/// One message to an outside channel about a news post (ADR-015): written when the post is
/// published, sent later by the notification dispatcher. Deleting a post turns its sent rows
/// into <see cref="NotificationAction.Delete"/> rows, so they keep living without the post.
/// </summary>
public class NotificationDispatch
{
    public const int MaxAttempts = 4;
    public const int MaxErrorLength = 1000;
    public const int MaxExternalIdLength = 100;

    /// <summary>How long to wait after each failed attempt before the next one.</summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
    ];

    public long Id { get; set; }
    public Guid EventId { get; set; }

    /// <summary>Null once the post is deleted; only delete rows outlive it.</summary>
    public int? NewsPostId { get; set; }
    public NotificationAction Action { get; set; }
    public NotificationChannel Channel { get; set; }

    /// <summary>The member a personal channel goes to; null for group channels like Discord.</summary>
    public int? RecipientUserId { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>The channel's id for what was sent, e.g. the Discord message id.</summary>
    public string? ExternalId { get; set; }

    public Event Event { get; set; } = null!;
    public NewsPost? NewsPost { get; set; }
    public User? RecipientUser { get; set; }
}

public enum NotificationAction
{
    Send,
    Delete,
}

public enum NotificationChannel
{
    Discord,
    Telegram,
    Push,
    Email,
}

public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,
}
