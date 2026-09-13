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
        var group = app.MapGroup("/api/v1/events/{eventId:int}/attendees").RequireAuthorization();

        group.MapGet("/", GetAttendees);
        group.MapPost("/join", Join);
        group.MapPost("/{userId:int}/confirm", Confirm);
        group.MapDelete("/{userId:int}", RemoveAttendee);
        group.MapPut("/{userId:int}", UpdateAttendee);
        group.MapPost("/{userId:int}/promote", PromoteToOrganiser);
        group.MapPost("/{userId:int}/demote", DemoteOrganiser);
        group.MapPost("/{userId:int}/transfer-ownership", TransferOwnership);

        return app;
    }

    private static async Task<IResult> GetAttendees(
        int eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var exists = await db.Events.AnyAsync(e => e.Id == eventId);
        if (!exists)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        var isMember = await IsMember(db, eventId, user.Id);

        var query = db.EventAttendances.Where(a => a.EventId == eventId);
        if (!isMember)
            query = query.Where(a => a.Role == AttendanceRole.Organiser);

        var attendees = await query
            .Include(a => a.User)
            .Select(a => new AttendeeResponse(
                a.UserId,
                a.User.DisplayName ?? a.User.Handle,
                a.User.AvatarUrl,
                a.Role.ToString(),
                a.PlannedArrival,
                a.PlannedDeparture
            ))
            .ToListAsync();

        return TypedResults.Ok(attendees);
    }

    private static async Task<IResult> Join(
        int eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        if (ev.Status != EventStatus.Open)
            return TypedResults.Conflict(new { error = "Event is not open for RSVPs." });

        var user = await currentUser.GetOrCreateAsync();

        var existing = await db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId && a.UserId == user.Id
        );

        if (existing)
            return TypedResults.Conflict(new { error = "Already attending this event." });

        db.EventAttendances.Add(
            new EventAttendance
            {
                EventId = eventId,
                UserId = user.Id,
                Role = AttendanceRole.Pending,
            }
        );

        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/v1/events/{eventId}/attendees/{user.Id}");
    }

    private static async Task<IResult> Confirm(
        int eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var requestingUser = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiser(db, eventId, requestingUser.Id))
            return TypedResults.Forbid();

        var attendance = await db.EventAttendances.FirstOrDefaultAsync(a =>
            a.EventId == eventId && a.UserId == userId
        );

        if (attendance is null)
            return TypedResults.NotFound();

        attendance.Role = AttendanceRole.Attendee;
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveAttendee(
        int eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
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
        int eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
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
        int eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
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
        int eventId,
        int userId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
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
        int eventId,
        int userId,
        [FromBody] UpdateAttendanceRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
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

    private static Task<bool> IsOrganiser(AmsterfamDbContext db, int eventId, int userId) =>
        db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId && a.UserId == userId && a.Role == AttendanceRole.Organiser
        );

    private static Task<bool> IsMember(AmsterfamDbContext db, int eventId, int userId) =>
        db.EventAttendances.AnyAsync(a => a.EventId == eventId && a.UserId == userId);

    private static Task<bool> IsOwner(AmsterfamDbContext db, int eventId, int userId) =>
        db.Events.AnyAsync(e => e.Id == eventId && e.CreatedById == userId);
}
