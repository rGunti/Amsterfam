namespace Amsterfam.Core.Entities;

public class DatePollEntry
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int UserId { get; set; }
    public DateOnly WeekStart { get; set; }
    public DatePollStatus Status { get; set; }

    public Event Event { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum DatePollStatus
{
    Available,
    Unavailable,
    Partial,
}
