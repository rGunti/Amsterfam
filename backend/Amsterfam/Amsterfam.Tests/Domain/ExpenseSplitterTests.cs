using Amsterfam.Core.Entities;
using Amsterfam.Core.Expenses;

namespace Amsterfam.Tests.Domain;

public class ExpenseSplitterTests
{
    private static SplitParticipant[] People(params int[] ids) =>
        ids.Select(id => new SplitParticipant(id)).ToArray();

    private static Dictionary<int, decimal> Amounts(SplitResult result)
    {
        Assert.Null(result.Error);
        return result.Shares.ToDictionary(s => s.UserId, s => s.Amount);
    }

    [Fact]
    public void Equal_SplitsEvenly()
    {
        var shares = Amounts(ExpenseSplitter.Split(30m, ExpenseSplitMode.Equal, People(1, 2, 3)));

        Assert.Equal(
            new Dictionary<int, decimal>
            {
                [1] = 10m,
                [2] = 10m,
                [3] = 10m,
            },
            shares
        );
    }

    [Fact]
    public void Equal_HandsLeftoverCentsToLowestIdsAndAddsUpExactly()
    {
        var shares = Amounts(ExpenseSplitter.Split(10m, ExpenseSplitMode.Equal, People(3, 1, 2)));

        Assert.Equal(3.34m, shares[1]);
        Assert.Equal(3.33m, shares[2]);
        Assert.Equal(3.33m, shares[3]);
        Assert.Equal(10m, shares.Values.Sum());
    }

    [Fact]
    public void Percentage_ResolvesToAmountsAndKeepsPercentages()
    {
        var result = ExpenseSplitter.Split(
            100m,
            ExpenseSplitMode.Percentage,
            [new(1, 50m), new(2, 30m), new(3, 20m)]
        );

        Assert.Equal(
            new Dictionary<int, decimal>
            {
                [1] = 50m,
                [2] = 30m,
                [3] = 20m,
            },
            Amounts(result)
        );
        Assert.Equal(30m, result.Shares.Single(s => s.UserId == 2).Percentage);
    }

    [Fact]
    public void Percentage_GivesLeftoverCentsToTheBiggestRoundingLoss()
    {
        // 33.33% of 0.10 is 3.333c, 66.67% is 6.667c: the second loses more to rounding.
        var shares = Amounts(
            ExpenseSplitter.Split(
                0.10m,
                ExpenseSplitMode.Percentage,
                [new(1, 33.33m), new(2, 66.67m)]
            )
        );

        Assert.Equal(0.03m, shares[1]);
        Assert.Equal(0.07m, shares[2]);
    }

    [Fact]
    public void Percentage_BelowHundredLeavesTheRestUnassigned()
    {
        var shares = Amounts(
            ExpenseSplitter.Split(100m, ExpenseSplitMode.Percentage, [new(1, 50m), new(2, 40m)])
        );

        Assert.Equal(new Dictionary<int, decimal> { [1] = 50m, [2] = 40m }, shares);
    }

    [Fact]
    public void Percentage_FailsAboveHundred()
    {
        var result = ExpenseSplitter.Split(
            100m,
            ExpenseSplitMode.Percentage,
            [new(1, 60m), new(2, 50m)]
        );

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Exact_UsesAmountsAsGiven()
    {
        var shares = Amounts(
            ExpenseSplitter.Split(25m, ExpenseSplitMode.Exact, [new(1, 20m), new(2, 5m)])
        );

        Assert.Equal(new Dictionary<int, decimal> { [1] = 20m, [2] = 5m }, shares);
    }

    [Fact]
    public void Exact_BelowTheTotalLeavesTheRestUnassigned()
    {
        var shares = Amounts(
            ExpenseSplitter.Split(25m, ExpenseSplitMode.Exact, [new(1, 20m), new(2, 4m)])
        );

        Assert.Equal(24m, shares.Values.Sum());
    }

    [Fact]
    public void Exact_FailsAboveTheTotal()
    {
        var result = ExpenseSplitter.Split(25m, ExpenseSplitMode.Exact, [new(1, 20m), new(2, 6m)]);

        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(ExpenseSplitMode.Percentage)]
    [InlineData(ExpenseSplitMode.Exact)]
    public void Split_AllowsLeavingEverythingUnassigned(ExpenseSplitMode mode)
    {
        var result = ExpenseSplitter.Split(10m, mode, []);

        Assert.Null(result.Error);
        Assert.Empty(result.Shares);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public void Split_RejectsInvalidTotals(decimal total)
    {
        Assert.NotNull(ExpenseSplitter.Split(total, ExpenseSplitMode.Equal, People(1)).Error);
    }

    [Fact]
    public void Split_RejectsNoOrDuplicateParticipants()
    {
        Assert.NotNull(ExpenseSplitter.Split(10m, ExpenseSplitMode.Equal, []).Error);
        Assert.NotNull(ExpenseSplitter.Split(10m, ExpenseSplitMode.Equal, People(1, 1)).Error);
    }
}
