namespace Amsterfam.Core.Entities;

/// <summary>
/// An announcement an organiser posts to an event's confirmed members (ADR-014). The body is
/// raw Markdown. A post is visible once <see cref="PublishedAt"/> is set.
/// </summary>
public class NewsPost
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 4000;

    public int Id { get; set; }
    public Guid EventId { get; set; }
    public int AuthorId { get; set; }
    public string? Title { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the post should go out; equal to <see cref="CreatedAt"/> unless scheduled.</summary>
    public DateTimeOffset PublishAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }

    // Used by later News issues (#139, #140, #142, #147); stored now to avoid extra migrations.
    public DateTimeOffset? PinnedAt { get; set; }
    public bool RequiresAck { get; set; }
    public bool Notify { get; set; } = true;
    public bool CommentsEnabled { get; set; } = true;
    public Guid? ImageFileId { get; set; }

    public Event Event { get; set; } = null!;
    public User Author { get; set; } = null!;
}
