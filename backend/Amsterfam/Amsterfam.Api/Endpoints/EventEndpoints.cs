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
        group.MapGet("/{id:guid}", GetEvent);
        group.MapPut("/{id:guid}", UpdateEvent);
        group.MapDelete("/{id:guid}", DeleteEvent);
        group.MapPost("/{id:guid}/status", TransitionEvent);

        return app;
    }

    private static async Task<IResult> GetEvents(
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var user = await currentUser.GetOrCreateAsync();
        var today = time.Today();

        var events = await db
            .Events.Include(e => e.Attendances)
                .ThenInclude(a => a.User)
            .Where(e => e.Attendances.Any(a => a.UserId == user.Id))
            .OrderByDescending(e => e.CreatedAt)
            .AsSplitQuery()
            .ToListAsync();

        return TypedResults.Ok(events.Select(ev => BuildResponse(ev, user.Id, today)).ToList());
    }

    private static async Task<IResult> GetEvent(
        Guid id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time
    )
    {
        var ev = await LoadEventWithOrganisers(db, id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        // Events are only reachable via join link; non-members can't peek by GUID.
        if (ev.Attendances.All(a => a.UserId != user.Id))
            return TypedResults.NotFound();

        return TypedResults.Ok(BuildResponse(ev, user.Id, time.Today()));
    }

    private static async Task<IResult> CreateEvent(
        [FromBody] CreateEventRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var today = time.Today();
        var dateError = EventStateMachine.ValidateTentativeDates(
            request.StartDate,
            request.EndDate,
            today
        );
        if (dateError is not null)
            return TypedResults.BadRequest(new { error = dateError });

        var user = await currentUser.GetOrCreateAsync();

        var ev = new Event
        {
            Name = request.Name,
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Location = request.Location,
            CreatedById = user.Id,
        };

        db.Events.Add(ev);

        db.EventAttendances.Add(
            new EventAttendance
            {
                Event = ev,
                User = user,
                Role = AttendanceRole.Organiser,
            }
        );
        log.Record(ev.Id, EventLogType.EventCreated, user.Id);

        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/v1/events/{ev.Id}", BuildResponse(ev, user.Id, today));
    }

    private static async Task<IResult> UpdateEvent(
        Guid id,
        [FromBody] UpdateEventRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        EventLog log
    )
    {
        var ev = await LoadEventWithOrganisers(db, id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!IsOrganiser(ev, user.Id))
            return TypedResults.Forbid();

        if (EventStateMachine.IsReadOnly(ev.Status))
            return EventGuards.ReadOnlyConflict(ev.Status);

        var today = time.Today();
        var datesChanged = request.StartDate != ev.StartDate || request.EndDate != ev.EndDate;
        if (datesChanged)
        {
            if (EventStateMachine.AreDatesLocked(ev.Status))
                return TypedResults.Conflict(
                    new
                    {
                        error = "Dates are fixed once an event is open. Move it back to Draft or Looking for Date to change them.",
                    }
                );

            var dateError = EventStateMachine.ValidateTentativeDates(
                request.StartDate,
                request.EndDate,
                today
            );
            if (dateError is not null)
                return TypedResults.BadRequest(new { error = dateError });
        }

        var changed = new List<string>();
        if (request.Name != ev.Name)
            changed.Add("name");
        if (request.Description != ev.Description)
            changed.Add("description");
        if (datesChanged)
            changed.Add("dates");
        if (request.Location != ev.Location)
            changed.Add("location");

        ev.Name = request.Name;
        ev.Description = request.Description;
        ev.StartDate = request.StartDate;
        ev.EndDate = request.EndDate;
        ev.Location = request.Location;

        if (changed.Count > 0)
            log.Record(ev.Id, EventLogType.EventDetailsUpdated, user.Id, data: new { changed });

        await db.SaveChangesAsync();
        return TypedResults.Ok(BuildResponse(ev, user.Id, today));
    }

    private static async Task<IResult> DeleteEvent(
        Guid id,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (ev.CreatedById != user.Id)
            return TypedResults.Forbid();

        if (!EventStateMachine.CanDelete(ev.Status))
            return TypedResults.Conflict(
                new { error = "Only archived or cancelled events can be deleted." }
            );

        db.Events.Remove(ev);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    private static async Task<IResult> TransitionEvent(
        Guid id,
        [FromBody] TransitionEventRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db,
        TimeProvider time,
        IEventBalanceCheck balances,
        EventLog log
    )
    {
        // Enum.TryParse would also accept numeric strings, so match on names only.
        if (!Enum.GetNames<EventStatus>().Contains(request.Target))
            return TypedResults.BadRequest(new { error = $"Unknown status '{request.Target}'." });
        var target = Enum.Parse<EventStatus>(request.Target);

        var ev = await LoadEventWithOrganisers(db, id);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        var actor = ActorFor(ev, user.Id);
        if (!actor.IsOrganiser)
            return TypedResults.Forbid();

        if (
            target == EventStatus.Archived
            && ev.Status != EventStatus.Archived
            && await balances.HasOpenBalancesAsync(ev.Id)
        )
            return TypedResults.Conflict(
                new { error = "This event still has open balances. Settle them before archiving." }
            );

        var today = time.Today();
        var from = ev.Status;
        var result = EventStateMachine.TryTransition(ev, target, actor, today);
        switch (result.Outcome)
        {
            case TransitionOutcome.Forbidden:
                return TypedResults.Forbid();
            case TransitionOutcome.NotAllowed:
            case TransitionOutcome.GuardFailed:
                return TypedResults.Conflict(new { error = result.Error });
        }

        if (ev.Status != from)
            log.Record(
                ev.Id,
                EventLogType.StatusChanged,
                user.Id,
                data: new { from = from.ToString(), to = ev.Status.ToString() }
            );

        await db.SaveChangesAsync();
        return TypedResults.Ok(BuildResponse(ev, user.Id, today));
    }

    private static bool IsOrganiser(Event ev, int userId) =>
        ev.Attendances.Any(a => a.UserId == userId && a.Role == AttendanceRole.Organiser);

    private static EventActor ActorFor(Event ev, int userId)
    {
        var isOwner = ev.CreatedById == userId;
        return new EventActor(isOwner || IsOrganiser(ev, userId), isOwner);
    }

    private static Task<Event?> LoadEventWithOrganisers(AmsterfamDbContext db, Guid eventId) =>
        db
            .Events.Include(e => e.Attendances)
                .ThenInclude(a => a.User)
            .FirstOrDefaultAsync(e => e.Id == eventId);

    private static List<OrganiserSummary> OrganisersFrom(Event ev) =>
        ev
            .Attendances.Where(a => a.Role == AttendanceRole.Organiser)
            .Select(a => new OrganiserSummary(
                a.UserId,
                a.User.DisplayName ?? a.User.Handle,
                a.User.AvatarUrl
            ))
            .ToList();

    /// <summary>
    /// Builds the response for the given user. Expects <paramref name="ev"/> to have its
    /// attendances (with users) loaded. Non-members, and non-organisers of a cancelled
    /// event, get a stripped-down preview.
    /// </summary>
    private static EventResponse BuildResponse(Event ev, int userId, DateOnly today)
    {
        var role = ev.Attendances.FirstOrDefault(a => a.UserId == userId)?.Role;
        var organisers = OrganisersFrom(ev);
        var isOrganiser = role == AttendanceRole.Organiser;

        if (role is null || EventStateMachine.IsHiddenFrom(ev.Status, isOrganiser))
            return ToPreviewResponse(ev, role?.ToString(), organisers);

        var allowed = EventStateMachine
            .GetAllowedTransitions(ev, ActorFor(ev, userId), today)
            .Select(s => s.ToString())
            .ToList();

        return new(
            ev.Id,
            ev.Name,
            ev.Description,
            ev.StartDate,
            ev.EndDate,
            ev.Location,
            ev.PollRangeStart,
            ev.PollRangeEnd,
            ev.Status.ToString(),
            ev.CreatedAt,
            role.ToString(),
            true,
            ev.CreatedById,
            organisers,
            allowed,
            ev.AutoTransitionsPaused,
            ev.BannerFileId,
            // Only organisers can act on pending requests, so only they get the count.
            isOrganiser ? ev.Attendances.Count(a => a.Role == AttendanceRole.Pending) : null
        );
    }

    private static EventResponse ToPreviewResponse(
        Event ev,
        string? currentUserRole,
        IReadOnlyList<OrganiserSummary> organisers
    ) =>
        new(
            ev.Id,
            ev.Name,
            null,
            ev.StartDate,
            ev.EndDate,
            ev.Location,
            null,
            null,
            ev.Status.ToString(),
            ev.CreatedAt,
            currentUserRole,
            currentUserRole is not null,
            ev.CreatedById,
            organisers,
            [],
            false
        );
}
