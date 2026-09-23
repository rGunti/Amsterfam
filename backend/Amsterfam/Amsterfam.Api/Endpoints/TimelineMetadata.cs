namespace Amsterfam.Api.Endpoints;

/// <summary>Marks an endpoint that records its changes on the event timeline (ADR-012).</summary>
public sealed class LogsToTimelineMetadata;

/// <summary>Marks an endpoint that deliberately doesn't record to the timeline, and why.</summary>
public sealed record NotLoggedToTimelineMetadata(string Reason);

/// <summary>
/// Every POST/PUT/PATCH/DELETE endpoint must carry exactly one of these markers; a test
/// fails otherwise, so adding a mutating endpoint forces a decision about logging.
/// </summary>
public static class TimelineMetadataExtensions
{
    /// <summary>The handler calls <c>EventLog</c> before saving.</summary>
    public static TBuilder LogsToTimeline<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new LogsToTimelineMetadata());

    public static TBuilder NotLoggedToTimeline<TBuilder>(this TBuilder builder, string reason)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new NotLoggedToTimelineMetadata(reason));
}
