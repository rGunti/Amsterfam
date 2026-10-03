using Amsterfam.Core.Entities;
using Amsterfam.Core.Expenses;

namespace Amsterfam.Tests.Domain;

public class BalanceCalculatorTests
{
    private static Expense Paid(
        int payer,
        decimal amount,
        params (int UserId, decimal Amount)[] shares
    ) =>
        new()
        {
            Title = "Test",
            Amount = amount,
            PaidById = payer,
            Shares = shares
                .Select(s => new ExpenseShare { UserId = s.UserId, Amount = s.Amount })
                .ToList(),
        };

    private static ExpensePayment Repaid(int from, int to, decimal amount) =>
        new()
        {
            FromUserId = from,
            ToUserId = to,
            Amount = amount,
        };

    [Fact]
    public void NetBalances_CreditsPayerAndDebitsShares()
    {
        var balances = BalanceCalculator.NetBalances(
            [Paid(1, 30m, (1, 10m), (2, 10m), (3, 10m))],
            []
        );

        Assert.Equal(20m, balances[1]);
        Assert.Equal(-10m, balances[2]);
        Assert.Equal(-10m, balances[3]);
    }

    [Fact]
    public void NetBalances_RepaymentsSettleDebts()
    {
        var balances = BalanceCalculator.NetBalances(
            [Paid(1, 20m, (1, 10m), (2, 10m))],
            [Repaid(2, 1, 10m)]
        );

        Assert.True(BalanceCalculator.AllSettled(balances));
    }

    [Fact]
    public void SuggestTransfers_SettlesEveryBalance()
    {
        var balances = BalanceCalculator.NetBalances(
            [
                Paid(1, 90m, (1, 30m), (2, 30m), (3, 30m)),
                Paid(2, 40m, (2, 10m), (3, 10m), (4, 20m)),
                Paid(4, 15.01m, (1, 5m), (3, 5m), (4, 5.01m)),
            ],
            []
        );

        var transfers = BalanceCalculator.SuggestTransfers(balances);

        var settled = BalanceCalculator.NetBalances(
            [],
            transfers.Select(t => Repaid(t.FromUserId, t.ToUserId, t.Amount))
        );
        foreach (var (userId, balance) in balances)
            Assert.Equal(0m, balance + settled.GetValueOrDefault(userId));
        Assert.All(transfers, t => Assert.True(t.Amount > 0));
    }

    [Fact]
    public void UnassignedMoney_IsOwedToPayerButLeftOutOfTransfers()
    {
        // Alice paid 100 that isn't assigned yet; Carol owes Bob 10 for something else.
        Expense[] expenses = [Paid(1, 100m), Paid(2, 20m, (2, 10m), (3, 10m))];

        var balances = BalanceCalculator.NetBalances(expenses, []);
        var assigned = BalanceCalculator.NetBalances(expenses, [], includeUnassigned: false);

        Assert.Equal(100m, balances[1]);
        Assert.Equal(100m, BalanceCalculator.UnassignedByPayer(expenses)[1]);
        // Carol must pay Bob, not Alice, even though Alice is "owed" more.
        var transfer = Assert.Single(BalanceCalculator.SuggestTransfers(assigned));
        Assert.Equal(new Transfer(3, 2, 10m), transfer);
    }

    [Fact]
    public void SuggestTransfers_IsEmptyWhenSettled()
    {
        Assert.Empty(BalanceCalculator.SuggestTransfers(new Dictionary<int, decimal> { [1] = 0m }));
    }
}
