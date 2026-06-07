using Amsterfam.Api.Endpoints;
using Amsterfam.Api.Services;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AmsterfamDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder
    .Services.AddAuthentication()
    .AddJwtBearer(options =>
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

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
