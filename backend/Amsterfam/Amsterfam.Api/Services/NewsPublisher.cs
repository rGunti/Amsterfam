using Amsterfam.Core.Entities;

namespace Amsterfam.Api.Services;

/// <summary>
/// The single place a news post goes live (ADR-014). Like <see cref="EventLog"/>, it only
/// changes the context; the caller's SaveChanges stores the post and its timeline entry together.
/// The post must already have its id.
/// </summary>
public class NewsPublisher(EventLog log)
{
    public void Publish(NewsPost post, int actorId, DateTimeOffset now)
    {
        post.PublishedAt = now;
        log.Record(post.EventId, EventLogType.NewsPosted, actorId, data: LogData(post));
    }

    /// <summary>What the timeline keeps about a post: never the body.</summary>
    public static object LogData(NewsPost post) => new { postId = post.Id, title = post.Title };
}
