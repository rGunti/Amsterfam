using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Migrations;

// Issue #127: existing events start their timeline with an actor-less "created" entry.
public class EventLogMigrationTests : IAsyncLifetime
{
    private const string PreEventLogMigration = "20260920113517_EventBanner";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AmsterfamDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<AmsterfamDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .Options
        );

    [Fact]
    public async Task Migrate_BackfillsCreatedEntryForExistingEvents()
    {
        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreEventLogMigration);
        }

        Guid eventId;
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                WITH u AS (
                    INSERT INTO "Users" ("ExternalId", "Handle", "Email", "CreatedAt")
                    VALUES ('discord|migtest-log', 'Log Tester', 'log-tester@example.com', now())
                    RETURNING "Id"
                )
                INSERT INTO "Events" ("Id", "Name", "Location", "Status", "CreatedById", "CreatedAt")
                SELECT gen_random_uuid(), 'Log Event', 'Amsterdam', 'Open', "Id", '2026-01-02T03:04:05Z'
                FROM u
                RETURNING "Id";
                """,
                conn
            );
            eventId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync();
        }

        await using var readContext = CreateContext();
        var entry = await readContext.EventLogEntries.SingleAsync(l => l.EventId == eventId);
        Assert.Equal(EventLogType.EventCreated, entry.Type);
        Assert.Equal(EventLogVisibility.Everyone, entry.Visibility);
        Assert.Null(entry.ActorId);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), entry.OccurredAt);
    }
}
