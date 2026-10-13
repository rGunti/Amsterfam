using Amsterfam.Api.Services;

namespace Amsterfam.Tests.Services;

public class DiscordTestThrottleTests
{
    private class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    [Fact]
    public void AllowsOneTestPerCooldown()
    {
        var time = new Clock(DateTimeOffset.UtcNow);
        var throttle = new DiscordTestThrottle(time);
        var ev = Guid.NewGuid();

        Assert.True(throttle.TryAcquire(ev, out _));
        Assert.False(throttle.TryAcquire(ev, out var wait));
        Assert.Equal(DiscordTestThrottle.Cooldown, wait);
        Assert.True(throttle.TryAcquire(Guid.NewGuid(), out _));

        time.Advance(DiscordTestThrottle.Cooldown);
        Assert.True(throttle.TryAcquire(ev, out _));
    }
}
