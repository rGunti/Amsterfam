namespace Amsterfam.Core.Entities;

public class User
{
    public const int MaxPronounsLength = 40;
    public const int MaxLocationLength = 100;
    public const int MaxDietaryNotesLength = 500;
    public const int MaxBioLength = 1000;

    public int Id { get; set; }
    public required string ExternalId { get; set; }
    public required string Handle { get; set; }
    public string? DisplayName { get; set; }
    public required string Email { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public string? Pronouns { get; set; }
    public string? Location { get; set; }
    public string? Bio { get; set; }

    /// <summary>Anything about diet or allergies the seeded <see cref="DietaryOption"/>s don't cover.</summary>
    public string? DietaryNotes { get; set; }

    /// <summary>Day and month are set together; the year is optional so nobody has to share their age.</summary>
    public short? BirthdayDay { get; set; }
    public short? BirthdayMonth { get; set; }
    public short? BirthYear { get; set; }

    public ICollection<EventAttendance> Attendances { get; set; } = [];
    public ICollection<PaymentMethod> PaymentMethods { get; set; } = [];
    public ICollection<DietaryOption> DietaryOptions { get; set; } = [];
}
