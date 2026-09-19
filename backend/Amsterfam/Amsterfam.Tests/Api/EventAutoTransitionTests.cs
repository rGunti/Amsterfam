using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Amsterfam.Tests.Api;

public class EventAutoTransitionTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static readonly DateOnly Today = new(2031, 3, 10);

    private async Task<Guid> SeedEventAsync(
        string suffix,
        EventStatus status,
        DateOnly start,
        DateOnly end,
        bool paused = false
    )
    {
        await using var context = db.CreateDbContext();
        var user = new User
        {
            ExternalId = $"discord|auto-{suffix}",
            Handle = $"Auto {suffix}",
            Email = $"auto-{suffix}@example.com",
        };
        var ev = new Event
        {
            Name = $"Auto {suffix}",
            Location = "Amsterdam",
            Status = status,
            StartDate = start,
            EndDate = end,
            AutoTransitionsPaused = paused,
            CreatedBy = user,
        };
        context.Events.Add(ev);
        await context.SaveChangesAsync();
        return ev.Id;
    }

    private async Task<EventStatus> StatusOf(Guid id)
    {
        await using var context = db.CreateDbContext();
        return (await context.Events.SingleAsync(e => e.Id == id)).Status;
    }

    private async Task RunAsync(DateOnly today)
    {
        await using var context = db.CreateDbContext();
        await new EventAutoTransitioner(
            context,
            NullLogger<EventAutoTransitioner>.Instance
        ).RunAsync(today);
    }

    [Fact]
    public async Task Run_MovesEventsAlongByDate()
    {
        var starting = await SeedEventAsync("start", EventStatus.Open, Today, Today.AddDays(5));
        var future = await SeedEventAsync(
            "future",
            EventStatus.Open,
            Today.AddDays(1),
            Today.AddDays(5)
        );
        var ending = await SeedEventAsync(
            "end",
            EventStatus.InProgress,
            Today.AddDays(-5),
            Today.AddDays(-1)
        );
        var lastDay = await SeedEventAsync(
            "lastday",
            EventStatus.InProgress,
            Today.AddDays(-5),
            Today
        );
        var missed = await SeedEventAsync(
            "missed",
            EventStatus.Open,
            Today.AddDays(-5),
            Today.AddDays(-1)
        );
        var paused = await SeedEventAsync(
            "paused",
            EventStatus.Open,
            Today.AddDays(-5),
            Today.AddDays(-1),
            paused: true
        );

        await RunAsync(Today);

        Assert.Equal(EventStatus.InProgress, await StatusOf(starting));
        Assert.Equal(EventStatus.Open, await StatusOf(future));
        Assert.Equal(EventStatus.Closed, await StatusOf(ending));
        Assert.Equal(EventStatus.InProgress, await StatusOf(lastDay));
        Assert.Equal(EventStatus.Closed, await StatusOf(missed));
        Assert.Equal(EventStatus.Open, await StatusOf(paused));

        // Idempotent.
        await RunAsync(Today);
        Assert.Equal(EventStatus.InProgress, await StatusOf(starting));
    }

    [Fact]
    public void Options_DefaultScheduleIsValid_AndDailyShortlyAfterMidnight()
    {
        var options = new AutoTransitionOptions();
        Assert.True(options.HasValidSchedule());

        var next = options
            .ParseSchedule()
            .GetNextOccurrence(new DateTime(2031, 3, 10, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2031, 3, 11, 0, 5, 0, DateTimeKind.Utc), next);
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("61 * * * *")]
    [InlineData("")]
    public void Options_RejectInvalidSchedules(string schedule)
    {
        Assert.False(new AutoTransitionOptions { Schedule = schedule }.HasValidSchedule());
    }
}
