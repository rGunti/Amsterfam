using Amsterfam.Core.Entities;

namespace Amsterfam.Core.Expenses;

public record Transfer(int FromUserId, int ToUserId, decimal Amount);

/// <summary>
/// Works out who owes whom from an event's expenses and repayments. Balances are computed on
/// read rather than stored, so editing or deleting an expense can never leave them stale.
/// </summary>
public static class BalanceCalculator
{
    /// <summary>
    /// Net balance per user: positive means the user is owed money, negative that they owe.
    /// Users who never took part in anything are left out. Expects shares to be loaded.
    /// </summary>
    public static Dictionary<int, decimal> NetBalances(
        IEnumerable<Expense> expenses,
        IEnumerable<ExpensePayment> payments
    )
    {
        var balances = new Dictionary<int, decimal>();
        void Add(int userId, decimal amount) =>
            balances[userId] = balances.GetValueOrDefault(userId) + amount;

        foreach (var expense in expenses)
        {
            Add(expense.PaidById, expense.Amount);
            foreach (var share in expense.Shares)
                Add(share.UserId, -share.Amount);
        }

        foreach (var payment in payments)
        {
            Add(payment.FromUserId, payment.Amount);
            Add(payment.ToUserId, -payment.Amount);
        }

        return balances;
    }

    public static bool AllSettled(IReadOnlyDictionary<int, decimal> balances) =>
        balances.Values.All(b => b == 0);

    /// <summary>
    /// A short list of repayments that settles every balance: the biggest debtor repeatedly
    /// pays the biggest creditor. Not always the fewest possible, but close and predictable.
    /// </summary>
    public static List<Transfer> SuggestTransfers(IReadOnlyDictionary<int, decimal> balances)
    {
        var debtors = balances
            .Where(b => b.Value < 0)
            .Select(b => (UserId: b.Key, Left: -b.Value))
            .ToList();
        var creditors = balances
            .Where(b => b.Value > 0)
            .Select(b => (UserId: b.Key, Left: b.Value))
            .ToList();
        var transfers = new List<Transfer>();

        while (debtors.Count > 0 && creditors.Count > 0)
        {
            var d = debtors.OrderByDescending(x => x.Left).ThenBy(x => x.UserId).First();
            var c = creditors.OrderByDescending(x => x.Left).ThenBy(x => x.UserId).First();
            var amount = Math.Min(d.Left, c.Left);

            transfers.Add(new Transfer(d.UserId, c.UserId, amount));

            debtors.Remove(d);
            creditors.Remove(c);
            if (d.Left > amount)
                debtors.Add((d.UserId, d.Left - amount));
            if (c.Left > amount)
                creditors.Add((c.UserId, c.Left - amount));
        }

        return transfers;
    }
}
