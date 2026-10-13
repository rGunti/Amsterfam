using System.Collections.Concurrent;

namespace Amsterfam.Api.Services;

/// <summary>
/// Lets each event send one Discord test message per <see cref="Cooldown"/>, so repeated clicks
/// don't run into Discord's rate limit. Kept in memory: a restart just allows one more test.
/// </summary>
public class DiscordTestThrottle(TimeProvider time)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSent = new();

    /// <summary>
    /// Claims the event's next test, or returns how long to wait. Concurrent callers can't both
    /// get through.
    /// </summary>
    public bool TryAcquire(Guid eventId, out TimeSpan retryAfter)
    {
        var now = time.GetUtcNow();
        while (true)
        {
            if (_lastSent.TryGetValue(eventId, out var last))
            {
                var wait = last + Cooldown - now;
                if (wait > TimeSpan.Zero)
                {
                    retryAfter = wait;
                    return false;
                }
                if (_lastSent.TryUpdate(eventId, now, last))
                    break;
            }
            else if (_lastSent.TryAdd(eventId, now))
            {
                break;
            }
        }
        retryAfter = TimeSpan.Zero;
        return true;
    }
}
