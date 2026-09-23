namespace Amsterfam.Core.Entities;

/// <summary>
/// One recorded change to an event, shown newest-first on the event's timeline.
/// Entries are written in the same SaveChanges as the change they describe.
/// </summary>
public class EventLogEntry
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public EventLogType Type { get; set; }
    public EventLogVisibility Visibility { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Who made the change; null for automatic changes.</summary>
    public int? ActorId { get; set; }

    /// <summary>The attendee the change is about, when it's about someone other than the event.</summary>
    public int? SubjectUserId { get; set; }

    /// <summary>Type-specific details as a JSON object, e.g. the from/to status.</summary>
    public string? Data { get; set; }

    public Event Event { get; set; } = null!;
    public User? Actor { get; set; }
    public User? SubjectUser { get; set; }

    /// <summary>Who can see an entry of this type unless the caller says otherwise.</summary>
    public static EventLogVisibility DefaultVisibility(EventLogType type) =>
        type switch
        {
            EventLogType.JoinRequested
            or EventLogType.JoinRequestDeclined
            or EventLogType.JoinRequestWithdrawn
            or EventLogType.CostOverrideChanged
            or EventLogType.JoinLinkCreated
            or EventLogType.JoinLinkRevoked
            or EventLogType.JoinLinkRegenerated => EventLogVisibility.Organisers,
            _ => EventLogVisibility.Everyone,
        };

    /// <summary>The visibilities a viewer with the given standing may see.</summary>
    public static EventLogVisibility[] VisibleTo(bool isOrganiser, bool isOwner) =>
        isOwner
            ? [EventLogVisibility.Everyone, EventLogVisibility.Organisers, EventLogVisibility.Owner]
        : isOrganiser ? [EventLogVisibility.Everyone, EventLogVisibility.Organisers]
        : [EventLogVisibility.Everyone];
}

public enum EventLogType
{
    EventCreated,
    EventDetailsUpdated,
    StatusChanged,
    PollRangeChanged,
    DatePollResponded,
    JoinRequested,
    JoinRequestDeclined,
    JoinRequestWithdrawn,
    AttendeeConfirmed,
    AttendeeLeft,
    AttendeeRemoved,
    OrganiserPromoted,
    OrganiserDemoted,
    OwnershipTransferred,
    TravelDatesChanged,
    CostOverrideChanged,
    JoinLinkCreated,
    JoinLinkRevoked,
    JoinLinkRegenerated,
}

public enum EventLogVisibility
{
    Everyone,
    Organisers,

    /// <summary>The current owner only, e.g. for organiser links, which only the owner manages.</summary>
    Owner,
}
