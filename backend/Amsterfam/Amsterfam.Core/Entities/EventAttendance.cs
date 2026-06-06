namespace Amsterfam.Core.Entities;

public class EventAttendance
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int UserId { get; set; }
    public AttendanceRole Role { get; set; } = AttendanceRole.Pending;
    public DateOnly? PlannedArrival { get; set; }
    public DateOnly? PlannedDeparture { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal? CostOverride { get; set; }

    public Event Event { get; set; } = null!;
    public User User { get; set; } = null!;
    public ICollection<ComfortAnswer> ComfortAnswers { get; set; } = [];
}

public enum AttendanceRole
{
    Pending,
    Attendee,
    Organiser,
}
