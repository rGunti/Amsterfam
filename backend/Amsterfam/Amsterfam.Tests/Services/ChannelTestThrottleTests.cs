using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;

namespace Amsterfam.Tests.Services;

public class ChannelTestThrottleTests
{
    private class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private const NotificationChannel Discord = NotificationChannel.Discord;

    [Fact]
    public void AllowsOneTestPerCooldown()
    {
        var time = new Clock(DateTimeOffset.UtcNow);
        var throttle = new ChannelTestThrottle(time);
        var ev = Guid.NewGuid();

        Assert.True(throttle.TryAcquire(ev, Discord, out _));
        Assert.False(throttle.TryAcquire(ev, Discord, out var wait));
        Assert.Equal(ChannelTestThrottle.Cooldown, wait);
        Assert.True(throttle.TryAcquire(Guid.NewGuid(), Discord, out _));

        time.Advance(ChannelTestThrottle.Cooldown);
        Assert.True(throttle.TryAcquire(ev, Discord, out _));
    }

    [Fact]
    public void EachChannelHasItsOwnCooldown()
    {
        var throttle = new ChannelTestThrottle(new Clock(DateTimeOffset.UtcNow));
        var ev = Guid.NewGuid();

        Assert.True(throttle.TryAcquire(ev, Discord, out _));
        Assert.True(throttle.TryAcquire(ev, NotificationChannel.Telegram, out _));
        Assert.False(throttle.TryAcquire(ev, NotificationChannel.Telegram, out _));
    }
}
