using Amsterfam.Api.Dtos;
using Amsterfam.Api.Services;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Endpoints;

public static class DatePollEndpoints
{
    public static IEndpointRouteBuilder MapDatePollEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/events/{eventId:int}/date-poll").RequireAuthorization();

        group.MapPut("/range", SetPollRange);
        group.MapGet("/", GetSummary);
        group.MapGet("/me", GetMyEntries);
        group.MapPut("/me", UpdateMyEntries);
        group.MapDelete("/me/{weekStart}", DeleteMyEntry);

        return app;
    }

    private static async Task<IResult> SetPollRange(
        int eventId,
        [FromBody] UpdatePollRangeRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsOrganiserOrSuperuser(db, eventId, user.Id))
            return TypedResults.Forbid();

        if (ev.Status != EventStatus.Draft)
            return TypedResults.Conflict(
                new { error = "Poll range can only be set while the event is in Draft status." }
            );

        if (
            request.PollRangeStart is not null
            && request.PollRangeEnd is not null
            && request.PollRangeStart >= request.PollRangeEnd
        )
            return TypedResults.BadRequest(
                new { error = "PollRangeStart must be before PollRangeEnd." }
            );

        ev.PollRangeStart = request.PollRangeStart;
        ev.PollRangeEnd = request.PollRangeEnd;

        await db.SaveChangesAsync();
        return TypedResults.Ok(await BuildSummary(db, ev));
    }

    private static async Task<IResult> GetSummary(
        int eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsConfirmedMember(db, eventId, user.Id))
            return TypedResults.Forbid();

        return TypedResults.Ok(await BuildSummary(db, ev));
    }

    private static async Task<IResult> GetMyEntries(
        int eventId,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsConfirmedMember(db, eventId, user.Id))
            return TypedResults.Forbid();

        var entries = await db
            .DatePollEntries.Where(e => e.EventId == eventId && e.UserId == user.Id)
            .Select(e => new DatePollEntryDto(e.WeekStart, e.Status.ToString()))
            .ToListAsync();

        return TypedResults.Ok(entries);
    }

    private static async Task<IResult> UpdateMyEntries(
        int eventId,
        [FromBody] UpdateDatePollEntriesRequest request,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsConfirmedMember(db, eventId, user.Id))
            return TypedResults.Forbid();

        if (ev.PollRangeStart is null || ev.PollRangeEnd is null)
            return TypedResults.Conflict(new { error = "This event has no poll range set." });

        var firstSelectableMonday = FirstMonday(ev.PollRangeStart.Value);

        foreach (var entry in request.Entries)
        {
            if (FirstMonday(entry.WeekStart) != entry.WeekStart)
                return TypedResults.BadRequest(
                    new { error = $"WeekStart {entry.WeekStart} is not a Monday." }
                );

            // Compare against the Monday-aligned start of the range, not the raw
            // PollRangeStart — a boundary week can legitimately start before a
            // non-Monday PollRangeStart while still containing an in-range day
            // (mirrors the week generation in BuildSummary below).
            if (entry.WeekStart < firstSelectableMonday || entry.WeekStart >= ev.PollRangeEnd)
                return TypedResults.BadRequest(
                    new { error = $"WeekStart {entry.WeekStart} is outside the poll range." }
                );

            if (!Enum.TryParse<DatePollStatus>(entry.Status, out var status))
                return TypedResults.BadRequest(new { error = $"Invalid status '{entry.Status}'." });

            var existing = await db.DatePollEntries.FirstOrDefaultAsync(e =>
                e.EventId == eventId && e.UserId == user.Id && e.WeekStart == entry.WeekStart
            );

            if (existing is null)
            {
                db.DatePollEntries.Add(
                    new DatePollEntry
                    {
                        EventId = eventId,
                        UserId = user.Id,
                        WeekStart = entry.WeekStart,
                        Status = status,
                    }
                );
            }
            else
            {
                existing.Status = status;
            }
        }

        await db.SaveChangesAsync();

        var entries = await db
            .DatePollEntries.Where(e => e.EventId == eventId && e.UserId == user.Id)
            .Select(e => new DatePollEntryDto(e.WeekStart, e.Status.ToString()))
            .ToListAsync();

        return TypedResults.Ok(entries);
    }

    private static async Task<IResult> DeleteMyEntry(
        int eventId,
        string weekStart,
        ICurrentUserService currentUser,
        AmsterfamDbContext db
    )
    {
        var ev = await db.Events.FindAsync(eventId);
        if (ev is null)
            return TypedResults.NotFound();

        if (!DateOnly.TryParseExact(weekStart, "yyyy-MM-dd", out var parsedWeekStart))
            return TypedResults.BadRequest(new { error = $"Invalid weekStart '{weekStart}'." });

        var user = await currentUser.GetOrCreateAsync();
        if (!await IsConfirmedMember(db, eventId, user.Id))
            return TypedResults.Forbid();

        var existing = await db.DatePollEntries.FirstOrDefaultAsync(e =>
            e.EventId == eventId && e.UserId == user.Id && e.WeekStart == parsedWeekStart
        );

        if (existing is not null)
        {
            db.DatePollEntries.Remove(existing);
            await db.SaveChangesAsync();
        }

        return TypedResults.NoContent();
    }

    private static async Task<DatePollSummaryResponse> BuildSummary(AmsterfamDbContext db, Event ev)
    {
        if (ev.PollRangeStart is null || ev.PollRangeEnd is null)
            return new DatePollSummaryResponse(ev.PollRangeStart, ev.PollRangeEnd, []);

        var confirmedCount = await db.EventAttendances.CountAsync(a =>
            a.EventId == ev.Id
            && (a.Role == AttendanceRole.Organiser || a.Role == AttendanceRole.Attendee)
        );

        var entries = await db
            .DatePollEntries.Where(e => e.EventId == ev.Id)
            .GroupBy(e => e.WeekStart)
            .Select(g => new
            {
                WeekStart = g.Key,
                Available = g.Count(e => e.Status == DatePollStatus.Available),
                Unavailable = g.Count(e => e.Status == DatePollStatus.Unavailable),
                Partial = g.Count(e => e.Status == DatePollStatus.Partial),
            })
            .ToDictionaryAsync(g => g.WeekStart);

        var weeks = new List<DatePollWeekSummary>();
        for (
            var weekStart = FirstMonday(ev.PollRangeStart.Value);
            weekStart < ev.PollRangeEnd.Value;
            weekStart = weekStart.AddDays(7)
        )
        {
            entries.TryGetValue(weekStart, out var counts);
            var responded =
                (counts?.Available ?? 0) + (counts?.Unavailable ?? 0) + (counts?.Partial ?? 0);
            weeks.Add(
                new DatePollWeekSummary(
                    weekStart,
                    counts?.Available ?? 0,
                    counts?.Unavailable ?? 0,
                    counts?.Partial ?? 0,
                    Math.Max(0, confirmedCount - responded)
                )
            );
        }

        return new DatePollSummaryResponse(ev.PollRangeStart, ev.PollRangeEnd, weeks);
    }

    private static DateOnly FirstMonday(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.AddDays(-offset);
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

    private static async Task<bool> IsConfirmedMember(
        AmsterfamDbContext db,
        int eventId,
        int userId
    )
    {
        return await db.EventAttendances.AnyAsync(a =>
            a.EventId == eventId
            && a.UserId == userId
            && (a.Role == AttendanceRole.Organiser || a.Role == AttendanceRole.Attendee)
        );
    }
}
