using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Migrations;

// Regression coverage for issue #93: an earlier version of this migration wiped the
// Events table (and everything referencing it) instead of preserving rows. These
// tests seed data against the pre-Guid schema, run the real migration, and assert
// nothing was lost — plus that the downgrade path doesn't delete rows either.
public class ReplaceEventIdWithGuidMigrationTests : IAsyncLifetime
{
    private const string PreGuidMigration =
        "20260913193421_RenameDisplayNameToHandleAddDisplayName";

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
    public async Task Migrate_PreservesEventsAndTheirRelationships()
    {
        await using (var context = CreateContext())
        {
            await MigrateToAsync(context, PreGuidMigration);
        }

        int userId,
            eventAId,
            eventBId;
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            userId = await SeedUserAsync(conn, "discord|migtest-owner", "Migration Tester");
            eventAId = await SeedEventAsync(conn, "Migrate Test Event A", userId);
            eventBId = await SeedEventAsync(conn, "Migrate Test Event B", userId);
            await SeedAttendanceAsync(conn, eventAId, userId);
            await SeedAttendanceAsync(conn, eventBId, userId);
            await SeedShoppingItemAsync(conn, eventAId, "Beer", userId);
        }

        await using (var context = CreateContext())
        {
            await MigrateToAsync(context, null); // up to latest
        }

        await using var readContext = CreateContext();
        var events = await readContext
            .Events.Where(e => e.Name == "Migrate Test Event A" || e.Name == "Migrate Test Event B")
            .ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.NotEqual(Guid.Empty, e.Id));
        Assert.Equal(2, events.Select(e => e.Id).Distinct().Count());

        var eventA = events.Single(e => e.Name == "Migrate Test Event A");
        var eventB = events.Single(e => e.Name == "Migrate Test Event B");

        var attendances = await readContext
            .EventAttendances.Where(a => a.EventId == eventA.Id || a.EventId == eventB.Id)
            .ToListAsync();
        Assert.Equal(2, attendances.Count);
        Assert.Contains(attendances, a => a.EventId == eventA.Id);
        Assert.Contains(attendances, a => a.EventId == eventB.Id);

        var shoppingItem = await readContext.ShoppingItems.SingleAsync(s => s.Name == "Beer");
        Assert.Equal(eventA.Id, shoppingItem.EventId);
    }

    [Fact]
    public async Task Downgrade_KeepsRowsButClearsEventRelationships()
    {
        Guid userExternalMarker = Guid.NewGuid();
        int userId;

        await using (var context = CreateContext())
        {
            await MigrateToAsync(context, null); // up to latest first

            var user = new User
            {
                ExternalId = $"discord|migtest-down-{userExternalMarker}",
                Handle = "Downgrade Tester",
                Email = "downgrade-tester@example.com",
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            userId = user.Id;

            var ev = new Event
            {
                Name = "Downgrade Test Event",
                Location = "Amsterdam",
                CreatedById = user.Id,
            };
            context.Events.Add(ev);
            await context.SaveChangesAsync();

            context.EventAttendances.Add(
                new EventAttendance
                {
                    EventId = ev.Id,
                    UserId = user.Id,
                    Role = AttendanceRole.Organiser,
                }
            );
            context.ShoppingItems.Add(
                new ShoppingItem
                {
                    EventId = ev.Id,
                    Name = "Snacks",
                    AddedById = user.Id,
                }
            );
            await context.SaveChangesAsync();

            await MigrateToAsync(context, PreGuidMigration); // downgrade
        }

        // The compiled model is Guid-typed and no longer matches the reverted int
        // schema, so verification here goes through raw ADO rather than EF LINQ.
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        Assert.Equal(
            1L,
            await ScalarAsync(
                conn,
                """SELECT COUNT(*) FROM "Events" WHERE "Name" = 'Downgrade Test Event' AND "CreatedById" = $1;""",
                userId
            )
        );

        Assert.Equal(
            1L,
            await ScalarAsync(
                conn,
                """SELECT COUNT(*) FROM "EventAttendances" WHERE "UserId" = $1;""",
                userId
            )
        );
        Assert.Equal(
            0L,
            await ScalarAsync(
                conn,
                """SELECT "EventId" FROM "EventAttendances" WHERE "UserId" = $1;""",
                userId
            )
        );

        Assert.Equal(
            1L,
            await ScalarAsync(
                conn,
                """SELECT COUNT(*) FROM "ShoppingItems" WHERE "AddedById" = $1 AND "Name" = 'Snacks';""",
                userId
            )
        );
        Assert.Equal(
            0L,
            await ScalarAsync(
                conn,
                """SELECT "EventId" FROM "ShoppingItems" WHERE "AddedById" = $1 AND "Name" = 'Snacks';""",
                userId
            )
        );

        await using var typeCmd = new NpgsqlCommand(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_name = 'Events' AND column_name = 'Id';
            """,
            conn
        );
        Assert.Equal("integer", (string)(await typeCmd.ExecuteScalarAsync())!);
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection conn, string sql, int param)
    {
        await using var cmd = new NpgsqlCommand(sql, conn)
        {
            Parameters = { new() { Value = param } },
        };
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> SeedUserAsync(
        NpgsqlConnection conn,
        string externalId,
        string handle
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Users" ("ExternalId", "Handle", "Email", "CreatedAt")
            VALUES ($1, $2, $3, now())
            RETURNING "Id";
            """,
            conn
        )
        {
            Parameters =
            {
                new() { Value = externalId },
                new() { Value = handle },
                new() { Value = $"{handle.Replace(" ", "").ToLowerInvariant()}@example.com" },
            },
        };
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<int> SeedEventAsync(
        NpgsqlConnection conn,
        string name,
        int createdById
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Events" ("Name", "Location", "Status", "CreatedById", "CreatedAt")
            VALUES ($1, 'Amsterdam', 'Open', $2, now())
            RETURNING "Id";
            """,
            conn
        )
        {
            Parameters =
            {
                new() { Value = name },
                new() { Value = createdById },
            },
        };
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task SeedAttendanceAsync(NpgsqlConnection conn, int eventId, int userId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "EventAttendances" ("EventId", "UserId", "Role", "AmountPaid")
            VALUES ($1, $2, 'Organiser', 0);
            """,
            conn
        )
        {
            Parameters =
            {
                new() { Value = eventId },
                new() { Value = userId },
            },
        };
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedShoppingItemAsync(
        NpgsqlConnection conn,
        int eventId,
        string itemName,
        int addedById
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "ShoppingItems" ("EventId", "Name", "IsChecked", "AddedById", "CreatedAt")
            VALUES ($1, $2, false, $3, now());
            """,
            conn
        )
        {
            Parameters =
            {
                new() { Value = eventId },
                new() { Value = itemName },
                new() { Value = addedById },
            },
        };
        await cmd.ExecuteNonQueryAsync();
    }
}
