using System.Reflection;

namespace Amsterfam.Api.Endpoints;

public static class VersionEndpoints
{
    public static IEndpointRouteBuilder MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/version");

        group.MapGet("/", GetVersion);

        return app;
    }

    private static IResult GetVersion()
    {
        var infoVersion =
            typeof(VersionEndpoints)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "unknown";
        var parts = infoVersion.Split('+', 2);

        return TypedResults.Ok(
            new { version = parts[0], sha = parts.Length > 1 ? parts[1] : "unknown" }
        );
    }
}
