using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Migrations;

// Issue #107: existing events restart as Draft under the new lifecycle.
public class EventStateMachineMigrationTests : IAsyncLifetime
{
    private const string PreStateMachineMigration = "20260916105828_ReplaceEventIdWithGuid";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AmsterfamDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<AmsterfamDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .Options
        );

    private static Task MigrateToAsync(AmsterfamDbContext context, string? targetMigration) =>
        context.GetService<IMigrator>().MigrateAsync(targetMigration);

    [Fact]
    public async Task Migrate_MovesAllExistingEventsToDraft()
    {
        await using (var context = CreateContext())
        {
            await MigrateToAsync(context, PreStateMachineMigration);
        }

        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            var userId = await SeedUserAsync(conn);
            await SeedEventAsync(conn, "SM Draft", "Draft", userId);
            await SeedEventAsync(conn, "SM Open", "Open", userId);
            await SeedEventAsync(conn, "SM Closed", "Closed", userId);
        }

        await using (var context = CreateContext())
        {
            await MigrateToAsync(context, null);
        }

        await using var readContext = CreateContext();
        var events = await readContext.Events.Where(e => e.Name.StartsWith("SM ")).ToListAsync();

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(EventStatus.Draft, e.Status));
        Assert.All(events, e => Assert.False(e.AutoTransitionsPaused));
    }

    private static async Task<int> SeedUserAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Users" ("ExternalId", "Handle", "Email", "CreatedAt")
            VALUES ('discord|migtest-sm', 'SM Tester', 'sm-tester@example.com', now())
            RETURNING "Id";
            """,
            conn
        );
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task SeedEventAsync(
        NpgsqlConnection conn,
        string name,
        string status,
        int createdById
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Events" ("Id", "Name", "Location", "Status", "CreatedById", "CreatedAt")
            VALUES (gen_random_uuid(), $1, 'Amsterdam', $2, $3, now());
            """,
            conn
        )
        {
            Parameters =
            {
                new() { Value = name },
                new() { Value = status },
                new() { Value = createdById },
            },
        };
        await cmd.ExecuteNonQueryAsync();
    }
}
