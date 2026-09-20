using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:guid}/attendees").RequireAuthorization();

        group.MapGet("/", GetAttendees);
        group.MapPost("/{userId:int}/confirm", Confirm);
        group.MapDelete("/{userId:int}", RemoveAttendee);
        group.MapPut("/{userId:int}", UpdateAttendee);
        group.MapPost("/{userId:int}/promote", PromoteToOrganiser);
        group.MapPost("/{userId:int}/demote", DemoteOrganiser);
        group.MapPost("/{userId:int}/transfer-ownership", TransferOwnership);

        return app;
    }

    private static async Task<IResult> GetAttendees(
        Guid eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var exists = await db.Events.AnyAsync(e => e.Id == eventId);
        if (!exists)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (await EventGuards.EnsureVisibleAsync(db, eventId, user.Id) is { } hidden)
            return hidden;

        var isMember = await IsMember(db, eventId, user.Id);

        var query = db.EventAttendances.Where(a => a.EventId == eventId);
        if (!isMember)
            query = query.Where(a => a.Role == AttendanceRole.Organiser);

        var isOrganiser = await IsOrganiser(db, eventId, user.Id);

        var rows = await query.Include(a => a.User).Include(a => a.JoinLink).ToListAsync();

        // Which link someone came in through is only useful to organisers.
        var attendees = rows.Select(a => new AttendeeResponse(
                a.UserId,
                a.User.DisplayName ?? a.User.Handle,
                a.User.AvatarUrl,
                a.Role.ToString(),
                a.PlannedArrival,
                a.PlannedDeparture,
                a.RequestedOrganiser,
                isOrganiser ? a.JoinLink?.DisplayLabel : null
            ))
            .ToList();

        return TypedResults.Ok(attendees);
    }

    private static async Task<IResult> Confirm(
        Guid eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiser(db, eventId, requestingUser.Id))
            return TypedResults.Forbid();

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        if (attendance.Role != AttendanceRole.Pending)
            return TypedResults.Conflict(new { error = "This attendee is already confirmed." });

        if (attendance.RequestedOrganiser)
        {
            if (!await IsOwner(db, eventId, requestingUser.Id))
                return TypedResults.Forbid();
            attendance.Role = AttendanceRole.Organiser;
        }
        else
        {
            attendance.Role = AttendanceRole.Attendee;
        }

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveAttendee(
        Guid eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        var isSelf = requestingUser.Id == userId;
        var isOrganiser = await IsOrganiser(db, eventId, requestingUser.Id);

        if (!isSelf && !isOrganiser)
            return TypedResults.Forbid();

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        var isOwner = await IsOwner(db, eventId, requestingUser.Id);

        if (isSelf && isOwner)
            return TypedResults.Conflict(
                new { error = "Transfer ownership to another organiser before leaving the event." }
            );

        if (!isSelf && attendance.Role == AttendanceRole.Organiser && !isOwner)
            return TypedResults.Forbid();

        db.EventAttendances.Remove(attendance);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PromoteToOrganiser(
        Guid eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        if (!await IsOwner(db, eventId, requestingUser.Id))
            return TypedResults.Forbid();

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        if (attendance.Role != AttendanceRole.Attendee)
            return TypedResults.Conflict(
                new { error = "Only confirmed attendees can be promoted to organiser." }
            );

        attendance.Role = AttendanceRole.Organiser;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> DemoteOrganiser(
        Guid eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        if (!await IsOwner(db, eventId, requestingUser.Id))
            return TypedResults.Forbid();

        if (userId == requestingUser.Id)
            return TypedResults.Conflict(new { error = "The owner cannot demote themselves." });

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        if (attendance.Role != AttendanceRole.Organiser)
            return TypedResults.Conflict(new { error = "This attendee is not an organiser." });

        attendance.Role = AttendanceRole.Attendee;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> TransferOwnership(
        Guid eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        if (!await IsOwner(db, eventId, requestingUser.Id))
            return TypedResults.Forbid();

        if (userId == requestingUser.Id)
            return TypedResults.Conflict(
                new { error = "You are already the owner of this event." }
            );

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null || attendance.Role != AttendanceRole.Organiser)
            return TypedResults.Conflict(
                new { error = "Ownership can only be transferred to an existing organiser." }
            );

        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        ev.CreatedById = userId;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UpdateAttendee(
        Guid eventId,
        int userId,
        [FromBody] UpdateAttendanceRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        if (await EventGuards.EnsureWritableAsync(db, eventId) is { } notWritable)
            return notWritable;

        var requestingUser = await currentUser.GetOrCreateAsync();
        var isSelf = requestingUser.Id == userId;
        var isOrganiser = await IsOrganiser(db, eventId, requestingUser.Id);

        if (!isSelf && !isOrganiser)
            return TypedResults.Forbid();

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        attendance.PlannedArrival = request.PlannedArrival;
        attendance.PlannedDeparture = request.PlannedDeparture;

        if (isOrganiser)
            attendance.CostOverride = request.CostOverride;

        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static Task<bool> IsOrganiser(AmsterfamDbContext db, Guid eventId, int userId) =>
        EventGuards.IsOrganiser(db, eventId, userId);

    private static Task<bool> IsMember(AmsterfamDbContext db, Guid eventId, int userId) =>
        EventGuards.IsMember(db, eventId, userId);

    private static Task<bool> IsOwner(AmsterfamDbContext db, Guid eventId, int userId) =>
        EventGuards.IsOwner(db, eventId, userId);
}
