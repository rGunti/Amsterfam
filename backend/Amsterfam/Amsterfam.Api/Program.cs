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

app.MapUserEndpoints();
app.MapEventEndpoints();
app.MapAttendanceEndpoints();

app.Run();

public partial class Program;
