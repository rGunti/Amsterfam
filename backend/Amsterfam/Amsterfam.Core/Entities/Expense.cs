namespace Amsterfam.Core.Entities;

/// <summary>
/// Something one attendee paid for that is shared among some of the event's attendees,
/// who owe the payer their <see cref="ExpenseShare"/>. Amounts are in the event's currency.
/// </summary>
public class Expense
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public required string Title { get; set; }
    public decimal Amount { get; set; }
    public int PaidById { get; set; }
    public ExpenseSplitMode SplitMode { get; set; }
    public int CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User PaidBy { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public ICollection<ExpenseShare> Shares { get; set; } = [];

    public const int MaxTitleLength = 100;
}

/// <summary>
/// One participant's part of an expense. <see cref="Amount"/> is always the resolved money
/// amount, whatever the split mode; the shares of an expense add up to its amount exactly.
/// </summary>
public class ExpenseShare
{
    public int ExpenseId { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>The percentage entered for a percentage split, kept for editing.</summary>
    public decimal? Percentage { get; set; }

    public Expense Expense { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum ExpenseSplitMode
{
    Equal,
    Percentage,
    Exact,
}

/// <summary>A repayment from one attendee to another, settling (part of) what is owed.</summary>
public class ExpensePayment
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public int FromUserId { get; set; }
    public int ToUserId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public int RecordedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Event Event { get; set; } = null!;
    public User FromUser { get; set; } = null!;
    public User ToUser { get; set; } = null!;
    public User RecordedBy { get; set; } = null!;

    public const int MaxNoteLength = 200;
}
