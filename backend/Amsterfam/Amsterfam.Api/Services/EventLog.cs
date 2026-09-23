using System.Text.Json;
using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Services;

/// <summary>
/// Records changes on an event's timeline. Entries are only added to the context, so they
/// are saved (or not) together with the change itself by the caller's SaveChanges.
/// </summary>
public class EventLog(AmsterfamDbContext db, TimeProvider time)
{
    /// <summary>Repeated poll saves by the same person within this window share one entry.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        Guid eventId,
        EventLogType type,
        int? actorId,
        int? subjectUserId = null,
        object? data = null,
        EventLogVisibility? visibility = null
    ) =>
        db.EventLogEntries.Add(
            new EventLogEntry
            {
                EventId = eventId,
                Type = type,
                Visibility = visibility ?? EventLogEntry.DefaultVisibility(type),
                OccurredAt = time.GetUtcNow(),
                ActorId = actorId,
                SubjectUserId = subjectUserId,
                Data = data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            }
        );

    /// <summary>
    /// Like <see cref="Record"/>, but if the event's latest entry is the same type by the same
    /// actor and recent, bumps its timestamp instead. Keeps a flurry of small saves (e.g. a
    /// poll edit sent as several requests) from flooding the timeline.
    /// </summary>
    public async Task RecordCoalescedAsync(Guid eventId, EventLogType type, int actorId)
    {
        var now = time.GetUtcNow();
        var latest = await db
            .EventLogEntries.Where(l => l.EventId == eventId)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync();

        if (
            latest is not null
            && latest.Type == type
            && latest.ActorId == actorId
            && now - latest.OccurredAt < CoalesceWindow
        )
        {
            latest.OccurredAt = now;
            return;
        }

        Record(eventId, type, actorId);
    }
}
