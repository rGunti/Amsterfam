namespace Amsterfam.Core.Entities;

public class User
{
    public int Id { get; set; }
    public required string ExternalId { get; set; }
    public required string DisplayName { get; set; }
    public required string Email { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<EventAttendance> Attendances { get; set; } = [];
}
