namespace Amsterfam.Api.Services;

/// <summary>
/// Tells whether an event still has unsettled balances, which blocks archiving it.
/// </summary>
public interface IEventBalanceCheck
{
    Task<bool> HasOpenBalancesAsync(Guid eventId, CancellationToken ct = default);
}

/// <summary>
/// Placeholder until cost tracking (#27) exists: nothing is ever owed yet.
/// </summary>
public class NoOpEventBalanceCheck : IEventBalanceCheck
{
    public Task<bool> HasOpenBalancesAsync(Guid eventId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
