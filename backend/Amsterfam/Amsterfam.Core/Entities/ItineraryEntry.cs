namespace Amsterfam.Core.Entities;

public class ItineraryEntry
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? Time { get; set; }
    public required string Title { get; set; }
    public string? Notes { get; set; }
    public int? ActivityId { get; set; }
    public int CreatedById { get; set; }

    public Event Event { get; set; } = null!;
    public Activity? Activity { get; set; }
    public User CreatedBy { get; set; } = null!;
}
