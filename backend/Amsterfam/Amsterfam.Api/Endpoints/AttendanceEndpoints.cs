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

        return app;
    }

    private static async Task<IResult> GetAttendees(int eventId, AmsterfamDbContext db)
    {
        var exists = await db.Events.AnyAsync(e => e.Id == eventId);
        if (!exists)
            return TypedResults.NotFound();

        var attendees = await db
            .EventAttendances.Where(a => a.EventId == eventId)
            .Include(a => a.User)
            .Select(a => new AttendeeResponse(
                a.UserId,
                a.User.DisplayName,
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

        db.EventAttendances.Remove(attendance);
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
}
