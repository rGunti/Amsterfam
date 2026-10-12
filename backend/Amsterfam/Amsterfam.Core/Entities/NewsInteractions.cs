namespace Amsterfam.Core.Entities;

/// <summary>A member's "Got it" on a post that asks for acknowledgement (#139).</summary>
public class NewsAcknowledgement
{
    public int PostId { get; set; }
    public int UserId { get; set; }
    public DateTimeOffset AckedAt { get; set; }

    public NewsPost Post { get; set; } = null!;
    public User User { get; set; } = null!;
}

/// <summary>A member's emoji reaction to a post. One row per user and emoji.</summary>
public class NewsReaction
{
    public const int MaxEmojiLength = 16;

    /// <summary>
    /// The only emoji accepted. A fixed list avoids grapheme validation and abuse; compared
    /// ordinally, so ❤️ must include its variation selector.
    /// </summary>
    public static readonly IReadOnlyList<string> Allowed = ["👍", "❤️", "😂", "🎉", "😮", "🙏"];

    public int PostId { get; set; }
    public int UserId { get; set; }
    public required string Emoji { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public NewsPost Post { get; set; } = null!;
    public User User { get; set; } = null!;

    public static bool IsAllowed(string emoji) => Allowed.Contains(emoji, StringComparer.Ordinal);
}

/// <summary>
/// A member's plain-text comment on a post (#147). Kept when the author leaves the event;
/// deleted with the post.
/// </summary>
public class NewsComment
{
    public const int MaxBodyLength = 2000;

    public int Id { get; set; }
    public int PostId { get; set; }
    public int AuthorId { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }

    public NewsPost Post { get; set; } = null!;
    public User Author { get; set; } = null!;
}
