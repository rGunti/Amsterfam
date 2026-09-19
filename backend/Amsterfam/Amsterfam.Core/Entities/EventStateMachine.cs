namespace Amsterfam.Core.Entities;

/// <summary>Who is attempting a transition. The owner is always an organiser too.</summary>
public readonly record struct EventActor(bool IsOrganiser, bool IsOwner)
{
    public static EventActor Member => new(false, false);
    public static EventActor Organiser => new(true, false);
    public static EventActor Owner => new(true, true);
}

public enum TransitionOutcome
{
    Success,
    NotAllowed,
    Forbidden,
    GuardFailed,
}

public readonly record struct TransitionResult(TransitionOutcome Outcome, string? Error = null)
{
    public bool Succeeded => Outcome == TransitionOutcome.Success;
}

/// <summary>
/// Lifecycle rules for an <see cref="Event"/> (see issue #107 and docs/domain.md).
/// Pure logic: callers supply "today" and persist the result.
/// </summary>
public static class EventStateMachine
{
    private readonly record struct Rule(EventStatus From, EventStatus To, bool OwnerOnly);

    private static readonly Rule[] Rules =
    [
        new(EventStatus.Draft, EventStatus.LookingForDate, false),
        new(EventStatus.Draft, EventStatus.Open, false),
        new(EventStatus.Draft, EventStatus.Archived, false),
        new(EventStatus.Draft, EventStatus.Cancelled, true),
        new(EventStatus.LookingForDate, EventStatus.Draft, false),
        new(EventStatus.LookingForDate, EventStatus.Open, false),
        new(EventStatus.LookingForDate, EventStatus.Cancelled, true),
        new(EventStatus.Open, EventStatus.Draft, false),
        new(EventStatus.Open, EventStatus.LookingForDate, false),
        new(EventStatus.Open, EventStatus.InProgress, false),
        new(EventStatus.Open, EventStatus.Archived, false),
        new(EventStatus.Open, EventStatus.Cancelled, true),
        new(EventStatus.InProgress, EventStatus.Closed, false),
        new(EventStatus.InProgress, EventStatus.Archived, false),
        new(EventStatus.InProgress, EventStatus.Cancelled, true),
        new(EventStatus.InProgress, EventStatus.Open, true), // reset (Danger Zone)
        new(EventStatus.Closed, EventStatus.Archived, false),
        new(EventStatus.Closed, EventStatus.Open, true), // reset (Danger Zone)
    ];

    public static bool IsReadOnly(EventStatus status) =>
        status is EventStatus.Archived or EventStatus.Cancelled;

    public static bool AreDatesLocked(EventStatus status) =>
        status is not (EventStatus.Draft or EventStatus.LookingForDate);

    public static bool AcceptsJoins(EventStatus status) =>
        status is EventStatus.LookingForDate or EventStatus.Open;

    public static bool IsPollActive(EventStatus status) => status == EventStatus.LookingForDate;

    public static bool CanSetPollRange(EventStatus status) =>
        status is EventStatus.Draft or EventStatus.LookingForDate;

    /// <summary>Cancelled events are only visible to organisers.</summary>
    public static bool IsHiddenFrom(EventStatus status, bool isOrganiser) =>
        status == EventStatus.Cancelled && !isOrganiser;

    public static bool CanDelete(EventStatus status) => IsReadOnly(status);

    public static bool IsReset(EventStatus from, EventStatus to) =>
        to == EventStatus.Open && from is EventStatus.InProgress or EventStatus.Closed;

    /// <summary>
    /// Validates a start/end date pair for an event about to be fixed in time.
    /// Returns an error message, or null when valid.
    /// </summary>
    public static string? ValidateDates(DateOnly? start, DateOnly? end, DateOnly today)
    {
        if (start is null || end is null)
            return "Start and end date must be set.";
        if (end < start)
            return "End date must not be before the start date.";
        if (start < today)
            return "Start date cannot be in the past.";
        return null;
    }

    /// <summary>
    /// Validates dates while they're still being worked out (Draft / Looking for Date):
    /// either may be unset, but whatever is set has to make sense.
    /// </summary>
    public static string? ValidateTentativeDates(DateOnly? start, DateOnly? end, DateOnly today)
    {
        if (start is not null && end is not null && end < start)
            return "End date must not be before the start date.";
        if (start is not null && start < today)
            return "Start date cannot be in the past.";
        return null;
    }

    public static IReadOnlyList<EventStatus> GetAllowedTransitions(
        Event ev,
        EventActor actor,
        DateOnly today
    ) =>
        Rules
            .Where(r => r.From == ev.Status && IsPermitted(r, actor))
            .Where(r => CheckGuard(ev, r.To, today) is null)
            .Select(r => r.To)
            .ToList();

    public static TransitionResult TryTransition(
        Event ev,
        EventStatus target,
        EventActor actor,
        DateOnly today
    )
    {
        var rule = Rules
            .Cast<Rule?>()
            .FirstOrDefault(r => r!.Value.From == ev.Status && r.Value.To == target);
        if (rule is null)
            return new(
                TransitionOutcome.NotAllowed,
                $"Cannot move an event from {ev.Status} to {target}."
            );

        if (!IsPermitted(rule.Value, actor))
            return new(TransitionOutcome.Forbidden);

        var guardError = CheckGuard(ev, target, today);
        if (guardError is not null)
            return new(TransitionOutcome.GuardFailed, guardError);

        Apply(ev, target);
        return new(TransitionOutcome.Success);
    }

    /// <summary>
    /// The automatic transition due for this event today, if any. Applied repeatedly
    /// until null, so an Open event whose end date has also passed ends up Closed.
    /// </summary>
    public static EventStatus? GetDueAutoTransition(Event ev, DateOnly today)
    {
        if (ev.AutoTransitionsPaused)
            return null;

        return ev.Status switch
        {
            EventStatus.Open when ev.StartDate <= today => EventStatus.InProgress,
            EventStatus.InProgress when ev.EndDate < today => EventStatus.Closed,
            _ => null,
        };
    }

    public static void ApplyAutoTransition(Event ev, EventStatus target) => ev.Status = target;

    private static bool IsPermitted(Rule rule, EventActor actor) =>
        rule.OwnerOnly ? actor.IsOwner : actor.IsOrganiser;

    private static string? CheckGuard(Event ev, EventStatus target, DateOnly today)
    {
        // Resetting back to Open keeps the original dates, even if they're in the past.
        if (target == EventStatus.Open && !IsReset(ev.Status, target))
            return ValidateDates(ev.StartDate, ev.EndDate, today);
        return null;
    }

    private static void Apply(Event ev, EventStatus target)
    {
        // A reset pauses automatic transitions, otherwise a past start date would bounce
        // the event straight back into InProgress. Any other manual move resumes them.
        ev.AutoTransitionsPaused = IsReset(ev.Status, target);
        ev.Status = target;
    }
}
