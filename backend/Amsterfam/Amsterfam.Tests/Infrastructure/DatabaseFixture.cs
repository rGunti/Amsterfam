using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Infrastructure;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public AmsterfamDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AmsterfamDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new AmsterfamDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
