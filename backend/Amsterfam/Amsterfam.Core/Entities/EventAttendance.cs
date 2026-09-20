namespace Amsterfam.Core.Entities;

public class EventAttendance
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public int UserId { get; set; }
    public AttendanceRole Role { get; set; } = AttendanceRole.Pending;
    public DateOnly? PlannedArrival { get; set; }
    public DateOnly? PlannedDeparture { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal? CostOverride { get; set; }

    /// <summary>Joined via an organiser link; only the owner may confirm, granting Organiser.</summary>
    public bool RequestedOrganiser { get; set; }

    /// <summary>The join link this attendance was requested through, if any.</summary>
    public int? JoinLinkId { get; set; }

    public Event Event { get; set; } = null!;
    public User User { get; set; } = null!;
    public EventJoinLink? JoinLink { get; set; }
    public ICollection<ComfortAnswer> ComfortAnswers { get; set; } = [];
}

public enum AttendanceRole
{
    Pending,
    Attendee,
    Organiser,
}
