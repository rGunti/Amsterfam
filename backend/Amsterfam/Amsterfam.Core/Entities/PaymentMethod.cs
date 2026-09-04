namespace Amsterfam.Core.Entities;

public class PaymentMethod
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string Title { get; set; }
    public string? Icon { get; set; }
    public string? Link { get; set; }
    public string? Description { get; set; }

    public User User { get; set; } = null!;
}
