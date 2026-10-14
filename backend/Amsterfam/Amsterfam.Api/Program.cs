using Amsterfam.Api.Auth;
using Amsterfam.Api.Endpoints;
using Amsterfam.Api.Services;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddDbContext<AmsterfamDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks().AddNpgSql(connectionString!);

const string FrontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
    options.AddPolicy(
        FrontendCorsPolicy,
        policy =>
            policy
                .WithOrigins(
                    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
    )
);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEventBalanceCheck, ExpenseBalanceCheck>();
builder.Services.AddScoped<EventAutoTransitioner>();
builder.Services.AddScoped<EventLog>();
builder.Services.AddScoped<NewsPublisher>();
builder.Services.AddScoped<NotificationDispatcher>();
builder.Services.AddSingleton<DiscordTestThrottle>();
builder.Services.AddHttpClient<DiscordChannel>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<INotificationChannel>(sp => sp.GetRequiredService<DiscordChannel>());
builder.Services.AddHttpClient<PushChannel>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<INotificationChannel>(sp => sp.GetRequiredService<PushChannel>());

builder
    .Services.AddOptions<AutoTransitionOptions>()
    .Bind(builder.Configuration.GetSection(AutoTransitionOptions.SectionName))
    .Validate(
        o => o.HasValidSchedule(),
        $"{AutoTransitionOptions.SectionName}:Schedule is not a valid cron expression."
    )
    .ValidateOnStart();

builder
    .Services.AddOptions<NewsOptions>()
    .Bind(builder.Configuration.GetSection(NewsOptions.SectionName))
    .Validate(
        o => o.WorkerInterval > TimeSpan.Zero,
        $"{NewsOptions.SectionName}:WorkerInterval must be positive."
    )
    .ValidateOnStart();

builder
    .Services.AddOptions<PushOptions>()
    .Bind(builder.Configuration.GetSection(PushOptions.SectionName))
    .Validate(
        o => o.HasValidSubject(),
        $"{PushOptions.SectionName}:Subject must be a mailto: or https: URL."
    )
    .ValidateOnStart();

builder
    .Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.SectionName))
    .Validate(
        o => o.HasValidPublicUrl(),
        $"{AppOptions.SectionName}:PublicUrl (and ApiUrl, if set) must be an absolute http(s) URL."
    )
    .ValidateOnStart();

if (
    builder
        .Configuration.GetSection(AutoTransitionOptions.SectionName)
        .GetValue(nameof(AutoTransitionOptions.Enabled), true)
)
{
    builder.Services.AddHostedService<EventAutoTransitionService>();
}

if (
    builder
        .Configuration.GetSection(NewsOptions.SectionName)
        .GetValue(nameof(NewsOptions.WorkerEnabled), true)
)
{
    builder.Services.AddHostedService<NewsWorker>();
}

// In E2E test runs, swap real Authentik JWT validation for a header-based test
// scheme (mirrors Amsterfam.Tests' TestAuthHandler) so Playwright can authenticate
// without driving the OIDC flow. Gated on an exact environment-name match, so it
// can never activate in Development or Production.
var isE2E = builder.Environment.IsEnvironment("E2E");
var defaultScheme = isE2E ? TestAuthHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme;
var authBuilder = builder.Services.AddAuthentication(defaultScheme);

if (isE2E)
{
    authBuilder.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
        TestAuthHandler.SchemeName,
        _ => { }
    );
}
else
{
    authBuilder.AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Authority"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.RequireHttpsMetadata =
            options.Authority?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ?? true;

        // Authentik reports its issuer based on the host the discovery document was
        // fetched through. The backend reaches it via the internal Docker hostname,
        // but tokens are issued to the browser-facing URL, so the two differ — pin
        // the issuer we validate against to the externally visible one explicitly.
        var issuer = builder.Configuration["Jwt:Issuer"];
        if (!string.IsNullOrEmpty(issuer))
        {
            options.TokenValidationParameters.ValidIssuer = issuer;
        }
    });
}

builder.Services.AddAuthorization();

var app = builder.Build();

if (isE2E)
{
    app.Logger.LogWarning(
        "Running with E2E auth bypass — DO NOT use this environment in production."
    );
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

app.UseCors(FrontendCorsPolicy);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AmsterfamDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapVersionEndpoints();
app.MapUserEndpoints();
app.MapEventEndpoints();
app.MapEventBannerEndpoints();
app.MapAttendanceEndpoints();
app.MapJoinLinkEndpoints();
app.MapDatePollEndpoints();
app.MapPaymentMethodEndpoints();
app.MapTimelineEndpoints();
app.MapExpenseEndpoints();
app.MapNewsEndpoints();
app.MapNewsFeedEndpoints();
app.MapDiscordWebhookEndpoints();
app.MapPushEndpoints();

app.Run();

public partial class Program;
