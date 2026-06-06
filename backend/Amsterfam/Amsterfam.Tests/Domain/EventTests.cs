using Amsterfam.Core.Entities;
using Amsterfam.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Tests.Domain;

public class EventTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static User MakeUser(string suffix) =>
        new()
        {
            ExternalId = $"discord|event-test-{suffix}",
            DisplayName = $"User {suffix}",
            Email = $"{suffix}@example.com",
        };

    [Fact]
    public async Task CanCreateEventWithOrganiser()
    {
        await using var context = db.CreateDbContext();

        var organiser = MakeUser("organiser-1");
        context.Users.Add(organiser);
        await context.SaveChangesAsync();

        var ev = new Event
        {
            Name = "Amsterfam 2027",
            StartDate = new DateOnly(2027, 7, 1),
            EndDate = new DateOnly(2027, 7, 8),
            Location = "Amsterdam",
            CostPerNight = 35.00m,
            CreatedById = organiser.Id,
        };

        context.Events.Add(ev);
        await context.SaveChangesAsync();

        await using var readContext = db.CreateDbContext();
        var saved = await readContext
            .Events.Include(e => e.CreatedBy)
            .SingleAsync(e => e.Name == "Amsterfam 2027");

        Assert.Equal(EventStatus.Draft, saved.Status);
        Assert.Equal(35.00m, saved.CostPerNight);
        Assert.Equal(organiser.Id, saved.CreatedBy.Id);
    }

    [Fact]
    public async Task AttendanceUserEventCombinationMustBeUnique()
    {
        await using var context = db.CreateDbContext();

        var user = MakeUser("attendance-dupe");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var ev = new Event
        {
            Name = "Amsterfam 2028",
            StartDate = new DateOnly(2028, 7, 1),
            EndDate = new DateOnly(2028, 7, 8),
            Location = "Amsterdam",
            CostPerNight = 35.00m,
            CreatedById = user.Id,
        };
        context.Events.Add(ev);
        await context.SaveChangesAsync();

        context.EventAttendances.AddRange(
            new EventAttendance { EventId = ev.Id, UserId = user.Id },
            new EventAttendance { EventId = ev.Id, UserId = user.Id }
        );

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
