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

        return app;
    }

    private record Caller(
        int UserId,
        bool IsOrganiser,
        bool IsWritable,
        EventAttendance Attendance
    );

    /// <summary>
    /// Non-members get 404 (as for the event itself), pending members 403. Writes need an
    /// organiser and an event that isn't read-only.
    /// </summary>
    private static async Task<(Caller? Caller, IResult? Error)> AuthorizeAsync(
        AmsterfamDbContext db,
        Guid eventId,
        ICurrentUserService currentUser,
        bool write
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
        if (write && !isOrganiser)
            return (null, TypedResults.Forbid());
        if (write && !isWritable)
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
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: false);
        if (caller is null)
            return error!;

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var query = db.NewsPosts.Where(p => p.EventId == eventId && p.PublishedAt != null);
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
                posts.Select(p => ToResponse(p, caller)).ToList(),
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
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: false);
        if (caller is null)
            return error!;

        var post = await db
            .NewsPosts.Include(p => p.Author)
            .FirstOrDefaultAsync(p =>
                p.Id == postId && p.EventId == eventId && p.PublishedAt != null
            );
        return post is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(post, caller));
    }

    private static async Task<IResult> CreatePost(
        Guid eventId,
        [FromBody] UpsertNewsPostRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        NewsPublisher publisher
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
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
        };

        // The timeline entry needs the post's id, so the post is saved first; the transaction
        // keeps "entry if and only if the change" (ADR-012).
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.NewsPosts.Add(post);
        await db.SaveChangesAsync();
        publisher.Publish(post, caller.UserId, now);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await db.Entry(post).Reference(p => p.Author).LoadAsync();
        return TypedResults.Created(
            $"/api/v1/events/{eventId}/news/{post.Id}",
            ToResponse(post, caller)
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
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
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
        post.EditedAt = time.GetUtcNow();
        log.Record(
            eventId,
            EventLogType.NewsEdited,
            caller.UserId,
            data: NewsPublisher.LogData(post)
        );
        await db.SaveChangesAsync();

        return TypedResults.Ok(ToResponse(post, caller));
    }

    private static async Task<IResult> DeletePost(
        Guid eventId,
        int postId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        EventLog log
    )
    {
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: true);
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
        var (caller, error) = await AuthorizeAsync(db, eventId, currentUser, write: false);
        if (caller is null)
            return error!;

        caller.Attendance.NewsSeenAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

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

    private static NewsPostResponse ToResponse(NewsPost p, Caller caller) =>
        new(
            p.Id,
            p.Title,
            p.Body,
            new TimelineUser(
                p.Author.Id,
                p.Author.DisplayName ?? p.Author.Handle,
                p.Author.AvatarUrl
            ),
            p.CreatedAt,
            p.PublishedAt,
            p.EditedAt,
            caller.IsOrganiser && caller.IsWritable
        );
}
