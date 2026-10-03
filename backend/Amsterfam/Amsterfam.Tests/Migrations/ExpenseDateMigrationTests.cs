using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Migrations;

// Existing expenses are dated by when they were recorded.
public class ExpenseDateMigrationTests : IAsyncLifetime
{
    private const string PreExpenseDateMigration = "20261003102118_AddExpenses";

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
    public async Task Migrate_DatesExistingExpensesByCreation()
    {
        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(PreExpenseDateMigration);
        }

        int expenseId;
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                WITH u AS (
                    INSERT INTO "Users" ("ExternalId", "Handle", "Email", "CreatedAt")
                    VALUES ('discord|migtest-exp', 'Expense Tester', 'exp-tester@example.com', now())
                    RETURNING "Id"
                ), e AS (
                    INSERT INTO "Events" ("Id", "Name", "Location", "Status", "CreatedById", "CreatedAt")
                    SELECT gen_random_uuid(), 'Expense Event', 'Amsterdam', 'Open', "Id", now()
                    FROM u
                    RETURNING "Id", "CreatedById"
                )
                INSERT INTO "Expenses" ("EventId", "Title", "Amount", "PaidById", "SplitMode", "CreatedById", "CreatedAt")
                SELECT "Id", 'Groceries', 10, "CreatedById", 'Equal', "CreatedById", '2026-07-02T23:30:00Z'
                FROM e
                RETURNING "Id";
                """,
                conn
            );
            expenseId = (int)(await cmd.ExecuteScalarAsync())!;
        }

        await using (var context = CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync();
            var expense = await context.Expenses.SingleAsync(x => x.Id == expenseId);
            Assert.Equal(new DateOnly(2026, 7, 2), expense.Date);
        }
    }
}
