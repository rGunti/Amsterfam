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
    public const int DefaultCommentLimit = 50;
    public const int MaxCommentLimit = 100;

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
        group.MapGet("/{postId:int}/image", GetImage);
        group.MapPut("/{postId:int}/image", PutImage).LogsToTimeline();
        group.MapDelete("/{postId:int}/image", DeleteImage).LogsToTimeline();
        group.MapGet("/{postId:int}/comments", GetComments);
        group
            .MapPost("/{postId:int}/comments", AddComment)
            .NotLoggedToTimeline("conversation, shown on the post");
        group
            .MapPut("/{postId:int}/comments/{commentId:int}", EditComment)
            .NotLoggedToTimeline("conversation, shown on the post");
        // Only an organiser removing someone else's comment is logged (moderation).
        group.MapDelete("/{postId:int}/comments/{commentId:int}", DeleteComment).LogsToTimeline();
        group
            .MapPut("/{postId:int}/comments/{commentId:int}/reactions/{emoji}", AddCommentReaction)
            .NotLoggedToTimeline("reaction");
        group
            .MapDelete(
                "/{postId:int}/comments/{commentId:int}/reactions/{emoji}",
                RemoveCommentReaction
            )
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
            CommentsEnabled = request.CommentsEnabled,
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
        post.CommentsEnabled = request.CommentsEnabled;
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

        var imageId = post.ImageFileId;
        db.NewsPosts.Remove(post);
        log.Record(
            eventId,
            EventLogType.NewsDeleted,
            caller.UserId,
            data: NewsPublisher.LogData(post)
        );
        await db.SaveChangesAsync();
        // ImageFileId only nulls itself when the file goes, so the file has to go explicitly.
        if (imageId is { } id)
            await db.EventFiles.Where(f => f.Id == id).ExecuteDeleteAsync();

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
                && p.AuthorId != caller.UserId
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
        if (post.AuthorId == caller.UserId)
            return TypedResults.BadRequest(
                new { error = "You wrote this post, so there's nothing to acknowledge." }
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
    /// members and the post's author are never "missing".
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
                && a.UserId != post.AuthorId
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

    /// <summary>
    /// The post's image. Unpublished posts (scheduled ones, #141) are organiser-only, like
    /// the posts themselves.
    /// </summary>
    private static async Task<IResult> GetImage(
        Guid eventId,
        int postId,
        HttpRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;

        var post = await db
            .NewsPosts.Where(p =>
                p.Id == postId
                && p.EventId == eventId
                && (p.PublishedAt != null || caller.IsOrganiser)
            )
            .Select(p => new { p.ImageFileId })
            .FirstOrDefaultAsync();
        if (post is null)
            return TypedResults.NotFound();

        return await ImageUpload.ServeAsync(db, post.ImageFileId, request);
    }

    /// <summary>
    /// Attaches or replaces the post's image. <paramref name="initial"/> is for the compose
    /// page adding the image straight after creating the post: it isn't an edit, so it doesn't
    /// mark the post edited or add a timeline entry. It's ignored once the post has an image.
    /// </summary>
    private static async Task<IResult> PutImage(
        Guid eventId,
        int postId,
        string? fileName,
        bool? initial,
        HttpRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
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

        var (image, invalid) = await ImageUpload.ReadAsync(request, "The image");
        if (image is null)
            return invalid!;

        var previousId = post.ImageFileId;
        var now = time.GetUtcNow();
        var file = new EventFile
        {
            EventId = eventId,
            FileName = ImageUpload.CleanFileName(fileName, "image"),
            ContentType = image.ContentType,
            Size = image.Data.Length,
            Data = image.Data,
            UploadedById = caller.UserId,
            CreatedAt = now,
        };
        db.EventFiles.Add(file);
        post.ImageFileId = file.Id;
        if (initial != true || previousId is not null)
            MarkImageEdited(post, caller.UserId, now, log);
        await db.SaveChangesAsync();

        if (previousId is { } old)
            await db.EventFiles.Where(f => f.Id == old).ExecuteDeleteAsync();

        return TypedResults.Ok(new { imageFileId = file.Id });
    }

    private static async Task<IResult> DeleteImage(
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

        var post = await db.NewsPosts.FirstOrDefaultAsync(p =>
            p.Id == postId && p.EventId == eventId
        );
        if (post is null)
            return TypedResults.NotFound();
        if (post.ImageFileId is not { } imageId)
            return TypedResults.NoContent();

        post.ImageFileId = null;
        MarkImageEdited(post, caller.UserId, time.GetUtcNow(), log);
        await db.SaveChangesAsync();
        await db.EventFiles.Where(f => f.Id == imageId).ExecuteDeleteAsync();
        return TypedResults.NoContent();
    }

    private static void MarkImageEdited(
        NewsPost post,
        int actorId,
        DateTimeOffset now,
        EventLog log
    )
    {
        post.EditedAt = now;
        log.Record(
            post.EventId,
            EventLogType.NewsEdited,
            actorId,
            data: NewsPublisher.LogData(post, changed: ["image"])
        );
    }

    /// <summary>
    /// A published post's comments, oldest first. Pass the last comment's <c>createdAt</c> and
    /// id as <paramref name="after"/> and <paramref name="afterId"/> for the next page, or to
    /// fetch only what's new.
    /// </summary>
    private static async Task<IResult> GetComments(
        Guid eventId,
        int postId,
        DateTimeOffset? after,
        int? afterId,
        int? limit,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.Read);
        if (caller is null)
            return error!;
        if (await FindPublishedAsync(db, eventId, postId) is null)
            return TypedResults.NotFound();

        var take = Math.Clamp(limit ?? DefaultCommentLimit, 1, MaxCommentLimit);
        var query = db.NewsComments.Where(c => c.PostId == postId);
        if (after is not null)
        {
            var id = afterId ?? 0;
            query = query.Where(c => c.CreatedAt > after || (c.CreatedAt == after && c.Id > id));
        }

        var comments = await query
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(take)
            .Include(c => c.Author)
            .ToListAsync();
        return TypedResults.Ok(await ToCommentResponsesAsync(db, caller, comments));
    }

    private static async Task<IResult> AddComment(
        Guid eventId,
        int postId,
        [FromBody] UpsertNewsCommentRequest request,
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
        if (ValidateComment(post, request) is { } invalid)
            return invalid;

        var comment = new NewsComment
        {
            PostId = postId,
            AuthorId = caller.UserId,
            Body = request.Body.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        db.NewsComments.Add(comment);
        await db.SaveChangesAsync();

        await db.Entry(comment).Reference(c => c.Author).LoadAsync();
        return TypedResults.Created(
            $"/api/v1/events/{eventId}/news/{postId}/comments/{comment.Id}",
            (await ToCommentResponsesAsync(db, caller, [comment]))[0]
        );
    }

    /// <summary>Only the comment's author can edit it, and only while comments are on.</summary>
    private static async Task<IResult> EditComment(
        Guid eventId,
        int postId,
        int commentId,
        [FromBody] UpsertNewsCommentRequest request,
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
        var comment = await db
            .NewsComments.Include(c => c.Author)
            .FirstOrDefaultAsync(c => c.Id == commentId && c.PostId == postId);
        if (comment is null)
            return TypedResults.NotFound();
        if (comment.AuthorId != caller.UserId)
            return TypedResults.Forbid();
        if (ValidateComment(post, request) is { } invalid)
            return invalid;

        comment.Body = request.Body.Trim();
        comment.EditedAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return TypedResults.Ok((await ToCommentResponsesAsync(db, caller, [comment]))[0]);
    }

    /// <summary>
    /// The author or any organiser can delete a comment, even with comments turned off. An
    /// organiser removing someone else's comment leaves an organisers-only timeline entry
    /// without the text; deleting your own logs nothing.
    /// </summary>
    private static async Task<IResult> DeleteComment(
        Guid eventId,
        int postId,
        int commentId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, Access.MemberWrite);
        if (caller is null)
            return error!;
        var post = await FindPublishedAsync(db, eventId, postId);
        if (post is null)
            return TypedResults.NotFound();
        var comment = await db.NewsComments.FirstOrDefaultAsync(c =>
            c.Id == commentId && c.PostId == postId
        );
        if (comment is null)
            return TypedResults.NotFound();
        if (comment.AuthorId != caller.UserId && !caller.IsOrganiser)
            return TypedResults.Forbid();

        db.NewsComments.Remove(comment);
        if (comment.AuthorId != caller.UserId)
            log.Record(
                eventId,
                EventLogType.NewsCommentRemoved,
                caller.UserId,
                subjectUserId: comment.AuthorId,
                data: NewsPublisher.LogData(post)
            );
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static IResult? ValidateComment(NewsPost post, UpsertNewsCommentRequest request)
    {
        if (!post.CommentsEnabled)
            return TypedResults.BadRequest(
                new { error = "Comments are turned off for this post." }
            );
        if (string.IsNullOrWhiteSpace(request.Body))
            return TypedResults.BadRequest(new { error = "A comment needs some text." });
        if (request.Body.Trim().Length > NewsComment.MaxBodyLength)
            return TypedResults.BadRequest(
                new
                {
                    error = $"Comments can be at most {NewsComment.MaxBodyLength} characters long.",
                }
            );
        return null;
    }

    /// <summary>
    /// Reacting to a comment. Allowed even with comments turned off, like post reactions:
    /// it adds no text to the conversation.
    /// </summary>
    private static async Task<IResult> AddCommentReaction(
        Guid eventId,
        int postId,
        int commentId,
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
        if (!await CommentExistsAsync(db, eventId, postId, commentId))
            return TypedResults.NotFound();

        var exists = await db.NewsCommentReactions.AnyAsync(r =>
            r.CommentId == commentId && r.UserId == caller.UserId && r.Emoji == emoji
        );
        if (!exists)
        {
            db.NewsCommentReactions.Add(
                new NewsCommentReaction
                {
                    CommentId = commentId,
                    UserId = caller.UserId,
                    Emoji = emoji,
                    CreatedAt = time.GetUtcNow(),
                }
            );
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveCommentReaction(
        Guid eventId,
        int postId,
        int commentId,
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
        if (!await CommentExistsAsync(db, eventId, postId, commentId))
            return TypedResults.NotFound();

        var reaction = await db.NewsCommentReactions.FirstOrDefaultAsync(r =>
            r.CommentId == commentId && r.UserId == caller.UserId && r.Emoji == emoji
        );
        if (reaction is not null)
        {
            db.NewsCommentReactions.Remove(reaction);
            await db.SaveChangesAsync();
        }
        return TypedResults.NoContent();
    }

    /// <summary>The comment exists on that post, and the post is a published one in the event.</summary>
    private static Task<bool> CommentExistsAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int postId,
        int commentId
    ) =>
        db.NewsComments.AnyAsync(c =>
            c.Id == commentId
            && c.PostId == postId
            && c.Post.EventId == eventId
            && c.Post.PublishedAt != null
        );

    /// <summary>Builds comment responses, with reactions, in one extra query.</summary>
    private static async Task<List<NewsCommentResponse>> ToCommentResponsesAsync(
        AmsterfamDbContext db,
        Caller caller,
        IReadOnlyList<NewsComment> comments
    )
    {
        var ids = comments.Select(c => c.Id).ToList();
        var reactions = await db
            .NewsCommentReactions.Where(r => ids.Contains(r.CommentId))
            .Select(r => new
            {
                r.CommentId,
                r.UserId,
                r.Emoji,
            })
            .ToListAsync();

        return comments
            .Select(c =>
            {
                var isAuthor = c.AuthorId == caller.UserId;
                var commentReactions = reactions.Where(r => r.CommentId == c.Id).ToList();
                return new NewsCommentResponse(
                    c.Id,
                    ToTimelineUser(c.Author),
                    c.Body,
                    c.CreatedAt,
                    c.EditedAt,
                    caller.IsWritable && isAuthor,
                    caller.IsWritable && (isAuthor || caller.IsOrganiser),
                    NewsReaction
                        .Allowed.Select(e => new NewsReactionCount(
                            e,
                            commentReactions.Count(r => r.Emoji == e)
                        ))
                        .Where(r => r.Count > 0)
                        .ToList(),
                    commentReactions
                        .Where(r => r.UserId == caller.UserId)
                        .Select(r => r.Emoji)
                        .ToList()
                );
            })
            .ToList();
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
    /// include current confirmed members other than the author, matching <c>ackTotal</c>,
    /// and are organiser-only.
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
            .Select(a => new { a.PostId, a.UserId })
            .ToListAsync();
        var seenAt = caller.Attendance.NewsSeenAt;
        var comments = await db
            .NewsComments.Where(c => ids.Contains(c.PostId))
            .GroupBy(c => c.PostId)
            .Select(g => new
            {
                PostId = g.Key,
                Count = g.Count(),
                New = g.Count(c =>
                    c.AuthorId != caller.UserId && (seenAt == null || c.CreatedAt > seenAt)
                ),
            })
            .ToDictionaryAsync(c => c.PostId);
        // Organisers see counts over current confirmed members, leaving out the author.
        var memberIds = caller.IsOrganiser
            ? await db
                .EventAttendances.Where(m =>
                    m.EventId == eventId
                    && (m.Role == AttendanceRole.Attendee || m.Role == AttendanceRole.Organiser)
                )
                .Select(m => m.UserId)
                .ToHashSetAsync()
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
                    p.RequiresAck && p.AuthorId != caller.UserId,
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
                    memberIds is not null && p.RequiresAck
                        ? postAcks.Count(a =>
                            a.UserId != p.AuthorId && memberIds.Contains(a.UserId)
                        )
                        : null,
                    memberIds is not null && p.RequiresAck
                        ? memberIds.Count(id => id != p.AuthorId)
                        : null,
                    p.ImageFileId,
                    p.CommentsEnabled,
                    comments.GetValueOrDefault(p.Id)?.Count ?? 0,
                    comments.GetValueOrDefault(p.Id)?.New ?? 0
                );
            })
            .ToList();
    }

    private static TimelineUser ToTimelineUser(User u) =>
        new(u.Id, u.DisplayName ?? u.Handle, u.AvatarUrl);
}
