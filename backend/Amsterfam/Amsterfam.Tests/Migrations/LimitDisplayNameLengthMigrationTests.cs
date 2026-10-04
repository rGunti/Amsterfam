using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Migrations;

// Issue #126: display names were unlimited before; longer ones are cut to 100 characters.
public class LimitDisplayNameLengthMigrationTests : IAsyncLifetime
{
    private const string PreLimitMigration = "20261004154309_AddUserProfileFields";

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
    public async Task Migrate_TruncatesOverlongDisplayNames_AndKeepsShortOnes()
    {
        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreLimitMigration);
        }

        var longName = new string('a', 150);
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO "Users" ("ExternalId", "Handle", "Email", "DisplayName", "CreatedAt")
                VALUES
                    ('discord|migtest-long', 'Long', 'long@example.com', @long, now()),
                    ('discord|migtest-short', 'Short', 'short@example.com', 'Sam', now()),
                    ('discord|migtest-none', 'None', 'none@example.com', NULL, now());
                """,
                conn
            );
            cmd.Parameters.AddWithValue("long", longName);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync();
        }

        await using (var context = CreateContext())
        {
            var names = await context
                .Users.Where(u => u.ExternalId.StartsWith("discord|migtest-"))
                .ToDictionaryAsync(u => u.ExternalId, u => u.DisplayName);

            Assert.Equal(new string('a', 100), names["discord|migtest-long"]);
            Assert.Equal("Sam", names["discord|migtest-short"]);
            Assert.Null(names["discord|migtest-none"]);
        }
    }
}
