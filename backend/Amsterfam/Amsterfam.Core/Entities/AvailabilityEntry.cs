namespace Amsterfam.Core.Entities;

public class AvailabilityEntry
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public AvailabilityStatus Status { get; set; }

    public Event Event { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum AvailabilityStatus
{
    NotComing,
    Maybe,
    Planned,
    Booked,
    DayTrip,
}
