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
    bool CanEdit,
    decimal Unassigned
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

/// <summary><see cref="Unassigned"/> is the part of <see cref="Balance"/> nobody owes yet.</summary>
public record BalanceResponse(int UserId, decimal Balance, decimal Unassigned);

public record TransferResponse(int FromUserId, int ToUserId, decimal Amount);

/// <summary>Transfers settle the assigned money only; <see cref="Unassigned"/> is the total left over.</summary>
public record BalancesResponse(
    string Currency,
    IReadOnlyList<BalanceResponse> Balances,
    IReadOnlyList<TransferResponse> Transfers,
    decimal Unassigned
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
