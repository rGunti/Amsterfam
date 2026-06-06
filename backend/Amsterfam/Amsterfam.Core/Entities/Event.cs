namespace Amsterfam.Core.Entities;

public class Event
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public required string Location { get; set; }
    public decimal CostPerNight { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public int CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }

    public User CreatedBy { get; set; } = null!;
    public ICollection<EventAttendance> Attendances { get; set; } = [];
    public ICollection<Accommodation> Accommodations { get; set; } = [];
    public ICollection<AvailabilityEntry> AvailabilityEntries { get; set; } = [];
    public ICollection<Activity> Activities { get; set; } = [];
    public ICollection<ItineraryEntry> ItineraryEntries { get; set; } = [];
    public ICollection<ShoppingItem> ShoppingItems { get; set; } = [];
    public ICollection<EventComfortQuestion> ComfortQuestions { get; set; } = [];
}

public enum EventStatus
{
    Draft,
    Open,
    Closed,
}
