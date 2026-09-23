using Amsterfam.Api.Endpoints;
using Amsterfam.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Amsterfam.Tests.Api;

// ADR-012: every change to an event is logged. This can't check that an endpoint logs the
// right thing, but it makes sure nobody adds a mutating endpoint without deciding.
public class TimelineCoverageTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    [Fact]
    public void EveryMutatingEndpoint_DeclaresWhetherItLogsToTheTimeline()
    {
        var endpoints = api
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e =>
                e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Any(MutatingMethods.Contains) == true
            )
            .ToList();

        Assert.NotEmpty(endpoints);

        var problems = endpoints
            .Select(e =>
            {
                var logs = e.Metadata.GetMetadata<LogsToTimelineMetadata>() is not null;
                var notLogged = e.Metadata.GetMetadata<NotLoggedToTimelineMetadata>();
                var route =
                    $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {e.RoutePattern.RawText}";
                return (logs, notLogged) switch
                {
                    (false, null) =>
                        $"{route}: add .LogsToTimeline() or .NotLoggedToTimeline(reason) (see ADR-012)",
                    (true, not null) => $"{route}: marked both logged and not logged",
                    (_, { Reason: var r }) when string.IsNullOrWhiteSpace(r) =>
                        $"{route}: .NotLoggedToTimeline needs a reason",
                    _ => null,
                };
            })
            .OfType<string>()
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
