using Amsterfam.Core.Expenses;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Services;

/// <summary>
/// Tells whether an event still has unsettled balances, which blocks archiving it.
/// </summary>
public interface IEventBalanceCheck
{
    Task<bool> HasOpenBalancesAsync(Guid eventId, CancellationToken ct = default);
}

/// <summary>Open as long as anyone still owes or is owed money from the event's expenses.</summary>
public class ExpenseBalanceCheck(AmsterfamDbContext db) : IEventBalanceCheck
{
    public async Task<bool> HasOpenBalancesAsync(Guid eventId, CancellationToken ct = default)
    {
        var expenses = await db
            .Expenses.Where(x => x.EventId == eventId)
            .Include(x => x.Shares)
            .ToListAsync(ct);
        var payments = await db.ExpensePayments.Where(p => p.EventId == eventId).ToListAsync(ct);

        return !BalanceCalculator.AllSettled(BalanceCalculator.NetBalances(expenses, payments));
    }
}
