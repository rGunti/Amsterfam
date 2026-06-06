using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events").RequireAuthorization();

        group.MapGet("/", GetEvents);
        group.MapPost("/", CreateEvent);
        group.MapGet("/{id:int}", GetEvent);
        group.MapPut("/{id:int}", UpdateEvent);
        group.MapDelete("/{id:int}", DeleteEvent);
        group.MapPost("/{id:int}/publish", PublishEvent);
        group.MapPost("/{id:int}/close", CloseEvent);

        return app;
    }

    private static async Task<IResult> GetEvents(AmsterfamDbContext db)
    {
        var events = await db
            .Events.OrderByDescending(e => e.CreatedAt)
            .Select(e => ToResponse(e))
            .ToListAsync();
        return TypedResults.Ok(events);
    }

    private static async Task<IResult> GetEvent(int id, AmsterfamDbContext db)
    {
        var ev = await db.Events.FindAsync(id);
        return ev is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(ev));
    }

    private static async Task<IResult> CreateEvent(
        [FromBody] CreateEventRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var user = await currentUser.GetOrCreateAsync();

        var ev = new Event
        {
            Name = request.Name,
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Location = request.Location,
            CostPerNight = request.CostPerNight,
            CreatedById = user.Id,
        };

        db.Events.Add(ev);

        db.EventAttendances.Add(
            new EventAttendance
            {
                Event = ev,
                UserId = user.Id,
                Role = AttendanceRole.Organiser,
            }
        );

        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/v1/events/{ev.Id}", ToResponse(ev));
    }

    private static async Task<IResult> UpdateEvent(
        int id,
        [FromBody] UpdateEventRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiserOrSuperuser(db, id, user.Id))
            return TypedResults.Forbid();

        ev.Name = request.Name;
        ev.Description = request.Description;
        ev.StartDate = request.StartDate;
        ev.EndDate = request.EndDate;
        ev.Location = request.Location;
        ev.CostPerNight = request.CostPerNight;

        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(ev));
    }

    private static async Task<IResult> DeleteEvent(
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiserOrSuperuser(db, id, user.Id))
            return TypedResults.Forbid();

        db.Events.Remove(ev);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> PublishEvent(
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiserOrSuperuser(db, id, user.Id))
            return TypedResults.Forbid();

        if (ev.Status != EventStatus.Draft)
            return TypedResults.Conflict(
                new { error = "Event must be in Draft status to publish." }
            );

        ev.Status = EventStatus.Open;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(ev));
    }

    private static async Task<IResult> CloseEvent(
        int id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiserOrSuperuser(db, id, user.Id))
            return TypedResults.Forbid();

        if (ev.Status != EventStatus.Open)
            return TypedResults.Conflict(new { error = "Event must be in Open status to close." });

        ev.Status = EventStatus.Closed;
        await db.SaveChangesAsync();
        return TypedResults.Ok(ToResponse(ev));
    }

    private static async Task<bool> IsOrganiserOrSuperuser(
        AmsterfamDbContext db,
        int eventId,
        int userId
    )
    {
        return await db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId && a.UserId == userId && a.Role == AttendanceRole.Organiser
        );
    }

    private static EventResponse ToResponse(Event ev) =>
        new(
            ev.Id,
            ev.Name,
            ev.Description,
            ev.StartDate,
            ev.EndDate,
            ev.Location,
            ev.CostPerNight,
            ev.Status.ToString(),
            ev.CreatedAt
        );
}
