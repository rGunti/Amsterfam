namespace Amsterfam.Core.Entities;

/// <summary>
/// A pre-made dietary need or allergy users can tick on their profile. The set is seeded by
/// the database migrations; add new ones to <see cref="Seed"/> and create a migration.
/// </summary>
public class DietaryOption
{
    public int Id { get; set; }

    /// <summary>Stable slug, safe to reference from code and tests.</summary>
    public required string Key { get; set; }
    public required string Label { get; set; }
    public int SortOrder { get; set; }

    public ICollection<User> Users { get; set; } = [];

    /// <summary>Ids are fixed forever once shipped; never reuse or renumber them.</summary>
    public static readonly IReadOnlyList<DietaryOption> Seed =
    [
        new()
        {
            Id = 1,
            Key = "vegetarian",
            Label = "Vegetarian",
            SortOrder = 10,
        },
        new()
        {
            Id = 2,
            Key = "vegan",
            Label = "Vegan",
            SortOrder = 20,
        },
        new()
        {
            Id = 3,
            Key = "pescatarian",
            Label = "Pescatarian",
            SortOrder = 30,
        },
        new()
        {
            Id = 4,
            Key = "halal",
            Label = "Halal",
            SortOrder = 40,
        },
        new()
        {
            Id = 5,
            Key = "kosher",
            Label = "Kosher",
            SortOrder = 50,
        },
        new()
        {
            Id = 6,
            Key = "no-pork",
            Label = "No pork",
            SortOrder = 60,
        },
        new()
        {
            Id = 7,
            Key = "no-alcohol",
            Label = "No alcohol",
            SortOrder = 70,
        },
        new()
        {
            Id = 8,
            Key = "gluten-free",
            Label = "Gluten-free",
            SortOrder = 80,
        },
        new()
        {
            Id = 9,
            Key = "lactose-free",
            Label = "Lactose-free",
            SortOrder = 90,
        },
        new()
        {
            Id = 10,
            Key = "nut-allergy",
            Label = "Nut allergy",
            SortOrder = 100,
        },
        new()
        {
            Id = 11,
            Key = "peanut-allergy",
            Label = "Peanut allergy",
            SortOrder = 110,
        },
        new()
        {
            Id = 12,
            Key = "shellfish-allergy",
            Label = "Shellfish allergy",
            SortOrder = 120,
        },
    ];
}
