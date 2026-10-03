namespace Amsterfam.Api.Dtos;

/// <summary>Someone who can appear in expenses; former members stay listed for their history.</summary>
public record ExpenseMember(int UserId, string DisplayName, string? AvatarUrl, bool IsConfirmed);

public record ExpenseShareResponse(int UserId, decimal Amount, decimal? Percentage);

public record ExpenseResponse(
    int Id,
    string Title,
    decimal Amount,
    int PaidById,
    string SplitMode,
    IReadOnlyList<ExpenseShareResponse> Shares,
    int CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit
);

public record ExpensePaymentResponse(
    int Id,
    int FromUserId,
    int ToUserId,
    decimal Amount,
    string? Note,
    int RecordedById,
    DateTimeOffset CreatedAt,
    bool CanDelete
);

public record ExpenseListResponse(
    string Currency,
    bool CanEdit,
    IReadOnlyList<ExpenseMember> Members,
    IReadOnlyList<ExpenseResponse> Expenses,
    IReadOnlyList<ExpensePaymentResponse> Payments
);

public record BalanceResponse(int UserId, decimal Balance);

public record TransferResponse(int FromUserId, int ToUserId, decimal Amount);

public record BalancesResponse(
    string Currency,
    IReadOnlyList<BalanceResponse> Balances,
    IReadOnlyList<TransferResponse> Transfers
);

/// <summary><see cref="Value"/> is the percentage or exact amount, depending on the split mode.</summary>
public record ExpenseShareRequest(int UserId, decimal? Value);

public record UpsertExpenseRequest(
    string Title,
    decimal Amount,
    int PaidById,
    string SplitMode,
    IReadOnlyList<ExpenseShareRequest> Shares
);

public record RecordPaymentRequest(int FromUserId, int ToUserId, decimal Amount, string? Note);
