namespace Amsterfam.Core.Entities;

public class ShoppingItem
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public required string Name { get; set; }
    public bool IsChecked { get; set; }
    public int AddedById { get; set; }
    public DateTime CreatedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User AddedBy { get; set; } = null!;
}
