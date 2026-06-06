namespace Amsterfam.Core.Entities;

public class Activity
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int SuggestedById { get; set; }
    public required string Name { get; set; }
    public string? Type { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User SuggestedBy { get; set; } = null!;
    public ICollection<ActivityVote> Votes { get; set; } = [];
}

public class ActivityVote
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public int UserId { get; set; }
    public int Score { get; set; }

    public Activity Activity { get; set; } = null!;
    public User User { get; set; } = null!;
}
