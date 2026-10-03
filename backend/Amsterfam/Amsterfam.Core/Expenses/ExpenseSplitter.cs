using Amsterfam.Core.Entities;

namespace Amsterfam.Core.Expenses;

/// <summary>A participant of a split; <see cref="Value"/> is the percentage or exact amount.</summary>
public record SplitParticipant(int UserId, decimal? Value = null);

public record ResolvedShare(int UserId, decimal Amount, decimal? Percentage);

public record SplitResult(IReadOnlyList<ResolvedShare> Shares, string? Error)
{
    public static SplitResult Fail(string error) => new([], error);
}

/// <summary>
/// Resolves an expense's split into exact money amounts, in whole cents. Equal splits always
/// cover the total. Percentage and exact splits may cover less: the rest stays unassigned
/// (it can be assigned later by editing the expense), but they can never cover more.
/// Cents left over after rounding down go one each to the shares that lost the most to
/// rounding, ties broken by user id so the result is stable.
/// </summary>
public static class ExpenseSplitter
{
    /// <summary>The largest amount numeric(10,2) holds.</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    public static SplitResult Split(
        decimal total,
        ExpenseSplitMode mode,
        IReadOnlyList<SplitParticipant> participants
    )
    {
        if (total <= 0 || total > MaxAmount)
            return SplitResult.Fail("The amount must be greater than zero.");
        if (!IsWholeCents(total))
            return SplitResult.Fail("The amount can have at most two decimal places.");
        // Percentage and exact splits may leave everything unassigned for now.
        if (participants.Count == 0 && mode == ExpenseSplitMode.Equal)
            return SplitResult.Fail("Choose at least one participant.");
        if (participants.Select(p => p.UserId).Distinct().Count() != participants.Count)
            return SplitResult.Fail("Each participant can only be listed once.");

        return mode switch
        {
            ExpenseSplitMode.Equal => SplitEqually(total, participants),
            ExpenseSplitMode.Percentage => SplitByPercentage(total, participants),
            ExpenseSplitMode.Exact => SplitExactly(total, participants),
            _ => SplitResult.Fail("Unknown split mode."),
        };
    }

    private static SplitResult SplitEqually(decimal total, IReadOnlyList<SplitParticipant> ps) =>
        new(
            Distribute(
                total * 100,
                ps.Select(p => (p.UserId, Weight: 1m, (decimal?)null)).ToList()
            ),
            null
        );

    private static SplitResult SplitByPercentage(decimal total, IReadOnlyList<SplitParticipant> ps)
    {
        if (ps.Any(p => p.Value is null or <= 0))
            return SplitResult.Fail("Every participant needs a percentage greater than zero.");
        if (ps.Any(p => !IsWholeCents(p.Value!.Value)))
            return SplitResult.Fail("Percentages can have at most two decimal places.");
        var percent = ps.Sum(p => p.Value!.Value);
        if (percent > 100m)
            return SplitResult.Fail("The percentages can't add up to more than 100%.");
        if (ps.Count == 0)
            return new([], null);

        // Below 100% only that part of the total is shared out; the rest stays unassigned.
        var assignedCents = Math.Floor(total * 100 * percent / 100m);
        return new(
            Distribute(assignedCents, ps.Select(p => (p.UserId, p.Value!.Value, p.Value)).ToList()),
            null
        );
    }

    private static SplitResult SplitExactly(decimal total, IReadOnlyList<SplitParticipant> ps)
    {
        if (ps.Any(p => p.Value is null or <= 0))
            return SplitResult.Fail("Every participant needs an amount greater than zero.");
        if (ps.Any(p => !IsWholeCents(p.Value!.Value)))
            return SplitResult.Fail("Amounts can have at most two decimal places.");
        if (ps.Sum(p => p.Value!.Value) > total)
            return SplitResult.Fail("The amounts can't add up to more than the total.");

        return new(
            ps.Select(p => new ResolvedShare(p.UserId, p.Value!.Value, null)).ToList(),
            null
        );
    }

    /// <summary>Splits <paramref name="cents"/> in proportion to the weights, in whole cents.</summary>
    private static List<ResolvedShare> Distribute(
        decimal cents,
        List<(int UserId, decimal Weight, decimal? Percentage)> parts
    )
    {
        var weightSum = parts.Sum(p => p.Weight);

        var raw = parts
            .Select(p =>
            {
                var exact = cents * p.Weight / weightSum;
                var floor = Math.Floor(exact);
                return (p.UserId, p.Percentage, Cents: floor, Lost: exact - floor);
            })
            .ToList();

        var leftover = (int)(cents - raw.Sum(r => r.Cents));
        var bonus = raw.OrderByDescending(r => r.Lost)
            .ThenBy(r => r.UserId)
            .Take(leftover)
            .Select(r => r.UserId)
            .ToHashSet();

        return raw.Select(r => new ResolvedShare(
                r.UserId,
                (r.Cents + (bonus.Contains(r.UserId) ? 1 : 0)) / 100m,
                r.Percentage
            ))
            .ToList();
    }

    public static bool IsWholeCents(decimal value) => value * 100 == Math.Truncate(value * 100);
}
