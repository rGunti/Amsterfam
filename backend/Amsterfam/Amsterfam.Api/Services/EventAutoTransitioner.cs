using Amsterfam.Core.Entities;
using Amsterfam.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Amsterfam.Api.Services;

/// <summary>
/// Moves events along when their dates are reached: Open → In Progress on the start
/// date, In Progress → Closed once the end date has passed. Idempotent.
/// </summary>
public class EventAutoTransitioner(
    AmsterfamDbContext db,
    EventLog log,
    ILogger<EventAutoTransitioner> logger
)
{
    public async Task<int> RunAsync(DateOnly today, CancellationToken ct = default)
    {
        var candidates = await db
            .Events.Where(e =>
                !e.AutoTransitionsPaused
                && (e.Status == EventStatus.Open || e.Status == EventStatus.InProgress)
            )
            .ToListAsync(ct);

        var changed = 0;
        foreach (var ev in candidates)
        {
            var from = ev.Status;
            while (EventStateMachine.GetDueAutoTransition(ev, today) is { } next)
            {
                var step = ev.Status;
                EventStateMachine.ApplyAutoTransition(ev, next);
                log.Record(
                    ev.Id,
                    EventLogType.StatusChanged,
                    actorId: null,
                    data: new { from = step.ToString(), to = next.ToString() }
                );
            }

            if (ev.Status == from)
                continue;

            changed++;
            logger.LogInformation(
                "Auto-transitioned event {EventId} from {From} to {To}",
                ev.Id,
                from,
                ev.Status
            );
        }

        if (changed > 0)
            await db.SaveChangesAsync(ct);
        return changed;
    }
}
