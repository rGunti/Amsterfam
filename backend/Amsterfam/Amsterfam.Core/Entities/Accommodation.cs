namespace Amsterfam.Core.Entities;

public class Accommodation
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }

    public Event Event { get; set; } = null!;
    public ICollection<Room> Rooms { get; set; } = [];
}

public class Room
{
    public int Id { get; set; }
    public int AccommodationId { get; set; }
    public required string Name { get; set; }

    public Accommodation Accommodation { get; set; } = null!;
    public ICollection<Bed> Beds { get; set; } = [];
}

public class Bed
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public required string Name { get; set; }
    public BedType Type { get; set; }

    public Room Room { get; set; } = null!;
    public ICollection<BedAssignment> Assignments { get; set; } = [];
}

public enum BedType
{
    Double,
    Single,
    BunkTop,
    BunkBottom,
    Sofa,
}

public class BedAssignment
{
    public int Id { get; set; }
    public int BedId { get; set; }
    public int UserId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    public Bed Bed { get; set; } = null!;
    public User User { get; set; } = null!;
}
