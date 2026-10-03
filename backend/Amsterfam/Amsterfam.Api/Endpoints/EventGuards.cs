using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

/// <summary>Lifecycle checks shared by the event-scoped endpoints.</summary>
internal static class EventGuards
{
    public static IResult ReadOnlyConflict(EventStatus status) =>
        TypedResults.Conflict(
            new
            {
                error = $"This event is {status.ToString().ToLowerInvariant()} and can no longer be changed.",
            }
        );

    /// <summary>
    /// Returns 404 if the event doesn't exist, 409 if it's read-only, or null when
    /// changes are allowed.
    /// </summary>
    public static async Task<IResult?> EnsureWritableAsync(AmsterfamDbContext db, Guid eventId)
    {
        var status = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => (EventStatus?)e.Status)
            .FirstOrDefaultAsync();

        if (status is null)
            return TypedResults.NotFound();
        if (EventStateMachine.IsReadOnly(status.Value))
            return ReadOnlyConflict(status.Value);
        return null;
    }

    /// <summary>
    /// Cancelled events are hidden from everyone but organisers. Returns 403 when the
    /// user can't see the event's contents, otherwise null.
    /// </summary>
    public static async Task<IResult?> EnsureVisibleAsync(
        AmsterfamDbContext db,
        Guid eventId,
        int userId
    )
    {
        var status = await db
            .Events.Where(e => e.Id == eventId)
            .Select(e => (EventStatus?)e.Status)
            .FirstOrDefaultAsync();

        if (status is null || status != EventStatus.Cancelled)
            return null;

        var isOrganiser = await db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId && a.UserId == userId && a.Role == AttendanceRole.Organiser
        );
        return EventStateMachine.IsHiddenFrom(status.Value, isOrganiser)
            ? TypedResults.Forbid()
            : null;
    }

    public static Task<bool> IsOrganiser(AmsterfamDbContext db, Guid eventId, int userId) =>
        db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId && a.UserId == userId && a.Role == AttendanceRole.Organiser
        );

    public static Task<bool> IsMember(AmsterfamDbContext db, Guid eventId, int userId) =>
        db.EventAttendances.AnyAsync(a => a.EventId == eventId && a.UserId == userId);

    /// <summary>The user's role in the event, or null if they aren't a member.</summary>
    public static Task<AttendanceRole?> RoleOf(AmsterfamDbContext db, Guid eventId, int userId) =>
        db
            .EventAttendances.Where(a => a.EventId == eventId && a.UserId == userId)
            .Select(a => (AttendanceRole?)a.Role)
            .FirstOrDefaultAsync();

    /// <summary>Attendees and organisers; pending members don't take part yet.</summary>
    public static bool IsConfirmed(AttendanceRole? role) =>
        role is AttendanceRole.Attendee or AttendanceRole.Organiser;

    public static Task<bool> IsOwner(AmsterfamDbContext db, Guid eventId, int userId) =>
        db.Events.AnyAsync(e => e.Id == eventId && e.CreatedById == userId);
}
