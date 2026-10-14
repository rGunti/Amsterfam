using Amsterfam.Api.Auth;
using Amsterfam.Api.Services;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Amsterfam.Tests.Infrastructure;

public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PublicUrl = "https://amsterfam.test";

    /// <summary>Different from <see cref="PublicUrl"/>, so tests catch links to the wrong one.</summary>
    public const string ApiUrl = "https://api.amsterfam.test";

    /// <summary>Every call the app makes to Discord ends up here instead.</summary>
    public StubDiscordHandler Discord { get; } = new();

    /// <summary>Every web push the app sends ends up here instead.</summary>
    public StubPushHandler Push { get; } = new();

    /// <summary>Every email the app sends ends up here instead.</summary>
    public FakeEmailSender Email { get; } = new();

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
        builder.UseSetting("Push:VapidPublicKey", PushTestKeys.VapidPublicKey);
        builder.UseSetting("Push:VapidPrivateKey", PushTestKeys.VapidPrivateKey);
        builder.UseSetting("Push:Subject", PushTestKeys.Subject);
        builder.UseSetting("Smtp:Host", "smtp.amsterfam.test");
        builder.UseSetting("Smtp:From", "Amsterfam <news@amsterfam.test>");
        // ...and notifications via NotificationDispatcher.RunAsync.

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

            services
                .AddHttpClient<DiscordChannel>()
                .ConfigurePrimaryHttpMessageHandler(() => Discord)
                // The stub is shared, so the factory must never dispose it.
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services
                .AddHttpClient<PushChannel>()
                .ConfigurePrimaryHttpMessageHandler(() => Push)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.Replace(ServiceDescriptor.Singleton<IEmailSender>(Email));
        });
    }

    public HttpClient CreateClientWithUser(
        string externalId,
        string? authSource = null,
        bool noEmail = false
    )
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, externalId);
        if (authSource is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthSourceHeader, authSource);
        if (noEmail)
            client.DefaultRequestHeaders.Add(TestAuthHandler.NoEmailHeader, "1");
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
