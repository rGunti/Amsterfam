namespace Amsterfam.Core.Entities;

/// <summary>
/// A member's private Atom feed link for an event's news (ADR-016). Only the SHA-256 hash of
/// the token is stored; the token itself is shown once. At most one active token per user
/// and event.
/// </summary>
public class NewsFeedToken
{
    /// <summary>Hex SHA-256 of the token.</summary>
    public const int TokenHashLength = 64;

    public int Id { get; set; }
    public int UserId { get; set; }
    public Guid EventId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public User User { get; set; } = null!;
    public Event Event { get; set; } = null!;
}
