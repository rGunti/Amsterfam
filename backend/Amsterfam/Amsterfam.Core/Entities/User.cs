namespace Amsterfam.Core.Entities;

public class User
{
    public const int MaxDisplayNameLength = 100;
    public const int MaxPronounsLength = 40;
    public const int MaxLocationLength = 100;
    public const int MaxDietaryNotesLength = 500;
    public const int MaxBioLength = 1000;
    public const int MaxAuthSourceLength = 50;
    public const int MaxEmailLength = 254;

    public int Id { get; set; }
    public required string ExternalId { get; set; }
    public required string Handle { get; set; }

    /// <summary>
    /// Slug of the Authentik source the account comes from (e.g. "discord"), or "internal" for
    /// a plain Authentik account. Handle is unique per source. Null until the first sign-in
    /// that carries the claim.
    /// </summary>
    public string? AuthSource { get; set; }
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

    /// <summary>Opt-in for news by email (ADR-018). Off by default.</summary>
    public bool EmailNotifications { get; set; }

    /// <summary>
    /// An address the member chose for notifications instead of <see cref="Email"/>, which comes
    /// from the login and may be a fallback. Only used once confirmed; until then mail keeps
    /// going to the login address. See <c>NotificationAddress</c>.
    /// </summary>
    public string? NotificationEmail { get; set; }
    public DateTimeOffset? NotificationEmailConfirmedAt { get; set; }

    /// <summary>SHA-256 of the pending confirmation token; null once confirmed.</summary>
    public string? NotificationEmailTokenHash { get; set; }
    public DateTimeOffset? NotificationEmailTokenSentAt { get; set; }

    public ICollection<EventAttendance> Attendances { get; set; } = [];
    public ICollection<PaymentMethod> PaymentMethods { get; set; } = [];
    public ICollection<DietaryOption> DietaryOptions { get; set; } = [];
}
