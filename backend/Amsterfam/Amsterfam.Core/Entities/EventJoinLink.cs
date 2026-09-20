namespace Amsterfam.Core.Entities;

/// <summary>
/// An unguessable, revocable link that lets a signed-in user request to join an event.
/// Joining through a link always results in a pending attendance.
/// </summary>
public class EventJoinLink
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public string Token { get; set; } = null!;

    /// <summary>Optional organiser-chosen name; see <see cref="DisplayLabel"/>.</summary>
    public string? Label { get; set; }
    public JoinLinkKind Kind { get; set; } = JoinLinkKind.Attendee;
    public int CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;

    public const int MaxLabelLength = 60;

    public string DisplayLabel => Label ?? DefaultLabel(Kind);

    public static string DefaultLabel(JoinLinkKind kind) =>
        kind == JoinLinkKind.Organiser ? "Organiser link" : "Attendee link";

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null
        && (ExpiresAt is null || ExpiresAt > now)
        && (MaxUses is null || UseCount < MaxUses);
}

public enum JoinLinkKind
{
    Attendee,
    Organiser,
}
