using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

/// <summary>
/// The event's news feed (ADR-014): organisers post, confirmed members read. Bodies are raw
/// Markdown; the client renders them.
/// </summary>
public static class NewsEndpoints
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static IEndpointRouteBuilder MapNewsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/news").RequireAuthorization();

        group.MapGet("/", GetFeed);
        group.MapGet("/{postId:int}", GetPost);
        group.MapPost("/", CreatePost).LogsToTimeline();
        group.MapPut("/{postId:int}", UpdatePost).LogsToTimeline();
        group.MapDelete("/{postId:int}", DeletePost).LogsToTimeline();
        group.MapPost("/seen", MarkSeen).NotLoggedToTimeline("personal read marker");
        group.MapGet("/summary", GetSummary);
        group.MapPut("/{postId:int}/pin", PinPost).LogsToTimeline();
        group.MapDelete("/{postId:int}/pin", UnpinPost).LogsToTimeline();
        group
            .MapPost("/{postId:int}/ack", Acknowledge)
            .NotLoggedToTimeline("personal acknowledgement");
        group.MapGet("/{postId:int}/acks", GetAcks);
        group
            .MapPut("/{postId:int}/reactions/{emoji}", AddReaction)
            .NotLoggedToTimeline("reaction");
        group
            .MapDelete("/{postId:int}/reactions/{emoji}", RemoveReaction)
            .NotLoggedToTimeline("reaction");

        return app;
    }

    private record Caller(
        int UserId,
        bool IsOrganiser,
        bool IsWritable,
        EventAttendance Attendance
    );

    private enum Access
    {
        Read,

        /// <summary>A confirmed member's own change (ack, reaction) on a writable event.</summary>
        MemberWrite,

        /// <summary>An organiser's change on a writable event.</summary>
        Write,
    }

    /// <summary>
    /// Non-members get 404 (as for the event itself), pending members 403. Writes need an
    /// event that isn't read-only, and organiser writes an organiser.
    /// </summary>
    private static async Task<(Caller? Caller, IResult? Error)> AuthorizeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        ICurrentUserService currentUser,
        Access access
    )
    {
        var status = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => (EventStatus?)e.Status)
            .FirstOrDefaultAsync();
        if (status is null)
            return (null, TypedResults.NotFound());

        var user = await currentUser.GetOrCreateAsync();
        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == user.Id
        );
        if (attendance is null)
            return (null, TypedResults.NotFound());
        if (!EventGuards.IsConfirmed(attendance.Role))
            return (null, TypedResults.Forbid());

        var isOrganiser = attendance.Role == AttendanceRole.Organiser;
        if (EventStateMachine.IsHiddenFrom(status.Value, isOrganiser))
            return (null, TypedResults.Forbid());

        var isWritable = !EventStateMachine.IsReadOnly(status.Value);
        if (access == Access.Write && !isOrganiser)
            return (null, TypedResults.Forbid());
        if (access != Access.Read && !isWritable)
            return (null, EventGuards.ReadOnlyConflict(status.Value));

        return (new Caller(user.Id, isOrganiser, isWritable, attendance), null);
    }

    /// <summary>
    /// Published posts, newest first. Pass the last post's <c>publishedAt</c> and id as
    /// <paramref name="before"/> and <paramref name="beforeId"/> for the next page.
    /// </summary>
    private static async Task<IResult> GetFeed(
        Guid eventId,
        DateTimeOffset? before,
        int? beforeId,
        int? limit,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var published = db.NewsPosts.Where(p => p.EventId == eventId && p.PublishedAt != null);

        // Pinned posts lead the first page and are left out of the paged list, so the keyset
        // cursor only ever walks unpinned posts.
        var pinned = before is null
            ? await published
                .Where(p => p.PinnedAt != null)
                .OrderByDescending(p => p.PinnedAt)
                .Include(p => p.Author)
                .ToListAsync()
            : [];

        var query = published.Where(p => p.PinnedAt == null);
        if (before is not null)
        {
            var id = beforeId ?? int.MaxValue;
            query = query.Where(p =>
                p.PublishedAt < before || (p.PublishedAt == before && p.Id < id)
            );
        }

        var posts = await query
            .OrderByDescending(p => p.PublishedAt)
            .ThenByDescending(p => p.Id)
            .Take(take)
            .Include(p => p.Author)
            .ToListAsync();

        return TypedResults.Ok(
            new NewsFeedResponse(
                await ToResponsesAsync(db, eventId, caller, [.. pinned, .. posts]),
                caller.Attendance.NewsSeenAt
            )
        );
    }

    private static async Task<IResult> GetPost(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;

        var post = await db
            .NewsPosts.Include(p => p.Author)
            .FirstOrDefaultAsync(p =>
                p.Id == postId && p.EventId == eventId && p.PublishedAt != null
            );
        return post is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(await ToResponseAsync(db, eventId, caller, post));
    }

    private static async Task<IResult> CreatePost(
        Guid eventId,
        [FromBody] UpsertNewsPostRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        NewsPublisher publisher,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Write);
        if (caller is null)
            return error!;
        if (Validate(request) is { } invalid)
            return invalid;

        var now = time.GetUtcNow();
        var post = new NewsPost
        {
            EventId = eventId,
            AuthorId = caller.UserId,
            Title = NormaliseTitle(request.Title),
            Body = request.Body.Trim(),
            CreatedAt = now,
            PublishAt = now,
            RequiresAck = request.RequiresAck,
        };

        // The timeline entry needs the post's id, so the post is saved first; the transaction
        // keeps "entry if and only if the change" (ADR-012).
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.NewsPosts.Add(post);
        await db.SaveChangesAsync();
        publisher.Publish(post, caller.UserId, now);
        if (request.Pin)
            Pin(post, caller.UserId, now, log);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await db.Entry(post).Reference(p => p.Author).LoadAsync();
        return TypedResults.Created(
            $"/api/v1/events/{eventId}/news/{post.Id}",
            await ToResponseAsync(db, eventId, caller, post)
        );
    }

    private static async Task<IResult> UpdatePost(
        Guid eventId,
        int postId,
        [FromBody] UpsertNewsPostRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Write);
        if (caller is null)
            return error!;
        if (Validate(request) is { } invalid)
            return invalid;

        var post = await db
            .NewsPosts.Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == postId && p.EventId == eventId);
        if (post is null)
            return TypedResults.NotFound();

        post.Title = NormaliseTitle(request.Title);
        post.Body = request.Body.Trim();
        post.RequiresAck = request.RequiresAck;
        post.EditedAt = time.GetUtcNow();
        // Edits keep acknowledgements unless the organiser asks everyone to look again.
        if (request.ResetAcks)
            db.NewsAcknowledgements.RemoveRange(
                await db.NewsAcknowledgements.Where(a => a.PostId == post.Id).ToListAsync()
            );
        log.Record(
            eventId,
            EventLogType.NewsEdited,
            caller.UserId,
            data: NewsPublisher.LogData(post)
        );
        await db.SaveChangesAsync();

        return TypedResults.Ok(await ToResponseAsync(db, eventId, caller, post));
    }

    private static async Task<IResult> DeletePost(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Write);
        if (caller is null)
            return error!;

        var post = await db.NewsPosts.FirstOrDefaultAsync(p =>
            p.Id == postId && p.EventId == eventId
        );
        if (post is null)
            return TypedResults.NotFound();

        db.NewsPosts.Remove(post);
        log.Record(
            eventId,
            EventLogType.NewsDeleted,
            caller.UserId,
            data: NewsPublisher.LogData(post)
        );
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    /// <summary>Marks everything published so far as read for the caller.</summary>
    private static async Task<IResult> MarkSeen(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;

        caller.Attendance.NewsSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    /// <summary>
    /// For the event overview: pinned posts, the newest post and posts still waiting for the
    /// caller's "Got it".
    /// </summary>
    private static async Task<IResult> GetSummary(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;

        var published = db
            .NewsPosts.Where(p => p.EventId == eventId && p.PublishedAt != null)
            .Include(p => p.Author);
        var pinned = await published
            .Where(p => p.PinnedAt != null)
            .OrderByDescending(p => p.PinnedAt)
            .ToListAsync();
        var latest = await published
            .OrderByDescending(p => p.PublishedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();
        var needsAck = await published
            .Where(p =>
                p.RequiresAck
                && !db.NewsAcknowledgements.Any(a => a.PostId == p.Id && a.UserId == caller.UserId)
            )
            .OrderByDescending(p => p.PublishedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync();

        var all = pinned
            .Concat(needsAck)
            .Concat(latest is null ? [] : [latest])
            .DistinctBy(p => p.Id)
            .ToList();
        var byId = (await ToResponsesAsync(db, eventId, caller, all)).ToDictionary(r => r.Id);

        return TypedResults.Ok(
            new NewsSummaryResponse(
                pinned.Select(p => byId[p.Id]).ToList(),
                latest is null ? null : byId[latest.Id],
                needsAck.Select(p => byId[p.Id]).ToList()
            )
        );
    }

    private static async Task<IResult> PinPost(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Write);
        if (caller is null)
            return error!;

        var post = await FindPublishedAsync(db, eventId, postId);
        if (post is null)
            return TypedResults.NotFound();

        // Pinning twice is a no-op, so the timeline only shows real changes.
        if (post.PinnedAt is null)
        {
            Pin(post, caller.UserId, time.GetUtcNow(), log);
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnpinPost(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Write);
        if (caller is null)
            return error!;

        var post = await FindPublishedAsync(db, eventId, postId);
        if (post is null)
            return TypedResults.NotFound();

        if (post.PinnedAt is not null)
        {
            post.PinnedAt = null;
            log.Record(
                eventId,
                EventLogType.NewsUnpinned,
                caller.UserId,
                data: NewsPublisher.LogData(post)
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static void Pin(NewsPost post, int actorId, DateTimeOffset now, EventLog log)
    {
        post.PinnedAt = now;
        log.Record(
            post.EventId,
            EventLogType.NewsPinned,
            actorId,
            data: NewsPublisher.LogData(post)
        );
    }

    /// <summary>"Got it". Idempotent: acknowledging again keeps the first time.</summary>
    private static async Task<IResult> Acknowledge(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.MemberWrite);
        if (caller is null)
            return error!;

        var post = await FindPublishedAsync(db, eventId, postId);
        if (post is null)
            return TypedResults.NotFound();
        if (!post.RequiresAck)
            return TypedResults.BadRequest(
                new { error = "This post doesn't ask for an acknowledgement." }
            );

        var exists = await db.NewsAcknowledgements.AnyAsync(a =>
            a.PostId == postId && a.UserId == caller.UserId
        );
        if (!exists)
        {
            db.NewsAcknowledgements.Add(
                new NewsAcknowledgement
                {
                    PostId = postId,
                    UserId = caller.UserId,
                    AckedAt = time.GetUtcNow(),
                }
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Who has acknowledged a post, and which current confirmed members haven't. Pending
    /// members are never "missing".
    /// </summary>
    private static async Task<IResult> GetAcks(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;
        if (!caller.IsOrganiser)
            return TypedResults.Forbid();

        var post = await FindPublishedAsync(db, eventId, postId);
        if (post is null)
            return TypedResults.NotFound();

        var members = await db
            .EventAttendances.Where(a =>
                a.EventId == eventId
                && (a.Role == AttendanceRole.Attendee || a.Role == AttendanceRole.Organiser)
            )
            .Select(a => a.User)
            .ToListAsync();
        var acks = await db
            .NewsAcknowledgements.Where(a => a.PostId == postId)
            .ToDictionaryAsync(a => a.UserId, a => a.AckedAt);

        var acknowledged = members
            .Where(u => acks.ContainsKey(u.Id))
            .Select(u => new NewsAckEntry(ToTimelineUser(u), acks[u.Id]))
            .OrderBy(e => e.At)
            .ToList();
        var missing = members
            .Where(u => !acks.ContainsKey(u.Id))
            .Select(ToTimelineUser)
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return TypedResults.Ok(new NewsAcksResponse(acknowledged, missing));
    }

    private static async Task<IResult> AddReaction(
        Guid eventId,
        int postId,
        string emoji,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.MemberWrite);
        if (caller is null)
            return error!;
        if (!NewsReaction.IsAllowed(emoji))
            return TypedResults.BadRequest(new { error = "That reaction isn't available." });
        if (await FindPublishedAsync(db, eventId, postId) is null)
            return TypedResults.NotFound();

        var exists = await db.NewsReactions.AnyAsync(r =>
            r.PostId == postId && r.UserId == caller.UserId && r.Emoji == emoji
        );
        if (!exists)
        {
            db.NewsReactions.Add(
                new NewsReaction
                {
                    PostId = postId,
                    UserId = caller.UserId,
                    Emoji = emoji,
                    CreatedAt = time.GetUtcNow(),
                }
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveReaction(
        Guid eventId,
        int postId,
        string emoji,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.MemberWrite);
        if (caller is null)
            return error!;
        if (!NewsReaction.IsAllowed(emoji))
            return TypedResults.BadRequest(new { error = "That reaction isn't available." });
        if (await FindPublishedAsync(db, eventId, postId) is null)
            return TypedResults.NotFound();

        var reaction = await db.NewsReactions.FirstOrDefaultAsync(r =>
            r.PostId == postId && r.UserId == caller.UserId && r.Emoji == emoji
        );
        if (reaction is not null)
        {
            db.NewsReactions.Remove(reaction);
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static Task<NewsPost?> FindPublishedAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int postId
    ) =>
        db.NewsPosts.FirstOrDefaultAsync(p =>
            p.Id == postId && p.EventId == eventId && p.PublishedAt != null
        );

    /// <summary>
    /// Published posts the user didn't write and hasn't seen yet, for the nav badge.
    /// </summary>
    internal static Task<int> CountUnreadAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int userId,
        DateTimeOffset? seenAt
    ) =>
        db.NewsPosts.CountAsync(p =>
            p.EventId == eventId
            && p.PublishedAt != null
            && p.AuthorId != userId
            && (seenAt == null || p.PublishedAt > seenAt)
        );

    private static IResult? Validate(UpsertNewsPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return TypedResults.BadRequest(new { error = "A post needs some text." });
        if (request.Body.Trim().Length > NewsPost.MaxBodyLength)
            return TypedResults.BadRequest(
                new { error = $"Posts can be at most {NewsPost.MaxBodyLength} characters long." }
            );
        if (NormaliseTitle(request.Title)?.Length > NewsPost.MaxTitleLength)
            return TypedResults.BadRequest(
                new { error = $"Titles can be at most {NewsPost.MaxTitleLength} characters long." }
            );
        return null;
    }

    private static string? NormaliseTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    private static async Task<NewsPostResponse> ToResponseAsync(
        AmsterfamDbContext db,
        Guid eventId,
        Caller caller,
        NewsPost post
    ) => (await ToResponsesAsync(db, eventId, caller, [post]))[0];

    /// <summary>
    /// Builds responses for a set of posts with a fixed number of queries. Ack counts only
    /// include current confirmed members, matching <c>ackTotal</c>, and are organiser-only.
    /// </summary>
    private static async Task<List<NewsPostResponse>> ToResponsesAsync(
        AmsterfamDbContext db,
        Guid eventId,
        Caller caller,
        IReadOnlyList<NewsPost> posts
    )
    {
        var ids = posts.Select(p => p.Id).ToList();
        var reactions = await db
            .NewsReactions.Where(r => ids.Contains(r.PostId))
            .Select(r => new
            {
                r.PostId,
                r.UserId,
                r.Emoji,
            })
            .ToListAsync();
        var acks = await db
            .NewsAcknowledgements.Where(a => ids.Contains(a.PostId))
            .Select(a => new
            {
                a.PostId,
                a.UserId,
                Confirmed = db.EventAttendances.Any(m =>
                    m.EventId == eventId
                    && m.UserId == a.UserId
                    && (m.Role == AttendanceRole.Attendee || m.Role == AttendanceRole.Organiser)
                ),
            })
            .ToListAsync();
        int? ackTotal = caller.IsOrganiser
            ? await db.EventAttendances.CountAsync(m =>
                m.EventId == eventId
                && (m.Role == AttendanceRole.Attendee || m.Role == AttendanceRole.Organiser)
            )
            : null;

        return posts
            .Select(p =>
            {
                var postReactions = reactions.Where(r => r.PostId == p.Id).ToList();
                var postAcks = acks.Where(a => a.PostId == p.Id).ToList();
                return new NewsPostResponse(
                    p.Id,
                    p.Title,
                    p.Body,
                    ToTimelineUser(p.Author),
                    p.CreatedAt,
                    p.PublishedAt,
                    p.EditedAt,
                    caller.IsOrganiser && caller.IsWritable,
                    p.PinnedAt,
                    p.RequiresAck,
                    postAcks.Any(a => a.UserId == caller.UserId),
                    NewsReaction
                        .Allowed.Select(e => new NewsReactionCount(
                            e,
                            postReactions.Count(r => r.Emoji == e)
                        ))
                        .Where(c => c.Count > 0)
                        .ToList(),
                    postReactions
                        .Where(r => r.UserId == caller.UserId)
                        .Select(r => r.Emoji)
                        .ToList(),
                    caller.IsOrganiser && p.RequiresAck ? postAcks.Count(a => a.Confirmed) : null,
                    p.RequiresAck ? ackTotal : null
                );
            })
            .ToList();
    }

    private static TimelineUser ToTimelineUser(User u) =>
        new(u.Id, u.DisplayName ?? u.Handle, u.AvatarUrl);
}
