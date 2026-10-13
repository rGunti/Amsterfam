using Amsterfam.Api.Auth;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Infrastructure;

public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PublicUrl = "https://amsterfam.test";

    /// <summary>Different from <see cref="PublicUrl"/>, so tests catch links to the wrong one.</summary>
    public const string ApiUrl = "https://api.amsterfam.test";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Tests drive auto-transitions directly via EventAutoTransitioner.
        builder.UseSetting("AutoTransitions:Enabled", "false");
        // ...and scheduled news via NewsPublisher.PublishDueAsync.
        builder.UseSetting("News:WorkerEnabled", "false");
        builder.UseSetting("App:PublicUrl", PublicUrl);
        builder.UseSetting("App:ApiUrl", ApiUrl);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AmsterfamDbContext>)
            );
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AmsterfamDbContext>(options =>
                options.UseNpgsql(_container.GetConnectionString())
            );

            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { }
                );
        });
    }

    public HttpClient CreateClientWithUser(string externalId, string? authSource = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, externalId);
        if (authSource is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthSourceHeader, authSource);
        return client;
    }

    public async Task<AmsterfamDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AmsterfamDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        var db = new AmsterfamDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }
}
