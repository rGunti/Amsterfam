using System.Text.Json;
using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class TimelineEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public static IEndpointRouteBuilder MapTimelineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/events/{eventId:guid}/timeline", GetTimeline).RequireAuthorization();
        return app;
    }

    /// <summary>
    /// Newest first. Pass the last entry's id as <paramref name="before"/> to get the next page.
    /// </summary>
    private static async Task<IResult> GetTimeline(
        Guid eventId,
        long? before,
        int? limit,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => new { e.CreatedById })
            .FirstOrDefaultAsync();
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        var role = await db
            .EventAttendances.Where(a => a.EventId == eventId && a.UserId == user.Id)
            .Select(a => (AttendanceRole?)a.Role)
            .FirstOrDefaultAsync();

        // Same as GET /events/{id}: non-members can't tell the event exists.
        if (role is null)
            return TypedResults.NotFound();
        if (role == AttendanceRole.Pending)
            return TypedResults.Forbid();
        if (await EventGuards.EnsureVisibleAsync(db, eventId, user.Id) is { } hidden)
            return hidden;

        var visible = EventLogEntry.VisibleTo(
            role == AttendanceRole.Organiser,
            ev.CreatedById == user.Id
        );
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var query = db.EventLogEntries.Where(l =>
            l.EventId == eventId && visible.Contains(l.Visibility)
        );
        if (before is not null)
            query = query.Where(l => l.Id < before);

        var rows = await query
            .OrderByDescending(l => l.Id)
            .Take(take)
            .Include(l => l.Actor)
            .Include(l => l.SubjectUser)
            .ToListAsync();

        return TypedResults.Ok(rows.Select(ToResponse).ToList());
    }

    private static TimelineEntryResponse ToResponse(EventLogEntry l) =>
        new(
            l.Id,
            l.Type.ToString(),
            l.Visibility.ToString(),
            l.OccurredAt,
            ToUser(l.Actor),
            ToUser(l.SubjectUser),
            l.Data is null ? null : JsonDocument.Parse(l.Data).RootElement.Clone()
        );

    private static TimelineUser? ToUser(User? u) =>
        u is null ? null : new(u.Id, u.DisplayName ?? u.Handle, u.AvatarUrl);
}
