using System.Collections.Concurrent;
using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// Lets each event send one test message per channel per <see cref="Cooldown"/>, so repeated
/// clicks don't run into Discord's or Telegram's rate limits. Kept in memory: a restart just allows one more test.
/// </summary>
public class ChannelTestThrottle(TimeProvider time)
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<(Guid, NotificationChannel), DateTimeOffset> _lastSent =
        new();

    /// <summary>
    /// Claims the event's next test, or returns how long to wait. Concurrent callers can't both
    /// get through.
    /// </summary>
    public bool TryAcquire(Guid eventId, NotificationChannel channel, out TimeSpan retryAfter)
    {
        var key = (eventId, channel);
        var now = time.GetUtcNow();
        while (true)
        {
            if (_lastSent.TryGetValue(key, out var last))
            {
                var wait = last + Cooldown - now;
                if (wait > TimeSpan.Zero)
                {
                    retryAfter = wait;
                    return false;
                }
                if (_lastSent.TryUpdate(key, now, last))
                    break;
            }
            else if (_lastSent.TryAdd(key, now))
            {
                break;
            }
        }
        retryAfter = TimeSpan.Zero;
        return true;
    }
}
